using System.Collections.Concurrent;
using System.Text.Json;
using RestLib.Abstractions;
using RestLib.Filtering;
using RestLib.Pagination;

namespace RestLib.InMemory;

/// <summary>
/// Thread-safe in-memory implementation of <see cref="IRepository{TEntity, TKey}"/>.
/// Ideal for testing, prototyping, and scenarios where data persistence is not required.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TKey">The key type.</typeparam>
public class InMemoryRepository<TEntity, TKey> : IRepository<TEntity, TKey>
    where TEntity : class
    where TKey : notnull
{
  private readonly ConcurrentDictionary<TKey, TEntity> _store = new();
  private readonly Func<TEntity, TKey> _keySelector;
  private readonly Func<TKey> _keyGenerator;
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
  {
    _keySelector = keySelector ?? throw new ArgumentNullException(nameof(keySelector));
    _keyGenerator = keyGenerator ?? throw new ArgumentNullException(nameof(keyGenerator));
    _jsonOptions = jsonOptions ?? new JsonSerializerOptions
    {
      PropertyNameCaseInsensitive = true,
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
  }

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

    // Order items consistently by key for pagination
    var orderedItems = items.OrderBy(e => _keySelector(e)).ToList();

    // Apply cursor-based pagination
    int startIndex = 0;
    if (!string.IsNullOrEmpty(request.Cursor) && CursorEncoder.TryDecode<int>(request.Cursor, out var cursorIndex))
    {
      startIndex = cursorIndex;
    }

    var pagedItems = orderedItems
        .Skip(startIndex)
        .Take(request.Limit + 1) // Take one extra to determine if there are more
        .ToList();

    var hasMore = pagedItems.Count > request.Limit;
    if (hasMore)
    {
      pagedItems = pagedItems.Take(request.Limit).ToList();
    }

    string? nextCursor = hasMore ? CursorEncoder.Encode(startIndex + request.Limit) : null;

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

  /// <summary>
  /// Gets the current count of entities in the repository.
  /// </summary>
  public int Count => _store.Count;

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

  private IEnumerable<TEntity> ApplyFilters(IEnumerable<TEntity> items, IReadOnlyList<FilterValue> filters)
  {
    foreach (var filter in filters)
    {
      items = items.Where(e => MatchesFilter(e, filter));
    }
    return items;
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

    // Use equality comparison for in-memory filtering
    return Equals(entityValue, filterValue);
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
        return DateTime.Parse(value);
      }
      if (underlyingType == typeof(DateTimeOffset))
      {
        return DateTimeOffset.Parse(value);
      }
      if (underlyingType.IsEnum)
      {
        return Enum.Parse(underlyingType, value, ignoreCase: true);
      }
      return Convert.ChangeType(value, underlyingType);
    }
    catch
    {
      return value;
    }
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
    // Try to find a property that looks like a key
    var properties = typeof(TEntity).GetProperties();

    // First, try to find the property that the keySelector uses
    // by creating a dummy instance and checking which property returns the key
    var idProperty = properties.FirstOrDefault(p =>
        p.Name.Equals("Id", StringComparison.OrdinalIgnoreCase) ||
        p.Name.Equals($"{typeof(TEntity).Name}Id", StringComparison.OrdinalIgnoreCase));

    return idProperty?.Name;
  }

  private static string MergeJsonObjects(JsonElement original, JsonElement patch)
  {
    var merged = new Dictionary<string, object?>();

    // Start with original values
    foreach (var prop in original.EnumerateObject())
    {
      merged[prop.Name] = GetJsonValue(prop.Value);
    }

    // Apply patch values (overwriting originals)
    foreach (var prop in patch.EnumerateObject())
    {
      merged[prop.Name] = GetJsonValue(prop.Value);
    }

    return JsonSerializer.Serialize(merged);
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
}
