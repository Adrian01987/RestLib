using System.Linq.Expressions;
using System.Text;
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
    private const char LikeEscapeCharacter = '\\';

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

        var propertyAccess = ExpressionBuilder.BuildPropertyAccess<TEntity>(filter.PropertyName);
        var underlyingType = Nullable.GetUnderlyingType(propertyAccess.ReturnType) ?? propertyAccess.ReturnType;
        if (underlyingType != typeof(string))
        {
            throw new InvalidOperationException(
                $"String filter operators can only be applied to string properties, "
                + $"but property '{filter.PropertyName}' on entity type '{typeof(TEntity).Name}' "
                + $"is of type '{propertyAccess.ReturnType.Name}'.");
        }

        var filterString = filter.TypedValue?.ToString() ?? filter.RawValue;
        var parameter = propertyAccess.Parameters[0];
        var normalizedProperty = StringQuerySemantics.Normalize(propertyAccess.Body);
        var escapedFilterString = EscapeLikePattern(StringQuerySemantics.Normalize(filterString));
        var pattern = filter.Operator switch
        {
            FilterOperator.Contains => $"%{escapedFilterString}%",
            FilterOperator.StartsWith => $"{escapedFilterString}%",
            FilterOperator.EndsWith => $"%{escapedFilterString}",
            _ => throw new NotSupportedException(
                $"Filter operator '{filter.Operator}' is not supported by StringFilterBuilder. "
                + "Only string operators (Contains, StartsWith, EndsWith) are supported."),
        };

        var method = typeof(DbFunctionsExtensions).GetMethod(
            nameof(DbFunctionsExtensions.Like),
            [typeof(DbFunctions), typeof(string), typeof(string), typeof(string)])
            ?? throw new InvalidOperationException(
                "Method 'Like(DbFunctions, string, string, string)' was not found on type 'DbFunctionsExtensions'.");

        var functions = Expression.Property(null, typeof(EF), nameof(EF.Functions));
        var patternConstant = Expression.Constant(pattern, typeof(string));
        var escapeConstant = Expression.Constant(LikeEscapeCharacter.ToString(), typeof(string));
        var methodCall = Expression.Call(method, functions, normalizedProperty, patternConstant, escapeConstant);
        var notNull = Expression.NotEqual(propertyAccess.Body, Expression.Constant(null, typeof(string)));
        var body = Expression.AndAlso(notNull, methodCall);

        return Expression.Lambda<Func<TEntity, bool>>(body, parameter);
    }

    private static string EscapeLikePattern(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            if (character is LikeEscapeCharacter or '%' or '_' or '[' or ']' or '^')
            {
                builder.Append(LikeEscapeCharacter);
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}
