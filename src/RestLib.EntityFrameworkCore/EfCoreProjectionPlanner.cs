using System.Linq.Expressions;
using System.Reflection;
using System.Text;
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
    private const int DefaultPlanCacheCapacity = 256;
    private readonly Func<bool> _projectionPushdownEnabled;
    private readonly IReadOnlyList<string> _keyPropertyNames;
    private readonly PlanningCache _planningCache;

    /// <summary>
    /// Initializes a new instance of the <see cref="EfCoreProjectionPlanner{TEntity}"/> class.
    /// </summary>
    /// <param name="projectionPushdownEnabled">
    /// Resolves whether projection pushdown is enabled for the current repository options.
    /// </param>
    /// <param name="keyPropertyNames">The key properties required in every projected entity.</param>
    /// <param name="planningCache">The bounded cache shared by equivalent repository scopes.</param>
    internal EfCoreProjectionPlanner(
        Func<bool> projectionPushdownEnabled,
        IReadOnlyList<string> keyPropertyNames,
        PlanningCache? planningCache = null)
    {
        _projectionPushdownEnabled = projectionPushdownEnabled
            ?? throw new ArgumentNullException(nameof(projectionPushdownEnabled));
        _keyPropertyNames = keyPropertyNames
            ?? throw new ArgumentNullException(nameof(keyPropertyNames));
        _planningCache = planningCache ?? new PlanningCache();
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

        var shape = ProjectionPlanShape.Create(requiredProperties);
        var resolution = _planningCache.GetOrCreate(
            shape,
            () => BuildProjectionPlan(requiredProperties));
        projectionPlan = resolution.Plan;
        return resolution.IsSupported;
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

    private static ProjectionPlanResolution BuildProjectionPlan(
        IEnumerable<string> requiredProperties)
    {
        var requiredPropertyNames = requiredProperties
            .Order(StringComparer.Ordinal)
            .ToList();
        var properties = new List<PropertyInfo>(requiredPropertyNames.Count);
        foreach (var propertyName in requiredPropertyNames)
        {
            var property = typeof(TEntity).GetProperty(
                propertyName,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (property is null || !property.CanRead || !property.CanWrite || !IsProjectableProperty(property))
            {
                return ProjectionPlanResolution.Unsupported;
            }

            properties.Add(property);
        }

        var parameter = Expression.Parameter(typeof(TEntity), "entity");
        var bindings = properties
            .Select(property => Expression.Bind(property, Expression.Property(parameter, property)))
            .ToArray();
        var body = Expression.MemberInit(Expression.New(typeof(TEntity)), bindings);
        var selector = Expression.Lambda<Func<TEntity, TEntity>>(body, parameter);

        return new ProjectionPlanResolution(
            new EfCoreProjectionPlan<TEntity>(properties, selector));
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

    /// <summary>
    /// Identifies a normalized scalar-projection property set.
    /// </summary>
    /// <param name="Value">The length-prefixed property-set signature.</param>
    internal readonly record struct ProjectionPlanShape(string Value)
    {
        /// <summary>
        /// Creates a normalized shape from the required properties.
        /// </summary>
        /// <param name="propertyNames">The required CLR property names.</param>
        /// <returns>The normalized projection shape.</returns>
        internal static ProjectionPlanShape Create(IEnumerable<string> propertyNames)
        {
            var builder = new StringBuilder();
            foreach (var propertyName in propertyNames.Order(StringComparer.Ordinal))
            {
                builder.Append(propertyName.Length)
                    .Append(':')
                    .Append(propertyName)
                    .Append(';');
            }

            return new ProjectionPlanShape(builder.ToString());
        }
    }

    /// <summary>
    /// Retains a bounded set of immutable projection plans shared by equivalent
    /// repository scopes.
    /// </summary>
    internal sealed class PlanningCache
    {
        private readonly BoundedPlanCache<ProjectionPlanShape, ProjectionPlanResolution> _plans;

        /// <summary>
        /// Initializes a new instance of the <see cref="PlanningCache"/> class.
        /// </summary>
        /// <param name="capacity">The maximum number of projection shapes to retain.</param>
        internal PlanningCache(int capacity = DefaultPlanCacheCapacity)
        {
            _plans = new BoundedPlanCache<ProjectionPlanShape, ProjectionPlanResolution>(capacity);
        }

        /// <summary>
        /// Gets the number of retained projection planning results.
        /// </summary>
        internal int Count => _plans.Count;

        /// <summary>
        /// Gets or creates the planning result for a normalized projection shape.
        /// </summary>
        /// <param name="shape">The normalized projection shape.</param>
        /// <param name="valueFactory">Creates the planning result on a cache miss.</param>
        /// <returns>The cached or newly-created planning result.</returns>
        internal ProjectionPlanResolution GetOrCreate(
            ProjectionPlanShape shape,
            Func<ProjectionPlanResolution> valueFactory)
        {
            return _plans.GetOrCreate(shape, valueFactory);
        }
    }

    /// <summary>
    /// Represents a supported or unsupported projection planning result.
    /// </summary>
    /// <param name="Plan">The immutable plan, or <see langword="null"/> when unsupported.</param>
    internal sealed record ProjectionPlanResolution(EfCoreProjectionPlan<TEntity>? Plan)
    {
        /// <summary>
        /// Gets the shared unsupported result.
        /// </summary>
        internal static ProjectionPlanResolution Unsupported { get; } =
            new((EfCoreProjectionPlan<TEntity>?)null);

        /// <summary>
        /// Gets a value indicating whether projection pushdown is supported.
        /// </summary>
        internal bool IsSupported => Plan is not null;
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
