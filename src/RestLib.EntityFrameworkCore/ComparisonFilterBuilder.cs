using System.Linq.Expressions;
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

        var propertyAccess = ExpressionBuilder.BuildPropertyAccess<TEntity>(filter.PropertyName);
        var parameter = propertyAccess.Parameters[0];
        var filterValue = filter.TypedValue;
        if (filterValue is null && !string.IsNullOrEmpty(filter.RawValue))
        {
            var (success, convertedValue, _) = FilterParser.TryConvertValue(filter.RawValue, propertyAccess.ReturnType);
            filterValue = success ? convertedValue : null;
        }

        var constant = CreateConstantExpression(filterValue, propertyAccess.ReturnType);

        var comparison = filter.Operator switch
        {
            FilterOperator.Eq => Expression.Equal(propertyAccess.Body, constant),
            FilterOperator.Neq => Expression.NotEqual(propertyAccess.Body, constant),
            FilterOperator.Gt => Expression.GreaterThan(propertyAccess.Body, constant),
            FilterOperator.Lt => Expression.LessThan(propertyAccess.Body, constant),
            FilterOperator.Gte => Expression.GreaterThanOrEqual(propertyAccess.Body, constant),
            FilterOperator.Lte => Expression.LessThanOrEqual(propertyAccess.Body, constant),
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
}
