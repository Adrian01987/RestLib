using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using RestLib.Abstractions;
using RestLib.Filtering;
using RestLib.Internal;
using RestLib.Pagination;
using RestLib.Search;
using RestLib.Serialization;
using RestLib.Sorting;

namespace RestLib.InMemory;

/// <summary>
/// Thread-safe in-memory implementation of <see cref="IRepository{TEntity, TKey}"/>,
/// <see cref="IBatchRepository{TEntity, TKey}"/>,
/// <see cref="IConditionalWriteRepository{TEntity, TKey}"/>,
/// <see cref="ICountableRepository{TEntity, TKey}"/>, and
/// <see cref="IQueryCountableRepository{TEntity, TKey}"/>.
/// Ideal for testing, prototyping, and scenarios where data persistence is not required.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TKey">The key type.</typeparam>
public class InMemoryRepository<TEntity, TKey> :
    IRepository<TEntity, TKey>,
    IBatchRepository<TEntity, TKey>,
    IConditionalWriteRepository<TEntity, TKey>,
    ICountableRepository<TEntity, TKey>,
    IQueryCountableRepository<TEntity, TKey>
    where TEntity : class
    where TKey : notnull
{
    private static readonly ConcurrentDictionary<string, PropertyInfo[]> _propertyPathCache = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<TKey, TEntity> _store = new();
    private readonly object _mutationLock = new();
    private readonly Func<TEntity, TKey> _keySelector;
    private readonly Func<TKey> _keyGenerator;
    private readonly Func<TEntity, TKey, TEntity>? _keyAssigner;
    private readonly PropertyInfo? _keyProperty;
    private readonly IComparer<TKey> _keyComparer;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Initializes a new instance of <see cref="InMemoryRepository{TEntity, TKey}"/>.
    /// </summary>
    /// <param name="keySelector">Function to extract the key from an entity.</param>
    /// <param name="keyGenerator">Function to generate a new key for entity creation.</param>
    /// <param name="jsonOptions">Optional JSON serializer options for patch operations.</param>
    public InMemoryRepository(
        Func<TEntity, TKey> keySelector,
        Func<TKey> keyGenerator,
        JsonSerializerOptions? jsonOptions = null)
        : this(keySelector, keyGenerator, jsonOptions, null, null, hasExplicitKeyAssigner: false)
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="InMemoryRepository{TEntity, TKey}"/>
    /// with an explicit key comparer for deterministic collection ordering.
    /// </summary>
    /// <param name="keySelector">Function to extract the key from an entity.</param>
    /// <param name="keyGenerator">Function to generate a new key for entity creation.</param>
    /// <param name="jsonOptions">Optional JSON serializer options for patch operations.</param>
    /// <param name="keyComparer">Comparer used for default ordering and sort tie-breaking.</param>
    public InMemoryRepository(
        Func<TEntity, TKey> keySelector,
        Func<TKey> keyGenerator,
        JsonSerializerOptions? jsonOptions,
        IComparer<TKey> keyComparer)
        : this(
            keySelector,
            keyGenerator,
            jsonOptions,
            null,
            keyComparer ?? throw new ArgumentNullException(nameof(keyComparer)),
            hasExplicitKeyAssigner: false)
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="InMemoryRepository{TEntity, TKey}"/>
    /// with an explicit generated-key assigner.
    /// </summary>
    /// <param name="keySelector">Function to extract the key from an entity.</param>
    /// <param name="keyGenerator">Function to generate a new key for entity creation.</param>
    /// <param name="jsonOptions">Optional JSON serializer options for patch operations.</param>
    /// <param name="keyAssigner">
    /// Function that assigns a generated key and returns the entity instance to store.
    /// The function may return a replacement instance for immutable entity types.
    /// </param>
    public InMemoryRepository(
        Func<TEntity, TKey> keySelector,
        Func<TKey> keyGenerator,
        JsonSerializerOptions? jsonOptions,
        Func<TEntity, TKey, TEntity> keyAssigner)
        : this(
            keySelector,
            keyGenerator,
            jsonOptions,
            keyAssigner ?? throw new ArgumentNullException(nameof(keyAssigner)),
            null,
            hasExplicitKeyAssigner: true)
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="InMemoryRepository{TEntity, TKey}"/>
    /// with explicit generated-key assignment and key comparison.
    /// </summary>
    /// <param name="keySelector">Function to extract the key from an entity.</param>
    /// <param name="keyGenerator">Function to generate a new key for entity creation.</param>
    /// <param name="jsonOptions">Optional JSON serializer options for patch operations.</param>
    /// <param name="keyAssigner">
    /// Function that assigns a generated key and returns the entity instance to store.
    /// The function may return a replacement instance for immutable entity types.
    /// </param>
    /// <param name="keyComparer">Comparer used for default ordering and sort tie-breaking.</param>
    public InMemoryRepository(
        Func<TEntity, TKey> keySelector,
        Func<TKey> keyGenerator,
        JsonSerializerOptions? jsonOptions,
        Func<TEntity, TKey, TEntity> keyAssigner,
        IComparer<TKey> keyComparer)
        : this(
            keySelector,
            keyGenerator,
            jsonOptions,
            keyAssigner ?? throw new ArgumentNullException(nameof(keyAssigner)),
            keyComparer ?? throw new ArgumentNullException(nameof(keyComparer)),
            hasExplicitKeyAssigner: true)
    {
    }

    private InMemoryRepository(
        Func<TEntity, TKey> keySelector,
        Func<TKey> keyGenerator,
        JsonSerializerOptions? jsonOptions,
        Func<TEntity, TKey, TEntity>? keyAssigner,
        IComparer<TKey>? keyComparer,
        bool hasExplicitKeyAssigner)
    {
        _keySelector = keySelector ?? throw new ArgumentNullException(nameof(keySelector));
        _keyGenerator = keyGenerator ?? throw new ArgumentNullException(nameof(keyGenerator));
        _keyAssigner = hasExplicitKeyAssigner ? keyAssigner : null;
        _keyProperty = ResolveConventionalKeyProperty();
        _keyComparer = keyComparer ?? Comparer<TKey>.Default;
        _jsonOptions = jsonOptions ?? RestLibJsonOptions.CreateDefault();
    }

    /// <summary>
    /// Gets the current count of entities in the repository.
    /// </summary>
    public int Count => _store.Count;

    /// <inheritdoc />
    public Task<TEntity?> GetByIdAsync(TKey id, CancellationToken cancellationToken = default)
    {
        _store.TryGetValue(id, out var entity);
        return Task.FromResult(entity);
    }

    /// <inheritdoc />
    public Task<PagedResult<TEntity>> GetAllAsync(PaginationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var startIndex = DecodeOffsetCursor(request.Cursor);
        var items = _store.Values.AsEnumerable();

        // Apply filters
        items = ApplyFilters(items, request.Filters);

        // Apply search
        items = ApplySearch(items, request.Search);

        // Apply sorting (dynamic if sort fields provided, otherwise by key)
        var orderedItems = ApplySorting(items, request.SortFields).ToList();

        // Apply cursor-based pagination
        // Guard against int overflow when taking one extra to detect more items.
        var takeCount = request.Limit == int.MaxValue ? int.MaxValue : request.Limit + 1;

        var pagedItems = orderedItems
            .Skip(startIndex)
            .Take(takeCount)
            .ToList();

        var hasMore = pagedItems.Count > request.Limit;
        if (hasMore)
        {
            pagedItems = pagedItems.Take(request.Limit).ToList();
        }

        // Guard against int overflow when computing the next cursor position.
        string? nextCursor = hasMore && startIndex <= int.MaxValue - request.Limit
            ? CursorEncoder.Encode(startIndex + request.Limit)
            : null;

        return Task.FromResult(new PagedResult<TEntity>
        {
            Items = pagedItems,
            NextCursor = nextCursor
        });
    }

    /// <inheritdoc />
    public Task<TEntity> CreateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        lock (_mutationLock)
        {
            var key = _keySelector(entity);

            // If key is default, generate a new one and set it on the entity
            if (EqualityComparer<TKey>.Default.Equals(key, default!))
            {
                EnsureGeneratedKeyCanBeAssigned();
                key = _keyGenerator();
                entity = NormalizeEntityKey(entity, key);
            }

            if (!_store.TryAdd(key, entity))
            {
                throw new InvalidOperationException($"An entity with key '{key}' already exists.");
            }

            return Task.FromResult(entity);
        }
    }

    /// <inheritdoc />
    public Task<TEntity?> UpdateAsync(TKey id, TEntity entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        lock (_mutationLock)
        {
            if (!_store.ContainsKey(id))
            {
                return Task.FromResult<TEntity?>(null);
            }

            var normalizedEntity = NormalizeEntityKey(entity, id);
            _store[id] = normalizedEntity;
            return Task.FromResult<TEntity?>(normalizedEntity);
        }
    }

    /// <inheritdoc />
    public Task<TEntity?> PatchAsync(TKey id, JsonElement patchDocument, CancellationToken cancellationToken = default)
    {
        ThrowIfPatchModifiesKey(patchDocument);

        lock (_mutationLock)
        {
            if (!_store.TryGetValue(id, out var existing))
            {
                return Task.FromResult<TEntity?>(null);
            }

            var updated = JsonMergePatch.Apply(existing, patchDocument, _jsonOptions);
            if (updated == null)
            {
                throw new InvalidOperationException("Failed to deserialize patched entity.");
            }

            _store[id] = updated;
            return Task.FromResult<TEntity?>(updated);
        }
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(TKey id, CancellationToken cancellationToken = default)
    {
        lock (_mutationLock)
        {
            return Task.FromResult(_store.TryRemove(id, out _));
        }
    }

    /// <inheritdoc />
    public Task<ConditionalWriteResult<TEntity>> UpdateConditionallyAsync(
        TKey id,
        TEntity entity,
        Func<TEntity, bool> precondition,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(precondition);

        lock (_mutationLock)
        {
            if (!_store.TryGetValue(id, out var current))
            {
                return Task.FromResult(ConditionalWriteResult<TEntity>.NotFound());
            }

            if (!precondition(current))
            {
                return Task.FromResult(ConditionalWriteResult<TEntity>.PreconditionFailed());
            }

            var normalizedEntity = NormalizeEntityKey(entity, id);
            _store[id] = normalizedEntity;
            return Task.FromResult(ConditionalWriteResult<TEntity>.Success(normalizedEntity));
        }
    }

    /// <inheritdoc />
    public Task<ConditionalWriteResult<TEntity>> PatchConditionallyAsync(
        TKey id,
        JsonElement patchDocument,
        Func<TEntity, bool> precondition,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(precondition);
        ThrowIfPatchModifiesKey(patchDocument);

        lock (_mutationLock)
        {
            if (!_store.TryGetValue(id, out var current))
            {
                return Task.FromResult(ConditionalWriteResult<TEntity>.NotFound());
            }

            if (!precondition(current))
            {
                return Task.FromResult(ConditionalWriteResult<TEntity>.PreconditionFailed());
            }

            var updated = JsonMergePatch.Apply(current, patchDocument, _jsonOptions)
                ?? throw new InvalidOperationException("Failed to deserialize patched entity.");
            _store[id] = updated;
            return Task.FromResult(ConditionalWriteResult<TEntity>.Success(updated));
        }
    }

    /// <inheritdoc />
    public Task<ConditionalWriteResult<TEntity>> DeleteConditionallyAsync(
        TKey id,
        Func<TEntity, bool> precondition,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(precondition);

        lock (_mutationLock)
        {
            if (!_store.TryGetValue(id, out var current))
            {
                return Task.FromResult(ConditionalWriteResult<TEntity>.NotFound());
            }

            if (!precondition(current))
            {
                return Task.FromResult(ConditionalWriteResult<TEntity>.PreconditionFailed());
            }

            _store.TryRemove(id, out _);
            return Task.FromResult(ConditionalWriteResult<TEntity>.Success(current));
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TEntity>> CreateManyAsync(
        IReadOnlyList<TEntity> entities,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entities);

        lock (_mutationLock)
        {
            var staged = new List<(TKey Key, TEntity Entity)>(entities.Count);
            var stagedKeys = new HashSet<TKey>();

            foreach (var entity in entities)
            {
                var key = _keySelector(entity);
                var current = entity;

                if (EqualityComparer<TKey>.Default.Equals(key, default!))
                {
                    EnsureGeneratedKeyCanBeAssigned();
                    key = _keyGenerator();
                    current = NormalizeEntityKey(current, key);
                }

                if (!stagedKeys.Add(key) || _store.ContainsKey(key))
                {
                    throw new InvalidOperationException($"An entity with key '{key}' already exists.");
                }

                staged.Add((key, current));
            }

            foreach (var (key, entity) in staged)
            {
                _store[key] = entity;
            }

            return Task.FromResult<IReadOnlyList<TEntity>>(
                staged.Select(item => item.Entity).ToList());
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TEntity>> UpdateManyAsync(
        IReadOnlyList<TEntity> entities,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entities);

        lock (_mutationLock)
        {
            var staged = entities
                .Select(entity => (Key: _keySelector(entity), Entity: entity))
                .ToList();
            var matchedKeys = staged
                .Where(item => _store.ContainsKey(item.Key))
                .Select(item => item.Key)
                .ToList();

            foreach (var (key, entity) in staged)
            {
                if (_store.ContainsKey(key))
                {
                    _store[key] = entity;
                }
            }

            return Task.FromResult<IReadOnlyList<TEntity>>(
                matchedKeys.Select(key => _store[key]).ToList());
        }
    }

    /// <inheritdoc />
    public Task<int> DeleteManyAsync(
        IReadOnlyList<TKey> keys,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(keys);

        lock (_mutationLock)
        {
            var keysToDelete = keys
                .Distinct()
                .Where(_store.ContainsKey)
                .ToList();

            foreach (var key in keysToDelete)
            {
                _store.TryRemove(key, out _);
            }

            return Task.FromResult(keysToDelete.Count);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<TKey, TEntity>> GetByIdsAsync(
        IReadOnlyList<TKey> ids,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var result = new Dictionary<TKey, TEntity>(ids.Count);
        foreach (var id in ids)
        {
            if (_store.TryGetValue(id, out var entity))
            {
                result[id] = entity;
            }
        }

        return Task.FromResult<IReadOnlyDictionary<TKey, TEntity>>(result);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TEntity>> PatchManyAsync(
        IReadOnlyList<(TKey Id, JsonElement PatchDocument)> patches,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(patches);

        lock (_mutationLock)
        {
            var stagedById = new Dictionary<TKey, TEntity>();
            var matchedIds = new List<TKey>(patches.Count);

            foreach (var (id, patchDocument) in patches)
            {
                ThrowIfPatchModifiesKey(patchDocument);

                if (!stagedById.TryGetValue(id, out var existing) &&
                    !_store.TryGetValue(id, out existing))
                {
                    continue;
                }

                var updated = JsonMergePatch.Apply(existing, patchDocument, _jsonOptions);
                if (updated == null)
                {
                    throw new InvalidOperationException($"Failed to deserialize patched entity with key '{id}'.");
                }

                stagedById[id] = updated;
                matchedIds.Add(id);
            }

            foreach (var (id, entity) in stagedById)
            {
                _store[id] = entity;
            }

            return Task.FromResult<IReadOnlyList<TEntity>>(
                matchedIds.Select(id => stagedById[id]).ToList());
        }
    }

    /// <inheritdoc />
    public Task<long> CountAsync(IReadOnlyList<FilterValue> filters, CancellationToken ct = default)
    {
        var items = _store.Values.AsEnumerable();
        items = ApplyFilters(items, filters);
        return Task.FromResult((long)items.Count());
    }

    /// <inheritdoc />
    public Task<long> CountAsync(PaginationRequest query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var items = _store.Values.AsEnumerable();
        items = ApplyFilters(items, query.Filters);
        items = ApplySearch(items, query.Search);
        return Task.FromResult((long)items.Count());
    }

    /// <summary>
    /// Clears all entities from the repository.
    /// </summary>
    public void Clear()
    {
        lock (_mutationLock)
        {
            _store.Clear();
        }
    }

    /// <summary>
    /// Seeds the repository with initial data.
    /// </summary>
    /// <param name="entities">The entities to seed.</param>
    public void Seed(IEnumerable<TEntity> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);

        lock (_mutationLock)
        {
            foreach (var entity in entities)
            {
                var key = _keySelector(entity);
                _store[key] = entity;
            }
        }
    }

    private static object? ConvertFilterValue(string? value, Type targetType)
    {
        if (value == null)
        {
            return null;
        }

        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        try
        {
            if (underlyingType == typeof(Guid))
            {
                return Guid.Parse(value);
            }
            if (underlyingType == typeof(DateTime))
            {
                return DateTime.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
            }
            if (underlyingType == typeof(DateTimeOffset))
            {
                return DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
            }
            if (underlyingType.IsEnum)
            {
                return Enum.Parse(underlyingType, value, ignoreCase: true);
            }
            return Convert.ChangeType(value, underlyingType, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch
        {
            return value;
        }
    }

    private static int CompareValues(object? entityValue, object? filterValue)
    {
        if (entityValue is null && filterValue is null) return 0;
        if (entityValue is null) return -1;
        if (filterValue is null) return 1;

        // Normalize mismatched numeric types to a common type so that
        // comparisons like Equals(long, int) don't fail silently.
        if (entityValue is IConvertible && filterValue is IConvertible)
        {
            var entityCode = Type.GetTypeCode(entityValue.GetType());
            var filterCode = Type.GetTypeCode(filterValue.GetType());

            if (IsNumericTypeCode(entityCode) && IsNumericTypeCode(filterCode) && entityCode != filterCode)
            {
                // If either side is a floating-point type, compare as double.
                if (IsFloatingPoint(entityCode) || IsFloatingPoint(filterCode))
                {
                    var d1 = Convert.ToDouble(entityValue, System.Globalization.CultureInfo.InvariantCulture);
                    var d2 = Convert.ToDouble(filterValue, System.Globalization.CultureInfo.InvariantCulture);
                    return d1.CompareTo(d2);
                }

                // Both are integer types — compare as long.
                var l1 = Convert.ToInt64(entityValue, System.Globalization.CultureInfo.InvariantCulture);
                var l2 = Convert.ToInt64(filterValue, System.Globalization.CultureInfo.InvariantCulture);
                return l1.CompareTo(l2);
            }
        }

        if (entityValue is IComparable comparable)
        {
            try
            {
                return comparable.CompareTo(filterValue);
            }
            catch (ArgumentException)
            {
                // CompareTo throws when types are incompatible (e.g., Guid vs string).
                // Fall through to the equality fallback.
            }
        }

        // Fallback: equality only
        return Equals(entityValue, filterValue) ? 0 : -1;
    }

    private static bool IsNumericTypeCode(TypeCode code) =>
        code is >= TypeCode.SByte and <= TypeCode.Decimal;

    private static bool IsFloatingPoint(TypeCode code) =>
        code is TypeCode.Single or TypeCode.Double or TypeCode.Decimal;

    private static bool ContainsString(object? entityValue, object? filterValue)
    {
        if (entityValue is not string entityStr || filterValue is not string filterStr)
        {
            return false;
        }

        return entityStr.Contains(filterStr, StringComparison.OrdinalIgnoreCase);
    }

    private static bool StartsWithString(object? entityValue, object? filterValue)
    {
        if (entityValue is not string entityStr || filterValue is not string filterStr)
        {
            return false;
        }

        return entityStr.StartsWith(filterStr, StringComparison.OrdinalIgnoreCase);
    }

    private static bool EndsWithString(object? entityValue, object? filterValue)
    {
        if (entityValue is not string entityStr || filterValue is not string filterStr)
        {
            return false;
        }

        return entityStr.EndsWith(filterStr, StringComparison.OrdinalIgnoreCase);
    }

    private static bool InValues(object? entityValue, IReadOnlyList<object?>? typedValues)
    {
        if (typedValues is null || typedValues.Count == 0)
        {
            return false;
        }

        return typedValues.Any(v => CompareValues(entityValue, v) == 0);
    }

    private static PropertyInfo[]? GetCachedPropertyPath(string propertyPath)
    {
        if (_propertyPathCache.TryGetValue(propertyPath, out var cachedPath))
        {
            return cachedPath;
        }

        try
        {
            var resolvedPath = NamingUtils.ResolvePropertyPath(typeof(TEntity), propertyPath, nameof(propertyPath));
            var properties = new PropertyInfo[resolvedPath.ClrSegments.Count];
            var currentType = typeof(TEntity);

            for (var i = 0; i < resolvedPath.ClrSegments.Count; i++)
            {
                var property = currentType.GetProperty(
                    resolvedPath.ClrSegments[i],
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (property is null)
                {
                    return null;
                }

                properties[i] = property;
                currentType = property.PropertyType;
            }

            return _propertyPathCache.GetOrAdd(propertyPath, properties);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static bool TryGetPropertyPathValue(TEntity entity, string propertyPath, out object? value)
    {
        var properties = GetCachedPropertyPath(propertyPath);
        if (properties is null)
        {
            value = null;
            return false;
        }

        object? current = entity;
        foreach (var property in properties)
        {
            if (current is null)
            {
                value = null;
                return true;
            }

            current = property.GetValue(current);
        }

        value = current;
        return true;
    }

    private IEnumerable<TEntity> ApplyFilters(IEnumerable<TEntity> items, IReadOnlyList<FilterValue> filters)
    {
        foreach (var filter in filters)
        {
            items = items.Where(e => MatchesFilter(e, filter));
        }
        return items;
    }

    private IEnumerable<TEntity> ApplySearch(IEnumerable<TEntity> items, SearchRequest? search)
    {
        if (search is null || search.Properties.Count == 0)
        {
            return items;
        }

        return items.Where(entity => MatchesSearch(entity, search));
    }

    private IEnumerable<TEntity> ApplySorting(
        IEnumerable<TEntity> items,
        IReadOnlyList<SortField> sortFields)
    {
        if (sortFields.Count == 0)
        {
            // No sort requested — fall back to key ordering (preserves current behavior)
            return items.OrderBy(e => _keySelector(e), _keyComparer);
        }

        IOrderedEnumerable<TEntity>? ordered = null;

        foreach (var field in sortFields)
        {
            Func<TEntity, object?> selector = e =>
            {
                _ = TryGetPropertyPathValue(e, field.PropertyName, out var value);
                return value;
            };

            if (ordered is null)
            {
                ordered = field.Direction == SortDirection.Asc
                    ? items.OrderBy(selector)
                    : items.OrderByDescending(selector);
            }
            else
            {
                ordered = field.Direction == SortDirection.Asc
                    ? ordered.ThenBy(selector)
                    : ordered.ThenByDescending(selector);
            }
        }

        // Always append key as tie-breaker for stable cursor pagination
        return ordered!.ThenBy(e => _keySelector(e), _keyComparer);
    }

    private int DecodeOffsetCursor(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor))
        {
            return 0;
        }

        if (!CursorEncoder.TryDecode<int>(cursor, out var offset))
        {
            throw new InvalidCursorException(
                "The provided cursor is not a valid offset cursor for this result set.");
        }

        if (offset < 0)
        {
            throw new InvalidCursorException(
                "The provided cursor must contain a non-negative offset.");
        }

        return offset;
    }

    private bool MatchesFilter(TEntity entity, FilterValue filter)
    {
        if (FilterOperatorCompatibility.IsRelational(filter.Operator)
            && !FilterOperatorCompatibility.IsSupported(filter.Operator, filter.PropertyType))
        {
            throw new NotSupportedException(
                $"Filter operator '{filter.Operator}' cannot be applied to property '{filter.PropertyName}' " +
                $"of type '{filter.PropertyType.Name}'.");
        }

        if (!TryGetPropertyPathValue(entity, filter.PropertyName, out var entityValue))
        {
            return true; // Skip unknown properties
        }

        var filterValue = filter.TypedValue ?? ConvertFilterValue(filter.RawValue, filter.PropertyType);

        return filter.Operator switch
        {
            FilterOperator.Eq => CompareValues(entityValue, filterValue) == 0,
            FilterOperator.Neq => CompareValues(entityValue, filterValue) != 0,
            FilterOperator.Gt => entityValue is not null && filterValue is not null && CompareValues(entityValue, filterValue) > 0,
            FilterOperator.Lt => entityValue is not null && filterValue is not null && CompareValues(entityValue, filterValue) < 0,
            FilterOperator.Gte => entityValue is not null && filterValue is not null && CompareValues(entityValue, filterValue) >= 0,
            FilterOperator.Lte => entityValue is not null && filterValue is not null && CompareValues(entityValue, filterValue) <= 0,
            FilterOperator.Contains => ContainsString(entityValue, filterValue),
            FilterOperator.StartsWith => StartsWithString(entityValue, filterValue),
            FilterOperator.EndsWith => EndsWithString(entityValue, filterValue),
            FilterOperator.In => InValues(entityValue, filter.TypedValues),
            _ => CompareValues(entityValue, filterValue) == 0,
        };
    }

    private bool MatchesSearch(TEntity entity, SearchRequest search)
    {
        var comparison = search.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        foreach (var property in search.Properties)
        {
            if (!TryGetPropertyPathValue(entity, property.PropertyName, out var entityValue))
            {
                continue;
            }

            if (entityValue is string stringValue && stringValue.Contains(search.Term, comparison))
            {
                return true;
            }
        }

        return false;
    }

    private TEntity SetKeyOnEntity(TEntity entity, TKey key)
    {
        if (_keyAssigner is not null)
        {
            return _keyAssigner(entity, key)
                ?? throw new InvalidOperationException(
                    $"The configured key assigner for '{typeof(TEntity).Name}' returned null.");
        }

        var keyProperty = _keyProperty
            ?? throw CreateMissingKeyAssignerException();
        keyProperty.SetValue(entity, key);
        return entity;
    }

    private TEntity NormalizeEntityKey(TEntity entity, TKey key)
    {
        var normalized = SetKeyOnEntity(entity, key);
        if (!EqualityComparer<TKey>.Default.Equals(_keySelector(normalized), key))
        {
            throw new InvalidOperationException(
                $"The configured resource key for '{typeof(TEntity).Name}' could not be set to '{key}'.");
        }

        return normalized;
    }

    private void ThrowIfPatchModifiesKey(JsonElement patchDocument)
    {
        if (patchDocument.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var keyPropertyName = _keyProperty?.Name;
        if (keyPropertyName is null)
        {
            return;
        }

        var contract = JsonObjectContract.Get(typeof(TEntity), _jsonOptions);

        foreach (var patchProperty in patchDocument.EnumerateObject())
        {
            if (contract.TryGetPatchMember(patchProperty.Name, out var member)
                && member.ClrName?.Equals(keyPropertyName, StringComparison.Ordinal) == true)
            {
                throw new PatchValidationException(
                    $"PATCH cannot modify immutable resource key field '{patchProperty.Name}'.");
            }
        }
    }

    private void EnsureGeneratedKeyCanBeAssigned()
    {
        if (_keyAssigner is null && _keyProperty is null)
        {
            throw CreateMissingKeyAssignerException();
        }
    }

    private InvalidOperationException CreateMissingKeyAssignerException()
    {
        return new InvalidOperationException(
            $"A generated key for '{typeof(TEntity).Name}' cannot be assigned unambiguously. " +
            "Configure an explicit key assigner for calculated, composite, or ambiguous keys.");
    }

    private PropertyInfo? ResolveConventionalKeyProperty()
    {
        var candidates = typeof(TEntity)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(TKey) && p.CanRead && p.CanWrite)
            .ToArray();

        if (candidates.Length == 1)
        {
            return candidates[0];
        }

        return candidates.FirstOrDefault(p =>
            p.Name.Equals("Id", StringComparison.OrdinalIgnoreCase) ||
            p.Name.Equals($"{typeof(TEntity).Name}Id", StringComparison.OrdinalIgnoreCase));
    }
}
