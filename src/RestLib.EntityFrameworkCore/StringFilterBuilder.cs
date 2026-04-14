using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using RestLib.Filtering;

namespace RestLib.EntityFrameworkCore;

/// <summary>
/// Builds string filter predicates as LINQ expression trees.
/// Translates <see cref="FilterValue"/> objects with string operators
/// (Contains, StartsWith, EndsWith) into <c>Expression&lt;Func&lt;TEntity, bool&gt;&gt;</c>
/// predicates suitable for <c>IQueryable&lt;T&gt;.Where()</c>.
/// </summary>
internal static class StringFilterBuilder
{
    /// <summary>
    /// Builds a string filter predicate expression for the specified filter.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="filter">
    /// The filter value containing the property name, operator, and search string.
    /// Must have a string operator (Contains, StartsWith, or EndsWith).
    /// </param>
    /// <returns>
    /// An expression tree representing a string filter predicate, e.g.
    /// <c>entity =&gt; entity.Name.Contains("widget")</c>.
    /// </returns>
    /// <exception cref="NotSupportedException">
    /// Thrown when <paramref name="filter"/> has a non-string operator
    /// (Eq, Neq, Gt, Lt, Gte, Lte, or In).
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the property specified by <see cref="FilterValue.PropertyName"/>
    /// does not exist on <typeparamref name="TEntity"/>, or when the property type
    /// is not <see cref="string"/>.
    /// </exception>
    public static Expression<Func<TEntity, bool>> BuildPredicate<TEntity>(FilterValue filter)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(filter);

        var property = typeof(TEntity).GetProperty(
            filter.PropertyName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
            ?? throw new InvalidOperationException(
                $"Property '{filter.PropertyName}' was not found on entity type '{typeof(TEntity).Name}'.");

        var underlyingType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        if (underlyingType != typeof(string))
        {
            throw new InvalidOperationException(
                $"String filter operators can only be applied to string properties, "
                + $"but property '{filter.PropertyName}' on entity type '{typeof(TEntity).Name}' "
                + $"is of type '{property.PropertyType.Name}'.");
        }

        var filterString = filter.TypedValue?.ToString() ?? filter.RawValue;
        var parameter = Expression.Parameter(typeof(TEntity), "entity");
        var propertyAccess = Expression.Property(parameter, property);
        var pattern = filter.Operator switch
        {
            FilterOperator.Contains => $"%{filterString}%",
            FilterOperator.StartsWith => $"{filterString}%",
            FilterOperator.EndsWith => $"%{filterString}",
            _ => throw new NotSupportedException(
                $"Filter operator '{filter.Operator}' is not supported by StringFilterBuilder. "
                + "Only string operators (Contains, StartsWith, EndsWith) are supported."),
        };

        var method = typeof(DbFunctionsExtensions).GetMethod(
            nameof(DbFunctionsExtensions.Like),
            [typeof(DbFunctions), typeof(string), typeof(string)])
            ?? throw new InvalidOperationException(
                "Method 'Like(DbFunctions, string, string)' was not found on type 'DbFunctionsExtensions'.");

        var functions = Expression.Property(null, typeof(EF), nameof(EF.Functions));
        var patternConstant = Expression.Constant(pattern, typeof(string));
        var methodCall = Expression.Call(method, functions, propertyAccess, patternConstant);

        return Expression.Lambda<Func<TEntity, bool>>(methodCall, parameter);
    }
}
