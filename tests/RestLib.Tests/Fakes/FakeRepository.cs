using System.Text.Json;
using RestLib.Abstractions;
using RestLib.Pagination;

namespace RestLib.Tests.Fakes;

/// <summary>
/// A simple test entity for unit tests.
/// </summary>
public class TestEntity
{
  public Guid Id { get; set; }
  public string Name { get; set; } = string.Empty;
  public decimal Price { get; set; }
}

/// <summary>
/// Entity with multiple-word property names for testing snake_case conversion.
/// </summary>
public class ProductEntity
{
  public Guid Id { get; set; }
  public string ProductName { get; set; } = string.Empty;
  public decimal UnitPrice { get; set; }
  public int StockQuantity { get; set; }
  public DateTime CreatedAt { get; set; }
  public DateTime? LastModifiedAt { get; set; }
  public string? OptionalDescription { get; set; }
  public bool IsActive { get; set; }
  public Guid? CategoryId { get; set; }
  public string? Status { get; set; }
}

/// <summary>
/// Entity type used in ServiceRegistrationTests.
/// </summary>
public class FakeEntity
{
  public Guid Id { get; set; }
  public string? Name { get; set; }
}

/// <summary>
/// Base class for fake repositories to reduce code duplication.
/// </summary>
public abstract class FakeRepositoryBase<TEntity, TKey> : IRepository<TEntity, TKey>
    where TEntity : class
    where TKey : notnull
{
  protected readonly Dictionary<TKey, TEntity> Store = new();

  protected abstract TKey GetId(TEntity entity);

  public virtual Task<TEntity?> GetByIdAsync(TKey id, CancellationToken ct = default)
  {
    Store.TryGetValue(id, out var entity);
    return Task.FromResult(entity);
  }

  public virtual Task<PagedResult<TEntity>> GetAllAsync(PaginationRequest pagination, CancellationToken ct = default)
  {
    var items = Store.Values.Take(pagination.Limit).ToList();
    return Task.FromResult(new PagedResult<TEntity>
    {
      Items = items,
      NextCursor = null
    });
  }

  public virtual Task<TEntity> CreateAsync(TEntity entity, CancellationToken ct = default)
  {
    Store[GetId(entity)] = entity;
    return Task.FromResult(entity);
  }

  public virtual Task<TEntity?> UpdateAsync(TKey id, TEntity entity, CancellationToken ct = default)
  {
    if (!Store.ContainsKey(id))
      return Task.FromResult<TEntity?>(null);

    Store[id] = entity;
    return Task.FromResult<TEntity?>(entity);
  }

  public virtual Task<TEntity?> PatchAsync(TKey id, JsonElement patchDocument, CancellationToken ct = default)
  {
    if (!Store.TryGetValue(id, out var entity))
      return Task.FromResult<TEntity?>(null);

    return Task.FromResult<TEntity?>(entity);
  }

  public virtual Task<bool> DeleteAsync(TKey id, CancellationToken ct = default)
  {
    return Task.FromResult(Store.Remove(id));
  }
}

/// <summary>
/// A fake in-memory repository for FakeEntity, used in ServiceRegistrationTests.
/// </summary>
public class FakeRepository : FakeRepositoryBase<FakeEntity, Guid>
{
  /// <summary>
  /// Optional tag for testing factory registration with service provider.
  /// </summary>
  public string? Tag { get; set; }

  protected override Guid GetId(FakeEntity entity) => entity.Id;
}

/// <summary>
/// A fake in-memory repository for testing purposes with TestEntity.
/// </summary>
public class TestEntityRepository : IRepository<TestEntity, Guid>
{
  private readonly Dictionary<Guid, TestEntity> _store = new();

  public Task<TestEntity?> GetByIdAsync(Guid id, CancellationToken ct = default)
  {
    _store.TryGetValue(id, out var entity);
    return Task.FromResult(entity);
  }

  public Task<PagedResult<TestEntity>> GetAllAsync(PaginationRequest pagination, CancellationToken ct = default)
  {
    var items = _store.Values.Take(pagination.Limit).ToList();
    return Task.FromResult(new PagedResult<TestEntity>
    {
      Items = items,
      NextCursor = null
    });
  }

