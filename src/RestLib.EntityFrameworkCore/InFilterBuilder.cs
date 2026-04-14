using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using RestLib.Filtering;

namespace RestLib.EntityFrameworkCore;

/// <summary>
/// Builds In filter predicates as LINQ expression trees.
/// Translates <see cref="FilterValue"/> objects with the <see cref="FilterOperator.In"/>
/// operator into <c>Expression&lt;Func&lt;TEntity, bool&gt;&gt;</c> predicates suitable
/// for <c>IQueryable&lt;T&gt;.Where()</c>.
/// </summary>
internal static class InFilterBuilder
{
    /// <summary>
    /// Builds an In filter predicate expression for the specified filter.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="filter">
    /// The filter value containing the property name, operator, and list of values.
    /// Must have the <see cref="FilterOperator.In"/> operator and a non-null, non-empty
    /// <see cref="FilterValue.TypedValues"/> list.
    /// </param>
    /// <returns>
    /// An expression tree representing an In filter predicate, e.g.
    /// <c>entity =&gt; values.Contains(entity.Status)</c>, which EF Core translates to
    /// SQL <c>WHERE Status IN ('Active', 'Pending')</c>.
    /// </returns>
    /// <exception cref="NotSupportedException">
    /// Thrown when <paramref name="filter"/> has a non-In operator.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the property specified by <see cref="FilterValue.PropertyName"/>
    /// does not exist on <typeparamref name="TEntity"/>, or when
    /// <see cref="FilterValue.TypedValues"/> is null or empty.
    /// </exception>
    public static Expression<Func<TEntity, bool>> BuildPredicate<TEntity>(FilterValue filter)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(filter);

        if (filter.Operator != FilterOperator.In)
        {
            throw new NotSupportedException(
                $"Filter operator '{filter.Operator}' is not supported by InFilterBuilder. Only the In operator is supported.");
        }

        var property = typeof(TEntity).GetProperty(
            filter.PropertyName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
            ?? throw new InvalidOperationException(
                $"Property '{filter.PropertyName}' was not found on entity type '{typeof(TEntity).Name}'.");

        if (filter.TypedValues is null || filter.TypedValues.Count == 0)
        {
            throw new InvalidOperationException("FilterValue.TypedValues must be non-null and non-empty for the In operator.");
        }

        var list = CreateTypedList(filter.TypedValues, property.PropertyType);
        var parameter = Expression.Parameter(typeof(TEntity), "entity");
        var propertyAccess = Expression.Property(parameter, property);
        var listType = list.GetType();
        var elementType = listType.GetGenericArguments()[0];
        var containsMethod = listType.GetMethod(nameof(List<object>.Contains), [elementType])
            ?? throw new InvalidOperationException(
                $"Method 'Contains({elementType.Name})' was not found on type '{listType.Name}'.");

        Expression valueExpression = propertyAccess;
        if (propertyAccess.Type != elementType)
        {
            valueExpression = Expression.Convert(propertyAccess, elementType);
        }

        var containsCall = Expression.Call(Expression.Constant(list, listType), containsMethod, valueExpression);

        return Expression.Lambda<Func<TEntity, bool>>(containsCall, parameter);
    }

    private static object CreateTypedList(IReadOnlyList<object?> values, Type propertyType)
    {
        var targetType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        var listType = typeof(List<>).MakeGenericType(targetType);
        var list = (IList)Activator.CreateInstance(listType)!;

        foreach (var value in values)
        {
            list.Add(value);
        }

        return list;
    }
}
