using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;
using RestLib.Filtering;
using RestLib.Pagination;
using RestLib.Search;
using RestLib.Sorting;

namespace RestLib.EntityFrameworkCore;

/// <summary>
/// Applies EF Core query criteria and executes one consistent cursor-pagination pipeline.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
internal sealed class EfCorePageQueryExecutor<TEntity>
    where TEntity : class
{
    private const int DefaultPlanCacheCapacity = 256;
    private const int KeysetCursorVersion = 1;
    private static readonly MethodInfo OrderByMethod = typeof(Queryable)
        .GetMethods()
        .Single(method =>
            method.Name == nameof(Queryable.OrderBy) &&
            method.IsGenericMethodDefinition &&
            method.GetGenericArguments().Length == 2 &&
            method.GetParameters().Length == 2);
    private static readonly MethodInfo OrderByDescendingMethod = typeof(Queryable)
        .GetMethods()
        .Single(method =>
            method.Name == nameof(Queryable.OrderByDescending) &&
            method.IsGenericMethodDefinition &&
            method.GetGenericArguments().Length == 2 &&
            method.GetParameters().Length == 2);
    private static readonly MethodInfo ThenByMethod = typeof(Queryable)
        .GetMethods()
        .Single(method =>
            method.Name == nameof(Queryable.ThenBy) &&
            method.IsGenericMethodDefinition &&
            method.GetGenericArguments().Length == 2 &&
            method.GetParameters().Length == 2);
    private static readonly MethodInfo ThenByDescendingMethod = typeof(Queryable)
        .GetMethods()
        .Single(method =>
            method.Name == nameof(Queryable.ThenByDescending) &&
            method.IsGenericMethodDefinition &&
            method.GetGenericArguments().Length == 2 &&
            method.GetParameters().Length == 2);
    private static readonly MethodInfo StringCompareMethod = typeof(string)
        .GetMethod(nameof(string.Compare), [typeof(string), typeof(string)])
        ?? throw new InvalidOperationException("RestLib could not resolve string.Compare(string, string).");
    private readonly IEntityType _entityType;
    private readonly IReadOnlyList<SortBuilder.SortKeyPart> _keySortParts;
    private readonly Func<ILogger?> _loggerAccessor;
    private readonly PlanningCache _planningCache;

    /// <summary>
    /// Initializes a new instance of the <see cref="EfCorePageQueryExecutor{TEntity}"/> class.
    /// </summary>
    /// <param name="model">The EF Core model containing the entity metadata.</param>
    /// <param name="keySortParts">The stable key parts used as pagination tie-breakers.</param>
    /// <param name="loggerAccessor">Resolves the optional logger for offset-fallback diagnostics.</param>
    /// <param name="planningCache">The bounded cache shared by equivalent repository scopes.</param>
    internal EfCorePageQueryExecutor(
        IModel model,
        IReadOnlyList<SortBuilder.SortKeyPart> keySortParts,
        Func<ILogger?> loggerAccessor,
        PlanningCache? planningCache = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(keySortParts);

        _entityType = model.FindEntityType(typeof(TEntity))
            ?? throw new InvalidOperationException(
                $"Entity type '{typeof(TEntity).Name}' is not part of the EF Core model.");
        _keySortParts = keySortParts;
        _loggerAccessor = loggerAccessor ?? throw new ArgumentNullException(nameof(loggerAccessor));
        _planningCache = planningCache ?? new PlanningCache();
    }

    /// <summary>
    /// Applies the request criteria and materializes one page from a prepared base query.
    /// </summary>
    /// <param name="query">The prepared base query.</param>
    /// <param name="pagination">The query and pagination request.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The materialized page and its optional next cursor.</returns>
    internal async Task<PagedResult<TEntity>> ExecuteAsync(
        IQueryable<TEntity> query,
        PaginationRequest pagination,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(pagination);

        query = ApplyCriteria(query, pagination);

        var effectiveSortFields = GetEffectiveSortFields(pagination.SortFields);
        IOrderedQueryable<TEntity> orderedQuery;
        KeysetPlan? keysetPlan;
        var offsetStartIndex = 0;
        if (TryBuildKeysetPlan(effectiveSortFields, out var builtKeysetPlan))
        {
            var plan = builtKeysetPlan!;
            keysetPlan = plan;
            orderedQuery = ApplyKeysetCursorFilter(query, plan, pagination.Cursor);
        }
        else
        {
            keysetPlan = null;
            LogKeysetFallback(effectiveSortFields);
            offsetStartIndex = DecodeOffsetCursor(pagination.Cursor);
            orderedQuery = SortBuilder.ApplySorting(query, pagination.SortFields, _keySortParts);
        }

        var takeCount = pagination.Limit == int.MaxValue ? int.MaxValue : pagination.Limit + 1;
        List<TEntity> pagedItems;

        if (keysetPlan is not null)
        {
            pagedItems = await orderedQuery
                .Take(takeCount)
                .ToListAsync(ct);
        }
        else
        {
            pagedItems = await orderedQuery
                .Skip(offsetStartIndex)
                .Take(takeCount)
                .ToListAsync(ct);
        }

        var hasMore = pagedItems.Count > pagination.Limit;
        if (hasMore)
        {
            pagedItems = pagedItems.Take(pagination.Limit).ToList();
        }

        string? nextCursor;
        if (!hasMore)
        {
            nextCursor = null;
        }
        else if (keysetPlan is not null)
        {
            nextCursor = EncodeKeysetCursor(keysetPlan, pagedItems[^1]);
        }
        else
        {
            nextCursor = offsetStartIndex <= int.MaxValue - pagination.Limit
                ? CursorEncoder.Encode(offsetStartIndex + pagination.Limit)
                : null;
        }

        return new PagedResult<TEntity>
        {
            Items = pagedItems,
            NextCursor = nextCursor
        };
    }

    /// <summary>
    /// Applies search and filter criteria without sorting or materialization.
    /// </summary>
    /// <param name="query">The prepared base query.</param>
    /// <param name="request">The query request.</param>
    /// <returns>The composed query.</returns>
    internal IQueryable<TEntity> ApplyCriteria(
        IQueryable<TEntity> query,
        PaginationRequest request)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(request);

        if (request.Search is not null)
        {
            query = ApplySearch(query, request.Search);
        }

        return ApplyFilters(query, request.Filters);
    }

    /// <summary>
    /// Applies filter criteria without search, sorting, or materialization.
    /// </summary>
    /// <param name="query">The prepared base query.</param>
    /// <param name="filters">The filters to apply.</param>
    /// <returns>The composed query.</returns>
    internal IQueryable<TEntity> ApplyFilters(
        IQueryable<TEntity> query,
        IReadOnlyList<FilterValue> filters)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(filters);

        if (filters.Count == 0)
        {
            return query;
        }

        query = ApplyComparisonFilters(query, filters);
        query = ApplyStringFilters(query, filters);
        return ApplyInFilters(query, filters);
    }

    private static List<KeysetSortPart> GetEffectiveSortFields(IReadOnlyList<SortField> sortFields)
    {
        return sortFields
            .Select(sortField => new KeysetSortPart(
                sortField.PropertyName,
                sortField.Direction,
                sortField.QueryParameterName))
            .ToList();
    }

    private static int DecodeOffsetCursor(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor))
        {
            return 0;
        }

        if (CursorEncoder.TryDecode<int>(cursor, out var cursorIndex) && cursorIndex >= 0)
        {
            return cursorIndex;
        }

        throw new EfCoreInvalidCursorException(
            "The provided cursor is not a valid non-negative offset cursor for this result set.");
    }

    private static Expression<Func<TEntity, bool>> BuildKeysetPredicate(
        KeysetPlan keysetPlan,
        DecodedKeysetCursor cursor)
    {
        var parameter = Expression.Parameter(typeof(TEntity), "entity");
        Expression? predicate = null;

        for (var i = 0; i < keysetPlan.Parts.Count; i++)
        {
            Expression? andChain = null;

            for (var j = 0; j < i; j++)
            {
                var equalsExpression = BuildComparisonExpression(
                    parameter,
                    keysetPlan.Parts[j],
                    cursor.Values[j],
                    ExpressionType.Equal);
                andChain = andChain is null ? equalsExpression : Expression.AndAlso(andChain, equalsExpression);
            }

            var comparisonType = keysetPlan.Parts[i].Direction == SortDirection.Asc
                ? ExpressionType.GreaterThan
                : ExpressionType.LessThan;
            var comparisonExpression = BuildComparisonExpression(
                parameter,
                keysetPlan.Parts[i],
                cursor.Values[i],
                comparisonType);
            var branch = andChain is null ? comparisonExpression : Expression.AndAlso(andChain, comparisonExpression);
            predicate = predicate is null ? branch : Expression.OrElse(predicate, branch);
        }

        return Expression.Lambda<Func<TEntity, bool>>(predicate!, parameter);
    }

    private static Expression BuildComparisonExpression(
        ParameterExpression parameter,
        KeysetPlanPart part,
        object cursorValue,
        ExpressionType comparisonType)
    {
        var left = Expression.Property(parameter, part.Property);
        var right = Expression.Constant(cursorValue, part.PropertyType);

        if (part.PropertyType == typeof(string)
            && comparisonType is ExpressionType.GreaterThan or ExpressionType.LessThan)
        {
            var stringCompare = Expression.Call(StringCompareMethod, left, right);
            var zero = Expression.Constant(0);
            return comparisonType == ExpressionType.GreaterThan
                ? Expression.GreaterThan(stringCompare, zero)
                : Expression.LessThan(stringCompare, zero);
        }

        return Expression.MakeBinary(comparisonType, left, right);
    }

    private static IQueryable<TEntity> ApplyComparisonFilters(
        IQueryable<TEntity> query,
        IReadOnlyList<FilterValue> filters)
    {
        foreach (var filter in filters)
        {
            if (!IsComparisonOperator(filter.Operator))
            {
                continue;
            }

            var predicate = ComparisonFilterBuilder.BuildPredicate<TEntity>(filter);
            query = query.Where(predicate);
        }

        return query;
    }

    private static bool IsComparisonOperator(FilterOperator op)
    {
        return op is FilterOperator.Eq
            or FilterOperator.Neq
            or FilterOperator.Gt
            or FilterOperator.Lt
            or FilterOperator.Gte
            or FilterOperator.Lte;
    }

    private static IQueryable<TEntity> ApplyStringFilters(
        IQueryable<TEntity> query,
        IReadOnlyList<FilterValue> filters)
    {
        foreach (var filter in filters)
        {
            if (!IsStringOperator(filter.Operator))
            {
                continue;
            }

            var predicate = StringFilterBuilder.BuildPredicate<TEntity>(filter);
            query = query.Where(predicate);
        }

        return query;
    }

    private static bool IsStringOperator(FilterOperator op)
    {
        return op is FilterOperator.Contains
            or FilterOperator.StartsWith
            or FilterOperator.EndsWith;
    }

    private static IQueryable<TEntity> ApplyInFilters(
        IQueryable<TEntity> query,
        IReadOnlyList<FilterValue> filters)
    {
        foreach (var filter in filters)
        {
            if (!IsInOperator(filter.Operator))
            {
                continue;
            }

            var predicate = InFilterBuilder.BuildPredicate<TEntity>(filter);
            query = query.Where(predicate);
        }

        return query;
    }

    private static IQueryable<TEntity> ApplySearch(
        IQueryable<TEntity> query,
        SearchRequest search)
    {
        var predicate = SearchBuilder.BuildPredicate<TEntity>(search);
        return query.Where(predicate);
    }

    private static bool IsInOperator(FilterOperator op)
    {
        return op is FilterOperator.In;
    }

    private static MethodInfo GetQueryableSortMethod(SortDirection direction, bool isPrimarySort)
    {
        return (direction, isPrimarySort) switch
        {
            (SortDirection.Asc, true) => OrderByMethod,
            (SortDirection.Desc, true) => OrderByDescendingMethod,
            (SortDirection.Asc, false) => ThenByMethod,
            _ => ThenByDescendingMethod,
        };
    }

    private static IOrderedQueryable<TEntity> ApplyQueryableOrdering(
        MethodInfo method,
        IQueryable<TEntity> source,
        LambdaExpression keySelector)
    {
        var genericMethod = method.MakeGenericMethod(typeof(TEntity), keySelector.ReturnType);
        return (IOrderedQueryable<TEntity>)genericMethod.Invoke(null, [source, keySelector])!;
    }

    private bool TryBuildKeysetPlan(
        IReadOnlyList<KeysetSortPart> sortFields,
        out KeysetPlan? keysetPlan)
    {
        var shape = BuildKeysetPlanShape(sortFields);
        var resolution = _planningCache.GetOrCreate(
            shape,
            () => BuildKeysetPlan(sortFields));
        keysetPlan = resolution.Plan;
        return resolution.IsSupported;
    }

    private KeysetPlanResolution BuildKeysetPlan(
        IReadOnlyList<KeysetSortPart> sortFields)
    {
        var parts = new List<KeysetPlanPart>();

        foreach (var sortField in sortFields)
        {
            if (!TryBuildKeysetPlanPart(
                    sortField.PropertyName,
                    sortField.Direction,
                    sortField.QueryParameterName,
                    out var part))
            {
                return KeysetPlanResolution.Unsupported;
            }

            parts.Add(part!);
        }

        foreach (var keyPart in _keySortParts)
        {
            if (parts.Any(part => string.Equals(part.PropertyName, keyPart.PropertyName, StringComparison.Ordinal)))
            {
                continue;
            }

            if (!TryBuildKeysetPlanPart(
                    keyPart.PropertyName,
                    SortDirection.Asc,
                    JsonNamingPolicy.SnakeCaseLower.ConvertName(keyPart.PropertyName),
                    out var keyPlanPart))
            {
                return KeysetPlanResolution.Unsupported;
            }

            parts.Add(keyPlanPart!);
        }

        return new KeysetPlanResolution(new KeysetPlan(parts));
    }

    private bool TryBuildKeysetPlanPart(
        string propertyName,
        SortDirection direction,
        string queryParameterName,
        out KeysetPlanPart? part)
    {
        var property = typeof(TEntity).GetProperty(
            propertyName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        var mappedProperty = property is null ? null : _entityType.FindProperty(property.Name);
        if (property is null
            || mappedProperty is null
            || mappedProperty.IsNullable
            || !IsKeysetComparableType(property.PropertyType))
        {
            part = null;
            return false;
        }

        var parameter = Expression.Parameter(typeof(TEntity), "entity");
        var memberAccess = Expression.Property(parameter, property);
        var delegateType = typeof(Func<,>).MakeGenericType(typeof(TEntity), property.PropertyType);
        var selector = Expression.Lambda(delegateType, memberAccess, parameter);

        part = new KeysetPlanPart(
            property.Name,
            queryParameterName,
            property.PropertyType,
            direction,
            selector,
            property);
        return true;
    }

    private IOrderedQueryable<TEntity> ApplyKeysetCursorFilter(
        IQueryable<TEntity> query,
        KeysetPlan keysetPlan,
        string? cursor)
    {
        var filteredQuery = query;
        if (!string.IsNullOrEmpty(cursor))
        {
            var decodedCursor = DecodeKeysetCursor(cursor, keysetPlan);
            var predicate = BuildKeysetPredicate(keysetPlan, decodedCursor);
            filteredQuery = filteredQuery.Where(predicate);
        }

        return ApplyKeysetOrdering(filteredQuery, keysetPlan);
    }

    private IOrderedQueryable<TEntity> ApplyKeysetOrdering(
        IQueryable<TEntity> query,
        KeysetPlan keysetPlan)
    {
        IOrderedQueryable<TEntity>? orderedQuery = null;

        foreach (var part in keysetPlan.Parts)
        {
            var method = GetQueryableSortMethod(part.Direction, orderedQuery is null);
            orderedQuery = ApplyQueryableOrdering(method, orderedQuery ?? query, part.Selector);
        }

        return orderedQuery!;
    }

    private string EncodeKeysetCursor(KeysetPlan keysetPlan, TEntity entity)
    {
        var values = keysetPlan.Parts
            .Select(part => JsonSerializer.SerializeToElement(part.Property.GetValue(entity), part.PropertyType))
            .ToList();

        return CursorEncoder.Encode(new EfCoreKeysetCursor
        {
            Version = KeysetCursorVersion,
            SortSignature = BuildSortSignature(keysetPlan),
            Values = values
        });
    }

    private DecodedKeysetCursor DecodeKeysetCursor(string cursor, KeysetPlan keysetPlan)
    {
        if (CursorEncoder.TryDecode<EfCoreKeysetCursor>(cursor, out var decodedCursor))
        {
            var keysetCursor = decodedCursor
                ?? throw new EfCoreInvalidCursorException("The provided cursor could not be decoded.");
            if (keysetCursor.Version != KeysetCursorVersion)
            {
                throw new EfCoreInvalidCursorException("The provided cursor version is not supported.");
            }

            if (!string.Equals(
                    keysetCursor.SortSignature,
                    BuildSortSignature(keysetPlan),
                    StringComparison.Ordinal))
            {
                throw new EfCoreInvalidCursorException(
                    "The provided cursor does not match the active sort order.");
            }

            if (keysetCursor.Values is null || keysetCursor.Values.Count != keysetPlan.Parts.Count)
            {
                throw new EfCoreInvalidCursorException(
                    "The provided cursor does not match the active sort shape.");
            }

            var values = new List<object>(keysetPlan.Parts.Count);
            for (var i = 0; i < keysetPlan.Parts.Count; i++)
            {
                values.Add(DeserializeKeysetCursorValue(keysetCursor.Values[i], keysetPlan.Parts[i]));
            }

            return new DecodedKeysetCursor(values);
        }

        if (CursorEncoder.TryDecode<int>(cursor, out _))
        {
            throw new EfCoreInvalidCursorException(
                "Offset cursors are no longer valid for this sorted result set.");
        }

        throw new EfCoreInvalidCursorException(
            "The provided cursor is not a valid EF Core pagination cursor.");
    }

    private object DeserializeKeysetCursorValue(JsonElement cursorValue, KeysetPlanPart part)
    {
        if (cursorValue.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            throw new EfCoreInvalidCursorException(
                $"The provided cursor contains an invalid value for '{part.QueryParameterName}'.");
        }

        try
        {
            return JsonSerializer.Deserialize(cursorValue.GetRawText(), part.PropertyType)
                ?? throw new EfCoreInvalidCursorException(
                    $"The provided cursor contains an invalid value for '{part.QueryParameterName}'.");
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new EfCoreInvalidCursorException(
                $"The provided cursor contains an invalid value for '{part.QueryParameterName}'.");
        }
    }

    private bool IsKeysetComparableType(Type propertyType)
    {
        return Nullable.GetUnderlyingType(propertyType) is null
            && (propertyType == typeof(string)
                || propertyType == typeof(Guid)
                || propertyType == typeof(DateTime)
                || propertyType == typeof(DateTimeOffset)
                || propertyType == typeof(decimal)
                || propertyType == typeof(double)
                || propertyType == typeof(float)
                || propertyType == typeof(long)
                || propertyType == typeof(int)
                || propertyType == typeof(short)
                || propertyType == typeof(byte)
                || propertyType == typeof(ulong)
                || propertyType == typeof(uint)
                || propertyType == typeof(ushort)
                || propertyType == typeof(sbyte));
    }

    private string BuildSortSignature(KeysetPlan keysetPlan)
    {
        return string.Join(",", keysetPlan.Parts.Select(part => $"{part.QueryParameterName}:{part.Direction}"));
    }

    private string BuildKeysetPlanShape(IReadOnlyList<KeysetSortPart> sortFields)
    {
        var builder = new StringBuilder();
        foreach (var sortField in sortFields)
        {
            AppendShapeValue(builder, sortField.PropertyName);
            builder.Append((int)sortField.Direction).Append(':');
            AppendShapeValue(builder, sortField.QueryParameterName);
        }

        return builder.ToString();
    }

    private void AppendShapeValue(StringBuilder builder, string value)
    {
        builder.Append(value.Length).Append(':').Append(value).Append(';');
    }

    private void LogKeysetFallback(IReadOnlyList<KeysetSortPart> effectiveSortFields)
    {
        var logger = _loggerAccessor();
        if (logger is null)
        {
            return;
        }

        var sortDescription = effectiveSortFields.Count == 0
            ? "key only"
            : string.Join(", ", effectiveSortFields.Select(field => $"{field.QueryParameterName}:{field.Direction}"));
        logger.LogWarning(
            "EF Core keyset pagination fallback activated for {EntityType} with sort {SortDescription}; using offset cursor pagination instead.",
            typeof(TEntity).Name,
            sortDescription);
    }

    /// <summary>
    /// Retains a bounded set of immutable keyset plans shared by equivalent
    /// repository scopes.
    /// </summary>
    internal sealed class PlanningCache
    {
        private readonly BoundedPlanCache<string, object> _plans;

        /// <summary>
        /// Initializes a new instance of the <see cref="PlanningCache"/> class.
        /// </summary>
        /// <param name="capacity">The maximum number of query shapes to retain.</param>
        internal PlanningCache(int capacity = DefaultPlanCacheCapacity)
        {
            _plans = new BoundedPlanCache<string, object>(capacity);
        }

        /// <summary>
        /// Gets the number of retained keyset planning results.
        /// </summary>
        internal int Count => _plans.Count;

        /// <summary>
        /// Gets or creates the planning result for a normalized query shape.
        /// </summary>
        /// <typeparam name="TValue">The immutable planning result type.</typeparam>
        /// <param name="shape">The normalized query shape.</param>
        /// <param name="valueFactory">Creates the planning result on a cache miss.</param>
        /// <returns>The cached or newly-created planning result.</returns>
        internal TValue GetOrCreate<TValue>(
            string shape,
            Func<TValue> valueFactory)
            where TValue : notnull
        {
            return (TValue)_plans.GetOrCreate(shape, () => valueFactory());
        }
    }

    private sealed record KeysetPlanResolution(KeysetPlan? Plan)
    {
        internal static KeysetPlanResolution Unsupported { get; } = new((KeysetPlan?)null);

        internal bool IsSupported => Plan is not null;
    }

    private sealed record KeysetSortPart(
        string PropertyName,
        SortDirection Direction,
        string QueryParameterName);

    private sealed record KeysetPlan(IReadOnlyList<KeysetPlanPart> Parts);

    private sealed record DecodedKeysetCursor(IReadOnlyList<object> Values);

    private sealed record KeysetPlanPart(
        string PropertyName,
        string QueryParameterName,
        Type PropertyType,
        SortDirection Direction,
        LambdaExpression Selector,
        PropertyInfo Property);
}