  public Task<TestEntity> CreateAsync(TestEntity entity, CancellationToken ct = default)
  {
    if (entity.Id == Guid.Empty)
      entity.Id = Guid.NewGuid();

    _store[entity.Id] = entity;
    return Task.FromResult(entity);
  }

  public Task<TestEntity?> UpdateAsync(Guid id, TestEntity entity, CancellationToken ct = default)
  {
    if (!_store.ContainsKey(id))
      return Task.FromResult<TestEntity?>(null);

    entity.Id = id;
    _store[id] = entity;
    return Task.FromResult<TestEntity?>(entity);
  }

  public Task<TestEntity?> PatchAsync(Guid id, JsonElement patchDocument, CancellationToken ct = default)
  {
    if (!_store.TryGetValue(id, out var existing))
      return Task.FromResult<TestEntity?>(null);

    // Simple merge patch implementation
    if (patchDocument.TryGetProperty("name", out var nameElement))
      existing.Name = nameElement.GetString() ?? existing.Name;

    if (patchDocument.TryGetProperty("price", out var priceElement))
      existing.Price = priceElement.GetDecimal();

    _store[id] = existing;
    return Task.FromResult<TestEntity?>(existing);
  }

  public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
  {
    return Task.FromResult(_store.Remove(id));
  }

  // Helper methods for test setup
  public void Seed(params TestEntity[] entities)
  {
    foreach (var entity in entities)
    {
      _store[entity.Id] = entity;
    }
  }

  public void Clear() => _store.Clear();

  public int Count => _store.Count;
}

/// <summary>
/// A fake in-memory repository for ProductEntity used in JSON serialization tests.
/// </summary>
public class ProductEntityRepository : IRepository<ProductEntity, Guid>
{
  private readonly Dictionary<Guid, ProductEntity> _store = new();

  public Task<ProductEntity?> GetByIdAsync(Guid id, CancellationToken ct = default)
  {
    _store.TryGetValue(id, out var entity);
    return Task.FromResult(entity);
  }

  public Task<PagedResult<ProductEntity>> GetAllAsync(PaginationRequest pagination, CancellationToken ct = default)
  {
    var items = _store.Values.Take(pagination.Limit).ToList();
    return Task.FromResult(new PagedResult<ProductEntity>
    {
      Items = items,
      NextCursor = null
    });
  }

  public Task<ProductEntity> CreateAsync(ProductEntity entity, CancellationToken ct = default)
  {
    if (entity.Id == Guid.Empty)
      entity.Id = Guid.NewGuid();

    _store[entity.Id] = entity;
    return Task.FromResult(entity);
  }

  public Task<ProductEntity?> UpdateAsync(Guid id, ProductEntity entity, CancellationToken ct = default)
  {
    if (!_store.ContainsKey(id))
      return Task.FromResult<ProductEntity?>(null);

    entity.Id = id;
    _store[id] = entity;
    return Task.FromResult<ProductEntity?>(entity);
  }

  public Task<ProductEntity?> PatchAsync(Guid id, JsonElement patchDocument, CancellationToken ct = default)
  {
    if (!_store.TryGetValue(id, out var existing))
      return Task.FromResult<ProductEntity?>(null);

    // Simple merge patch implementation for ProductEntity
    if (patchDocument.TryGetProperty("product_name", out var nameElement))
      existing.ProductName = nameElement.GetString() ?? existing.ProductName;

    if (patchDocument.TryGetProperty("unit_price", out var priceElement))
      existing.UnitPrice = priceElement.GetDecimal();

    if (patchDocument.TryGetProperty("stock_quantity", out var stockElement))
      existing.StockQuantity = stockElement.GetInt32();

    if (patchDocument.TryGetProperty("is_active", out var activeElement))
      existing.IsActive = activeElement.GetBoolean();

    _store[id] = existing;
    return Task.FromResult<ProductEntity?>(existing);
  }

  public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
  {
    return Task.FromResult(_store.Remove(id));
  }

  // Helper methods for test setup
  public void Seed(params ProductEntity[] entities)
  {
    foreach (var entity in entities)
    {
      _store[entity.Id] = entity;
    }
  }

  public void Clear() => _store.Clear();

  public int Count => _store.Count;
}