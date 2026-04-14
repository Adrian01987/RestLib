using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using RestLib.Filtering;

namespace RestLib.EntityFrameworkCore;

/// <summary>
/// Builds comparison filter predicates as LINQ expression trees.
/// Translates <see cref="FilterValue"/> objects with comparison operators
/// (Eq, Neq, Gt, Lt, Gte, Lte) into <c>Expression&lt;Func&lt;TEntity, bool&gt;&gt;</c>
/// predicates suitable for <c>IQueryable&lt;T&gt;.Where()</c>.
/// </summary>
internal static class ComparisonFilterBuilder
{
    /// <summary>
    /// Builds a comparison predicate expression for the specified filter.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="filter">
    /// The filter value containing the property name, operator, and comparison value.
    /// Must have a comparison operator (Eq, Neq, Gt, Lt, Gte, or Lte).
    /// </param>
    /// <returns>
    /// An expression tree representing a comparison predicate, e.g.
    /// <c>entity =&gt; entity.Price &gt;= 10m</c>.
    /// </returns>
    /// <exception cref="NotSupportedException">
    /// Thrown when <paramref name="filter"/> has a non-comparison operator
    /// (Contains, StartsWith, EndsWith, or In).
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the property specified by <see cref="FilterValue.PropertyName"/>
    /// does not exist on <typeparamref name="TEntity"/>.
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

        var filterValue = filter.TypedValue ?? ConvertRawValue(filter.RawValue, property.PropertyType);
        var parameter = Expression.Parameter(typeof(TEntity), "entity");
        var propertyAccess = Expression.Property(parameter, property);
        var constant = CreateConstantExpression(filterValue, property.PropertyType);

        var comparison = filter.Operator switch
        {
            FilterOperator.Eq => Expression.Equal(propertyAccess, constant),
            FilterOperator.Neq => Expression.NotEqual(propertyAccess, constant),
            FilterOperator.Gt => Expression.GreaterThan(propertyAccess, constant),
            FilterOperator.Lt => Expression.LessThan(propertyAccess, constant),
            FilterOperator.Gte => Expression.GreaterThanOrEqual(propertyAccess, constant),
            FilterOperator.Lte => Expression.LessThanOrEqual(propertyAccess, constant),
            _ => throw new NotSupportedException(
                $"Filter operator '{filter.Operator}' is not supported by ComparisonFilterBuilder. "
                + "Only comparison operators (Eq, Neq, Gt, Lt, Gte, Lte) are supported."),
        };

        return Expression.Lambda<Func<TEntity, bool>>(comparison, parameter);
    }

    private static Expression CreateConstantExpression(object? value, Type targetType)
    {
        var underlyingType = Nullable.GetUnderlyingType(targetType);

        if (value is not null && underlyingType is not null && value.GetType() == underlyingType)
        {
            return Expression.Convert(Expression.Constant(value, underlyingType), targetType);
        }

        return Expression.Constant(value, targetType);
    }

    private static object ConvertRawValue(string rawValue, Type targetType)
    {
        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (underlyingType == typeof(Guid))
        {
            return Guid.Parse(rawValue);
        }

        if (underlyingType == typeof(DateTimeOffset))
        {
            return DateTimeOffset.Parse(rawValue, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        }

        return Convert.ChangeType(rawValue, underlyingType, CultureInfo.InvariantCulture)!;
    }
}
