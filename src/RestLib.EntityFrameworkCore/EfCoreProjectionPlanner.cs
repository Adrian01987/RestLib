using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using RestLib.FieldSelection;
using RestLib.Filtering;
using RestLib.Internal;
using RestLib.Search;
using RestLib.Sorting;

namespace RestLib.EntityFrameworkCore;

/// <summary>
/// Plans EF Core field-selection projections and navigation-loading fallbacks.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
internal sealed class EfCoreProjectionPlanner<TEntity>
    where TEntity : class
{
    private readonly Func<bool> _projectionPushdownEnabled;
    private readonly IReadOnlyList<string> _keyPropertyNames;

    /// <summary>
    /// Initializes a new instance of the <see cref="EfCoreProjectionPlanner{TEntity}"/> class.
    /// </summary>
    /// <param name="projectionPushdownEnabled">
    /// Resolves whether projection pushdown is enabled for the current repository options.
    /// </param>
    /// <param name="keyPropertyNames">The key properties required in every projected entity.</param>
    internal EfCoreProjectionPlanner(
        Func<bool> projectionPushdownEnabled,
        IReadOnlyList<string> keyPropertyNames)
    {
        _projectionPushdownEnabled = projectionPushdownEnabled
            ?? throw new ArgumentNullException(nameof(projectionPushdownEnabled));
        _keyPropertyNames = keyPropertyNames
            ?? throw new ArgumentNullException(nameof(keyPropertyNames));
    }

    /// <summary>
    /// Attempts to build a server-side scalar projection for the requested query shape.
    /// </summary>
    /// <param name="selectedFields">The selected response fields.</param>
    /// <param name="filters">The active filters.</param>
    /// <param name="sortFields">The active sort fields.</param>
    /// <param name="search">The active search request, if any.</param>
    /// <param name="projectionPlan">The projection plan when pushdown is safe.</param>
    /// <returns><see langword="true"/> when a projection plan was built.</returns>
    internal bool TryBuild(
        IReadOnlyList<SelectedField> selectedFields,
        IReadOnlyList<FilterValue> filters,
        IReadOnlyList<SortField> sortFields,
        SearchRequest? search,
        out EfCoreProjectionPlan<TEntity>? projectionPlan)
    {
        if (!_projectionPushdownEnabled() || selectedFields.Count == 0)
        {
            projectionPlan = null;
            return false;
        }

        if (search is not null
            || selectedFields.Any(field => IsNestedPath(field.PropertyName))
            || filters.Any(filter => IsNestedPath(filter.PropertyName))
            || sortFields.Any(sortField => IsNestedPath(sortField.PropertyName)))
        {
            projectionPlan = null;
            return false;
        }

        var requiredProperties = new HashSet<string>(StringComparer.Ordinal);

        foreach (var keyPropertyName in _keyPropertyNames)
        {
            requiredProperties.Add(keyPropertyName);
        }

        foreach (var field in selectedFields)
        {
            requiredProperties.Add(field.PropertyName);
        }

        foreach (var filter in filters)
        {
            requiredProperties.Add(filter.PropertyName);
        }

        foreach (var sortField in sortFields)
        {
            requiredProperties.Add(sortField.PropertyName);
        }

        var properties = new List<PropertyInfo>(requiredProperties.Count);
        foreach (var propertyName in requiredProperties)
        {
            var property = typeof(TEntity).GetProperty(
                propertyName,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (property is null || !property.CanRead || !property.CanWrite || !IsProjectableProperty(property))
            {
                projectionPlan = null;
                return false;
            }

            properties.Add(property);
        }

        var parameter = Expression.Parameter(typeof(TEntity), "entity");
        var bindings = properties
            .Select(property => Expression.Bind(property, Expression.Property(parameter, property)))
            .ToArray();
        var body = Expression.MemberInit(Expression.New(typeof(TEntity)), bindings);
        var selector = Expression.Lambda<Func<TEntity, TEntity>>(body, parameter);

        projectionPlan = new EfCoreProjectionPlan<TEntity>(properties, selector);
        return true;
    }

    /// <summary>
    /// Applies a scalar projection plan to a prepared query.
    /// </summary>
    /// <param name="query">The prepared query.</param>
    /// <param name="projectionPlan">The projection plan.</param>
    /// <returns>The no-tracking projected query.</returns>
    internal IQueryable<TEntity> BuildQuery(
        IQueryable<TEntity> query,
        EfCoreProjectionPlan<TEntity> projectionPlan)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(projectionPlan);

        return query.AsNoTracking().Select(projectionPlan.Selector);
    }

    /// <summary>
    /// Applies the navigation includes required by a projection fallback.
    /// </summary>
    /// <param name="query">The prepared query.</param>
    /// <param name="includePaths">The CLR navigation paths to include.</param>
    /// <returns>The query with all includes applied.</returns>
    internal IQueryable<TEntity> ApplyIncludes(
        IQueryable<TEntity> query,
        IReadOnlyList<string> includePaths)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(includePaths);

        foreach (var includePath in includePaths)
        {
            query = query.Include(includePath);
        }

        return query;
    }

    /// <summary>
    /// Resolves the navigation paths required for nested selected fields.
    /// </summary>
    /// <param name="selectedFields">The selected response fields.</param>
    /// <param name="includePaths">The resolved CLR navigation paths.</param>
    /// <returns><see langword="true"/> when at least one navigation path was resolved.</returns>
    internal bool TryBuildNavigationLoadPaths(
        IReadOnlyList<SelectedField> selectedFields,
        out IReadOnlyList<string> includePaths)
    {
        ArgumentNullException.ThrowIfNull(selectedFields);

        var paths = new HashSet<string>(StringComparer.Ordinal);

        foreach (var field in selectedFields)
        {
            if (!IsNestedPath(field.PropertyName))
            {
                continue;
            }

            var propertyPath = NamingUtils.ResolvePropertyPath<TEntity>(
                field.PropertyName,
                nameof(selectedFields));
            if (propertyPath.ClrSegments.Count < 2)
            {
                continue;
            }

            paths.Add(string.Join('.', propertyPath.ClrSegments.Take(propertyPath.ClrSegments.Count - 1)));
        }

        includePaths = paths.ToList();
        return includePaths.Count > 0;
    }

    private static bool IsNestedPath(string propertyPath)
    {
        return propertyPath.Contains('.', StringComparison.Ordinal);
    }

    private static bool IsProjectableProperty(PropertyInfo property)
    {
        var propertyType = property.PropertyType;
        var underlyingType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        return underlyingType.IsEnum
            || underlyingType.IsPrimitive
            || underlyingType == typeof(string)
            || underlyingType == typeof(decimal)
            || underlyingType == typeof(Guid)
            || underlyingType == typeof(DateTime)
            || underlyingType == typeof(DateTimeOffset)
            || underlyingType == typeof(TimeSpan);
    }
}

/// <summary>
/// Describes a server-side entity projection.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <param name="Properties">The properties included in the projection.</param>
/// <param name="Selector">The projection selector.</param>
internal sealed record EfCoreProjectionPlan<TEntity>(
    IReadOnlyList<PropertyInfo> Properties,
    Expression<Func<TEntity, TEntity>> Selector)
    where TEntity : class;
