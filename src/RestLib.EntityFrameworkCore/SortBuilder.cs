using System.Linq.Expressions;
using System.Reflection;
using RestLib.Sorting;

namespace RestLib.EntityFrameworkCore;

/// <summary>
/// Builds sort expressions as <see cref="IOrderedQueryable{T}"/> chains.
/// Translates <see cref="SortField"/> objects into <c>OrderBy</c>, <c>ThenBy</c>,
/// <c>OrderByDescending</c>, and <c>ThenByDescending</c> calls suitable for EF Core
/// server-side SQL <c>ORDER BY</c> translation.
/// </summary>
internal static class SortBuilder
{
    private static readonly MethodInfo OrderByMethod = GetQueryableMethod(nameof(Queryable.OrderBy));
    private static readonly MethodInfo OrderByDescendingMethod = GetQueryableMethod(nameof(Queryable.OrderByDescending));
    private static readonly MethodInfo ThenByMethod = GetQueryableMethod(nameof(Queryable.ThenBy));
    private static readonly MethodInfo ThenByDescendingMethod = GetQueryableMethod(nameof(Queryable.ThenByDescending));

    /// <summary>
    /// Applies sorting to the query based on the provided sort fields and a scalar key selector.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The primary key type.</typeparam>
    /// <param name="query">The queryable to sort.</param>
    /// <param name="sortFields">The sort fields to apply.</param>
    /// <param name="keySelector">The key selector appended as a deterministic tie-breaker.</param>
    /// <returns>An ordered queryable with all sort fields and the key tie-breaker applied.</returns>
    public static IOrderedQueryable<TEntity> ApplySorting<TEntity, TKey>(
        IQueryable<TEntity> query,
        IReadOnlyList<SortField> sortFields,
        Expression<Func<TEntity, TKey>> keySelector)
        where TEntity : class
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(sortFields);
        ArgumentNullException.ThrowIfNull(keySelector);

        if (sortFields.Count == 0)
        {
            return query.OrderBy(keySelector);
        }

        IOrderedQueryable<TEntity>? orderedQuery = null;

        foreach (var sortField in sortFields)
        {
            var propertyAccess = ExpressionBuilder.BuildPropertyAccess<TEntity>(sortField.PropertyName);
            var method = GetSortMethod(sortField.Direction, orderedQuery is null);

            if (orderedQuery is null)
            {
                orderedQuery = ApplyOrdering<TEntity>(method, query, propertyAccess);
            }
            else
            {
                orderedQuery = ApplyOrdering<TEntity>(method, orderedQuery, propertyAccess);
            }
        }

        return orderedQuery!.ThenBy(keySelector);
    }

    /// <summary>
    /// Applies sorting to the query based on the provided sort fields and ordered key parts.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="query">The queryable to sort.</param>
    /// <param name="sortFields">
    /// The sort fields to apply. When empty, the query is ordered by the key parts only.
    /// Each field's <see cref="SortField.PropertyName"/> is resolved via
    /// <see cref="ExpressionBuilder.BuildPropertyAccess{TEntity}(string)"/>.
    /// </param>
    /// <param name="keySelectors">
    /// The ordered key-part selectors appended as tie-breakers for deterministic
    /// cursor pagination.
    /// </param>
    /// <returns>An ordered queryable with all sort fields and the key tie-breaker applied.</returns>
    public static IOrderedQueryable<TEntity> ApplySorting<TEntity>(
        IQueryable<TEntity> query,
        IReadOnlyList<SortField> sortFields,
        IReadOnlyList<SortKeyPart> keySelectors)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(sortFields);
        ArgumentNullException.ThrowIfNull(keySelectors);

        if (keySelectors.Count == 0)
        {
            throw new InvalidOperationException(
                $"Entity type '{typeof(TEntity).Name}' must define at least one key selector for sorting.");
        }

        if (sortFields.Count == 0)
        {
            return ApplyKeyOrdering(query, keySelectors);
        }

        IOrderedQueryable<TEntity>? orderedQuery = null;

        foreach (var sortField in sortFields)
        {
            var propertyAccess = ExpressionBuilder.BuildPropertyAccess<TEntity>(sortField.PropertyName);
            var method = GetSortMethod(sortField.Direction, orderedQuery is null);

            if (orderedQuery is null)
            {
                orderedQuery = ApplyOrdering<TEntity>(method, query, propertyAccess);
            }
            else
            {
                orderedQuery = ApplyOrdering<TEntity>(method, orderedQuery, propertyAccess);
            }
        }

        foreach (var keySelector in keySelectors)
        {
            if (sortFields.Any(sortField =>
                string.Equals(sortField.PropertyName, keySelector.PropertyName, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            orderedQuery = ApplyOrdering<TEntity>(ThenByMethod, orderedQuery!, keySelector.Selector);
        }

        return orderedQuery!;
    }

    private static IOrderedQueryable<TEntity> ApplyKeyOrdering<TEntity>(
        IQueryable<TEntity> query,
        IReadOnlyList<SortKeyPart> keySelectors)
        where TEntity : class
    {
        IOrderedQueryable<TEntity>? orderedQuery = null;

        foreach (var keySelector in keySelectors)
        {
            var method = orderedQuery is null ? OrderByMethod : ThenByMethod;
            orderedQuery = ApplyOrdering<TEntity>(method, orderedQuery ?? query, keySelector.Selector);
        }

        return orderedQuery!;
    }

    private static IOrderedQueryable<TEntity> ApplyOrdering<TEntity>(
        MethodInfo method,
        IQueryable<TEntity> source,
        LambdaExpression keySelector)
        where TEntity : class
    {
        var genericMethod = method.MakeGenericMethod(typeof(TEntity), keySelector.ReturnType);

        return (IOrderedQueryable<TEntity>)genericMethod.Invoke(null, [source, keySelector])!;
    }

    private static MethodInfo GetQueryableMethod(string name)
    {
        return typeof(Queryable)
            .GetMethods()
            .Single(method =>
                method.Name == name &&
                method.IsGenericMethodDefinition &&
                method.GetGenericArguments().Length == 2 &&
                method.GetParameters().Length == 2);
    }

    private static MethodInfo GetSortMethod(SortDirection direction, bool isPrimarySort)
    {
        return (direction, isPrimarySort) switch
        {
            (SortDirection.Asc, true) => OrderByMethod,
            (SortDirection.Desc, true) => OrderByDescendingMethod,
            (SortDirection.Asc, false) => ThenByMethod,
            _ => ThenByDescendingMethod,
        };
    }

    /// <summary>
    /// Represents one ordered key-part selector appended as a stable sort tie-breaker.
    /// </summary>
    /// <param name="PropertyName">The CLR property name of the key part.</param>
    /// <param name="Selector">The selector expression for the key part.</param>
    internal sealed record SortKeyPart(string PropertyName, LambdaExpression Selector);
}
