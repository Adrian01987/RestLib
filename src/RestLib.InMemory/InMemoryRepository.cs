using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using RestLib.Abstractions;
using RestLib.Filtering;
using RestLib.Pagination;
using RestLib.Sorting;

namespace RestLib.InMemory;

/// <summary>
/// Thread-safe in-memory implementation of <see cref="IRepository{TEntity, TKey}"/>,
/// <see cref="IBatchRepository{TEntity, TKey}"/>, and <see cref="ICountableRepository{TEntity, TKey}"/>.
/// Ideal for testing, prototyping, and scenarios where data persistence is not required.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TKey">The key type.</typeparam>
public class InMemoryRepository<TEntity, TKey> : IRepository<TEntity, TKey>, IBatchRepository<TEntity, TKey>, ICountableRepository<TEntity, TKey>
    where TEntity : class
    where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, TEntity> _store = new();
    private readonly Func<TEntity, TKey> _keySelector;
    private readonly Func<TKey> _keyGenerator;
    private readonly JsonSerializerOptions _jsonOptions;
    private string? _cachedKeyPropertyName;
    private bool _keyPropertyNameResolved;

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
    {
        _keySelector = keySelector ?? throw new ArgumentNullException(nameof(keySelector));
        _keyGenerator = keyGenerator ?? throw new ArgumentNullException(nameof(keyGenerator));
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
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
        var items = _store.Values.AsEnumerable();

        // Apply filters
        items = ApplyFilters(items, request.Filters);

        // Apply sorting (dynamic if sort fields provided, otherwise by key)
        var orderedItems = ApplySorting(items, request.SortFields).ToList();

        // Apply cursor-based pagination
        int startIndex = 0;
        if (!string.IsNullOrEmpty(request.Cursor) && CursorEncoder.TryDecode<int>(request.Cursor, out var cursorIndex))
        {
            startIndex = cursorIndex;
        }

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

        var key = _keySelector(entity);

        // If key is default, generate a new one and set it on the entity
        if (EqualityComparer<TKey>.Default.Equals(key, default!))
        {
            key = _keyGenerator();
            entity = SetKeyOnEntity(entity, key);
        }

        if (!_store.TryAdd(key, entity))
        {
            throw new InvalidOperationException($"An entity with key '{key}' already exists.");
        }

        return Task.FromResult(entity);
    }

    /// <inheritdoc />
    public Task<TEntity?> UpdateAsync(TKey id, TEntity entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        if (!_store.ContainsKey(id))
        {
            return Task.FromResult<TEntity?>(null);
        }

        _store[id] = entity;
        return Task.FromResult<TEntity?>(entity);
    }

    /// <inheritdoc />
    public Task<TEntity?> PatchAsync(TKey id, JsonElement patchDocument, CancellationToken cancellationToken = default)
    {
        if (!_store.TryGetValue(id, out var existing))
        {
            return Task.FromResult<TEntity?>(null);
        }

        // Serialize existing entity to JSON
        var existingJson = JsonSerializer.Serialize(existing, _jsonOptions);

        // Merge patch document with existing JSON
        var existingDoc = JsonDocument.Parse(existingJson);
        var merged = MergeJsonObjects(existingDoc.RootElement, patchDocument);

        // Deserialize merged result back to entity
        var updated = JsonSerializer.Deserialize<TEntity>(merged, _jsonOptions);
        if (updated == null)
        {
            throw new InvalidOperationException("Failed to deserialize patched entity.");
        }

        _store[id] = updated;
        return Task.FromResult<TEntity?>(updated);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(TKey id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_store.TryRemove(id, out _));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TEntity>> CreateManyAsync(
        IReadOnlyList<TEntity> entities,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var results = new List<TEntity>(entities.Count);
        foreach (var entity in entities)
        {
            var key = _keySelector(entity);
            var current = entity;

            if (EqualityComparer<TKey>.Default.Equals(key, default!))
            {
                key = _keyGenerator();
                current = SetKeyOnEntity(current, key);
            }

            if (!_store.TryAdd(key, current))
            {
                throw new InvalidOperationException($"An entity with key '{key}' already exists.");
            }

            results.Add(current);
        }

        return Task.FromResult<IReadOnlyList<TEntity>>(results);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TEntity>> UpdateManyAsync(
        IReadOnlyList<TEntity> entities,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var results = new List<TEntity>(entities.Count);
        foreach (var entity in entities)
        {
            var key = _keySelector(entity);
            _store[key] = entity;
            results.Add(entity);
        }

        return Task.FromResult<IReadOnlyList<TEntity>>(results);
    }

    /// <inheritdoc />
    public Task<int> DeleteManyAsync(
        IReadOnlyList<TKey> keys,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(keys);

        var count = 0;
        foreach (var key in keys)
        {
            if (_store.TryRemove(key, out _))
            {
                count++;
            }
        }

        return Task.FromResult(count);
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

        var results = new List<TEntity>(patches.Count);
        foreach (var (id, patchDocument) in patches)
        {
            if (!_store.TryGetValue(id, out var existing))
            {
                throw new KeyNotFoundException($"Entity with key '{id}' not found.");
            }

            var existingJson = JsonSerializer.Serialize(existing, _jsonOptions);
            var existingDoc = JsonDocument.Parse(existingJson);
            var merged = MergeJsonObjects(existingDoc.RootElement, patchDocument);

            var updated = JsonSerializer.Deserialize<TEntity>(merged, _jsonOptions);
            if (updated == null)
            {
                throw new InvalidOperationException($"Failed to deserialize patched entity with key '{id}'.");
            }

            _store[id] = updated;
            results.Add(updated);
        }

        return Task.FromResult<IReadOnlyList<TEntity>>(results);
    }

    /// <inheritdoc />
    public Task<long> CountAsync(IReadOnlyList<FilterValue> filters, CancellationToken ct = default)
    {
        var items = _store.Values.AsEnumerable();
        items = ApplyFilters(items, filters);
        return Task.FromResult((long)items.Count());
    }

    /// <summary>
    /// Clears all entities from the repository.
    /// </summary>
    public void Clear() => _store.Clear();

    /// <summary>
    /// Seeds the repository with initial data.
    /// </summary>
    /// <param name="entities">The entities to seed.</param>
    public void Seed(IEnumerable<TEntity> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);

        foreach (var entity in entities)
        {
            var key = _keySelector(entity);
            _store[key] = entity;
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

    /// <summary>
    /// Builds a mapping from C# PascalCase property names to their serialized JSON key names.
    /// </summary>
    /// <param name="serializedKeys">The existing serialized key names from the original entity.</param>
    /// <returns>A dictionary mapping each CLR property name (case-insensitive) to its serialized key.</returns>
    private static Dictionary<string, string> BuildPropertyNameMap(IEnumerable<string> serializedKeys)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var entityProperties = typeof(TEntity).GetProperties(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        foreach (var clrProp in entityProperties)
        {
            // Find which serialized key corresponds to this CLR property.
            // Match case-insensitively against the serialized keys.
            foreach (var serializedKey in serializedKeys)
            {
                if (serializedKey.Equals(clrProp.Name, StringComparison.OrdinalIgnoreCase))
                {
                    map[clrProp.Name] = serializedKey;
                    break;
                }
            }
        }

        return map;
    }

    /// <summary>
    /// Resolves a patch property name to the matching serialized key name from the original entity,
    /// allowing the patch document to use a different naming convention (e.g., snake_case)
    /// than the repository's internal serialization (e.g., camelCase).
    /// </summary>
    /// <param name="patchKey">The property name from the patch document.</param>
    /// <param name="propertyNameMap">Map from CLR property name to serialized key.</param>
    /// <returns>The matching serialized key, or the original patch key if no match is found.</returns>
    private static string ResolvePropertyName(string patchKey, Dictionary<string, string> propertyNameMap)
    {
        // Direct match against CLR property names (handles PascalCase patches)
        if (propertyNameMap.TryGetValue(patchKey, out var match))
        {
            return match;
        }

        // Normalize the patch key by stripping underscores for comparison
        // This handles snake_case → PascalCase mapping (e.g., "is_active" → "IsActive")
        var normalizedPatch = patchKey.Replace("_", string.Empty);
        foreach (var kvp in propertyNameMap)
        {
            if (kvp.Key.Equals(normalizedPatch, StringComparison.OrdinalIgnoreCase))
            {
                return kvp.Value;
            }
        }

        // No match found — use the patch key as-is
        return patchKey;
    }

    private static object? GetJsonValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => element.EnumerateArray().Select(GetJsonValue).ToList(),
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(p => p.Name, p => GetJsonValue(p.Value)),
            _ => element.GetRawText()
        };
    }

    private static int CompareValues(object? entityValue, object? filterValue)
    {
        if (entityValue is null && filterValue is null) return 0;
        if (entityValue is null) return -1;
        if (filterValue is null) return 1;

        if (entityValue is IComparable comparable)
        {
            return comparable.CompareTo(filterValue);
        }

        // Fallback: equality only
        return Equals(entityValue, filterValue) ? 0 : -1;
    }

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

        return typedValues.Any(v => Equals(entityValue, v));
    }

    private IEnumerable<TEntity> ApplyFilters(IEnumerable<TEntity> items, IReadOnlyList<FilterValue> filters)
    {
        foreach (var filter in filters)
        {
            items = items.Where(e => MatchesFilter(e, filter));
        }
        return items;
    }

    private IEnumerable<TEntity> ApplySorting(
        IEnumerable<TEntity> items,
        IReadOnlyList<SortField> sortFields)
    {
        if (sortFields.Count == 0)
        {
            // No sort requested — fall back to key ordering (preserves current behavior)
            return items.OrderBy(e => _keySelector(e));
        }

        IOrderedEnumerable<TEntity>? ordered = null;

        foreach (var field in sortFields)
        {
            var property = typeof(TEntity).GetProperty(
                field.PropertyName,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)!;

            Func<TEntity, object?> selector = e => property.GetValue(e);

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
        return ordered!.ThenBy(e => _keySelector(e));
    }

    private bool MatchesFilter(TEntity entity, FilterValue filter)
    {
        var property = typeof(TEntity).GetProperty(filter.PropertyName,
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.IgnoreCase);

        if (property == null)
        {
            return true; // Skip unknown properties
        }

        var entityValue = property.GetValue(entity);
        var filterValue = filter.TypedValue ?? ConvertFilterValue(filter.RawValue, property.PropertyType);

        return filter.Operator switch
        {
            FilterOperator.Eq => Equals(entityValue, filterValue),
            FilterOperator.Neq => !Equals(entityValue, filterValue),
            FilterOperator.Gt => CompareValues(entityValue, filterValue) > 0,
            FilterOperator.Lt => CompareValues(entityValue, filterValue) < 0,
            FilterOperator.Gte => CompareValues(entityValue, filterValue) >= 0,
            FilterOperator.Lte => CompareValues(entityValue, filterValue) <= 0,
            FilterOperator.Contains => ContainsString(entityValue, filterValue),
            FilterOperator.StartsWith => StartsWithString(entityValue, filterValue),
            FilterOperator.EndsWith => EndsWithString(entityValue, filterValue),
            FilterOperator.In => InValues(entityValue, filter.TypedValues),
            _ => Equals(entityValue, filterValue),
        };
    }

    private TEntity SetKeyOnEntity(TEntity entity, TKey key)
    {
        // Serialize entity to JSON, set the key property, and deserialize back
        var json = JsonSerializer.Serialize(entity, _jsonOptions);
        var doc = JsonDocument.Parse(json);
        var dict = new Dictionary<string, JsonElement>();

        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            dict[prop.Name] = prop.Value;
        }

        // Find the key property name
        var keyPropertyName = FindKeyPropertyName();
        if (keyPropertyName != null)
        {
            var keyJson = JsonSerializer.Serialize(key, _jsonOptions);
            using var keyDoc = JsonDocument.Parse(keyJson);
            dict[keyPropertyName] = keyDoc.RootElement.Clone();
        }

        var mergedJson = JsonSerializer.Serialize(dict, _jsonOptions);
        return JsonSerializer.Deserialize<TEntity>(mergedJson, _jsonOptions)!;
    }

    private string? FindKeyPropertyName()
    {
        if (_keyPropertyNameResolved)
        {
            return _cachedKeyPropertyName;
        }

        _cachedKeyPropertyName = DetectKeyPropertyName();
        _keyPropertyNameResolved = true;
        return _cachedKeyPropertyName;
    }

    /// <summary>
    /// Detects the key property name by probing each <typeparamref name="TKey"/>-typed
    /// property with the <c>_keySelector</c>. Falls back to the <c>Id</c> / <c>{Entity}Id</c>
    /// naming convention when probing does not yield a result.
    /// </summary>
    private string? DetectKeyPropertyName()
    {
        var properties = typeof(TEntity).GetProperties(
            BindingFlags.Public | BindingFlags.Instance);

        var candidates = properties
            .Where(p => p.PropertyType == typeof(TKey) && p.CanRead && p.CanWrite)
            .ToList();

        // Exactly one writable property of the key type — no ambiguity
        if (candidates.Count == 1)
        {
            return candidates[0].Name;
        }

        // Multiple candidates — probe each one using a JSON round-trip:
        // build a JSON object with only that property set to a generated key,
        // deserialize it to TEntity, and see if _keySelector returns the same value.
        if (candidates.Count > 1)
        {
            var probeKey = _keyGenerator();
            var probeKeyJson = JsonSerializer.Serialize(probeKey, _jsonOptions);

            foreach (var candidate in candidates)
            {
                try
                {
                    var jsonPropertyName = _jsonOptions.PropertyNamingPolicy?.ConvertName(candidate.Name)
                        ?? candidate.Name;
                    var json = $"{{\"{jsonPropertyName}\":{probeKeyJson}}}";
                    var testEntity = JsonSerializer.Deserialize<TEntity>(json, _jsonOptions);
                    if (testEntity != null && EqualityComparer<TKey>.Default.Equals(_keySelector(testEntity), probeKey))
                    {
                        return candidate.Name;
                    }
                }
                catch
                {
                    // Deserialization may fail for entities with required members — skip candidate
                }
            }
        }

        // Fall back to naming convention
        var conventionMatch = properties.FirstOrDefault(p =>
            p.Name.Equals("Id", StringComparison.OrdinalIgnoreCase) ||
            p.Name.Equals($"{typeof(TEntity).Name}Id", StringComparison.OrdinalIgnoreCase));

        return conventionMatch?.Name;
    }

    private string MergeJsonObjects(JsonElement original, JsonElement patch)
    {
        var merged = new Dictionary<string, object?>();

        // Start with original values
        foreach (var prop in original.EnumerateObject())
        {
            merged[prop.Name] = GetJsonValue(prop.Value);
        }

        // Build a lookup from the C# property names to the serialized key names
        // so we can map patch keys (which may use a different naming convention)
        // back to the original's key names.
        var propertyNameMap = BuildPropertyNameMap(merged.Keys);

        // Apply patch values (overwriting originals)
        foreach (var prop in patch.EnumerateObject())
        {
            // Resolve the patch key to the corresponding original key
            var key = ResolvePropertyName(prop.Name, propertyNameMap);
            merged[key] = GetJsonValue(prop.Value);
        }

        return JsonSerializer.Serialize(merged, _jsonOptions);
    }
}
