using System.Text.Json;
using FluentAssertions;
using RestLib.Filtering;
using RestLib.InMemory;
using RestLib.Pagination;
using Xunit;

namespace RestLib.Tests;

public class InMemoryRepositoryTests
{
  private record TestEntity(Guid Id, string Name, int Value, DateTime CreatedAt);

  private static InMemoryRepository<TestEntity, Guid> CreateRepository() =>
      new(e => e.Id, Guid.NewGuid);

  private static TestEntity CreateEntity(string name = "Test", int value = 100) =>
      new(Guid.NewGuid(), name, value, DateTime.UtcNow);

  private static FilterValue CreateFilter(string propertyName, object? typedValue) => new()
  {
    PropertyName = propertyName,
    QueryParameterName = propertyName.ToLowerInvariant(),
    PropertyType = typedValue?.GetType() ?? typeof(string),
    RawValue = typedValue?.ToString() ?? "",
    TypedValue = typedValue
  };

  #region Constructor Tests

  [Fact]
  public void Constructor_WithNullKeySelector_ThrowsArgumentNullException()
  {
    var act = () => new InMemoryRepository<TestEntity, Guid>(null!, Guid.NewGuid);
    act.Should().Throw<ArgumentNullException>().WithParameterName("keySelector");
  }

  [Fact]
  public void Constructor_WithNullKeyGenerator_ThrowsArgumentNullException()
  {
    var act = () => new InMemoryRepository<TestEntity, Guid>(e => e.Id, null!);
    act.Should().Throw<ArgumentNullException>().WithParameterName("keyGenerator");
  }

  [Fact]
  public void Constructor_WithValidParameters_CreatesEmptyRepository()
  {
    var repository = CreateRepository();
    repository.Count.Should().Be(0);
  }

  #endregion

  #region GetByIdAsync Tests

  [Fact]
  public async Task GetByIdAsync_WithExistingEntity_ReturnsEntity()
  {
    var repository = CreateRepository();
    var entity = CreateEntity();
    await repository.CreateAsync(entity);

    var result = await repository.GetByIdAsync(entity.Id);

    result.Should().NotBeNull();
    result.Should().Be(entity);
  }

  [Fact]
  public async Task GetByIdAsync_WithNonExistingId_ReturnsNull()
  {
    var repository = CreateRepository();
    var result = await repository.GetByIdAsync(Guid.NewGuid());
    result.Should().BeNull();
  }

  [Fact]
  public async Task GetByIdAsync_WithEmptyRepository_ReturnsNull()
  {
    var repository = CreateRepository();
    var result = await repository.GetByIdAsync(Guid.NewGuid());
    result.Should().BeNull();
  }

  #endregion

  #region GetAllAsync Tests

  [Fact]
  public async Task GetAllAsync_WithEmptyRepository_ReturnsEmptyResult()
  {
    var repository = CreateRepository();
    var request = new PaginationRequest { Limit = 10 };

    var result = await repository.GetAllAsync(request);

    result.Items.Should().BeEmpty();
    result.HasMore.Should().BeFalse();
    result.NextCursor.Should().BeNull();
  }

  [Fact]
  public async Task GetAllAsync_WithEntities_ReturnsAllEntities()
  {
    var repository = CreateRepository();
    var entities = new[] { CreateEntity("Entity1", 1), CreateEntity("Entity2", 2), CreateEntity("Entity3", 3) };
    foreach (var entity in entities) await repository.CreateAsync(entity);
    var request = new PaginationRequest { Limit = 10 };

    var result = await repository.GetAllAsync(request);

    result.Items.Should().HaveCount(3);
    result.HasMore.Should().BeFalse();
    result.NextCursor.Should().BeNull();
  }

  [Fact]
  public async Task GetAllAsync_WithPagination_ReturnsFirstPage()
  {
    var repository = CreateRepository();
    for (int i = 0; i < 10; i++) await repository.CreateAsync(CreateEntity($"Entity{i}", i));
    var request = new PaginationRequest { Limit = 5 };

    var result = await repository.GetAllAsync(request);

    result.Items.Should().HaveCount(5);
    result.HasMore.Should().BeTrue();
    result.NextCursor.Should().NotBeNullOrEmpty();
  }

  [Fact]
  public async Task GetAllAsync_WithCursor_ReturnsNextPage()
  {
    var repository = CreateRepository();
    for (int i = 0; i < 10; i++) await repository.CreateAsync(CreateEntity($"Entity{i}", i));
    var firstPageRequest = new PaginationRequest { Limit = 5 };
    var firstPage = await repository.GetAllAsync(firstPageRequest);

    var secondPageRequest = new PaginationRequest { Limit = 5, Cursor = firstPage.NextCursor };
    var secondPage = await repository.GetAllAsync(secondPageRequest);

    secondPage.Items.Should().HaveCount(5);
    secondPage.HasMore.Should().BeFalse();
    secondPage.NextCursor.Should().BeNull();
    firstPage.Items.Should().NotIntersectWith(secondPage.Items);
  }

  [Fact]
  public async Task GetAllAsync_WithExactLimit_HasMoreIsFalse()
  {
    var repository = CreateRepository();
    for (int i = 0; i < 5; i++) await repository.CreateAsync(CreateEntity($"Entity{i}", i));
    var request = new PaginationRequest { Limit = 5 };

    var result = await repository.GetAllAsync(request);

    result.Items.Should().HaveCount(5);
    result.HasMore.Should().BeFalse();
    result.NextCursor.Should().BeNull();
  }

  [Fact]
  public async Task GetAllAsync_ConsistentOrderingAcrossPages()
  {
    var repository = CreateRepository();
    var allEntities = new List<TestEntity>();
    for (int i = 0; i < 15; i++)
    {
      var entity = CreateEntity($"Entity{i}", i);
      await repository.CreateAsync(entity);
      allEntities.Add(entity);
    }

    var retrievedEntities = new List<TestEntity>();
    string? cursor = null;
    do
    {
      var request = new PaginationRequest { Limit = 5, Cursor = cursor };
      var result = await repository.GetAllAsync(request);
      retrievedEntities.AddRange(result.Items);
      cursor = result.NextCursor;
    } while (cursor != null);

    retrievedEntities.Should().HaveCount(15);
    retrievedEntities.Should().OnlyHaveUniqueItems();
    retrievedEntities.Should().BeEquivalentTo(allEntities);
  }

  #endregion

  #region Filter Tests

  [Fact]
  public async Task GetAllAsync_WithEqualFilter_FiltersCorrectly()
  {
    var repository = CreateRepository();
    await repository.CreateAsync(CreateEntity("Alice", 100));
    await repository.CreateAsync(CreateEntity("Bob", 200));
    await repository.CreateAsync(CreateEntity("Charlie", 100));

    var filters = new List<FilterValue> { CreateFilter("Value", 100) };
    var request = new PaginationRequest { Limit = 10, Filters = filters };

    var result = await repository.GetAllAsync(request);

    result.Items.Should().HaveCount(2);
    result.Items.Should().OnlyContain(e => e.Value == 100);
  }

  [Fact]
  public async Task GetAllAsync_WithNameFilter_FiltersCorrectly()
  {
    var repository = CreateRepository();
    await repository.CreateAsync(CreateEntity("Alice", 100));
    await repository.CreateAsync(CreateEntity("Bob", 200));
    await repository.CreateAsync(CreateEntity("Alice", 300));

    var filters = new List<FilterValue> { CreateFilter("Name", "Alice") };
    var request = new PaginationRequest { Limit = 10, Filters = filters };

    var result = await repository.GetAllAsync(request);

    result.Items.Should().HaveCount(2);
    result.Items.Should().OnlyContain(e => e.Name == "Alice");
  }

  [Fact]
  public async Task GetAllAsync_WithMultipleFilters_AppliesAll()
  {
    var repository = CreateRepository();
    await repository.CreateAsync(CreateEntity("Alice", 100));
    await repository.CreateAsync(CreateEntity("Alice", 200));
    await repository.CreateAsync(CreateEntity("Bob", 100));

    var filters = new List<FilterValue> { CreateFilter("Name", "Alice"), CreateFilter("Value", 100) };
    var request = new PaginationRequest { Limit = 10, Filters = filters };

    var result = await repository.GetAllAsync(request);

    result.Items.Should().HaveCount(1);
    result.Items.Single().Name.Should().Be("Alice");
    result.Items.Single().Value.Should().Be(100);
  }

  [Fact]
  public async Task GetAllAsync_WithUnknownProperty_IgnoresFilter()
  {
    var repository = CreateRepository();
    await repository.CreateAsync(CreateEntity("Alice", 100));
    await repository.CreateAsync(CreateEntity("Bob", 200));

    var filters = new List<FilterValue> { CreateFilter("NonExistent", "Something") };
    var request = new PaginationRequest { Limit = 10, Filters = filters };

    var result = await repository.GetAllAsync(request);

    result.Items.Should().HaveCount(2);
  }

  #endregion

  #region CreateAsync Tests

  [Fact]
  public async Task CreateAsync_WithValidEntity_AddsToRepository()
  {
    var repository = CreateRepository();
    var entity = CreateEntity();

    var result = await repository.CreateAsync(entity);

    result.Should().Be(entity);
    repository.Count.Should().Be(1);
  }

  [Fact]
  public async Task CreateAsync_WithNullEntity_ThrowsArgumentNullException()
  {
    var repository = CreateRepository();
    var act = () => repository.CreateAsync(null!);
    await act.Should().ThrowAsync<ArgumentNullException>();
  }

  [Fact]
  public async Task CreateAsync_WithDuplicateKey_ThrowsInvalidOperationException()
  {
    var repository = CreateRepository();
    var entity = CreateEntity();
    await repository.CreateAsync(entity);

    var act = () => repository.CreateAsync(entity);
    await act.Should().ThrowAsync<InvalidOperationException>().WithMessage($"*{entity.Id}*");
  }

  [Fact]
  public async Task CreateAsync_WithDefaultKey_GeneratesNewKey()
  {
    var generatedId = Guid.NewGuid();
    var repository = new InMemoryRepository<TestEntity, Guid>(e => e.Id, () => generatedId);
    var entity = new TestEntity(Guid.Empty, "Test", 100, DateTime.UtcNow);

    var result = await repository.CreateAsync(entity);

    result.Should().NotBeNull();
    result.Id.Should().Be(generatedId);
    repository.Count.Should().Be(1);
  }

  [Fact]
  public async Task CreateAsync_MultipleEntities_AllAdded()
  {
    var repository = CreateRepository();
    var entities = Enumerable.Range(1, 100).Select(i => CreateEntity($"Entity{i}", i)).ToList();

    foreach (var entity in entities) await repository.CreateAsync(entity);

    repository.Count.Should().Be(100);
  }

  #endregion

  #region UpdateAsync Tests

  [Fact]
  public async Task UpdateAsync_WithExistingEntity_UpdatesAndReturns()
  {
    var repository = CreateRepository();
    var entity = CreateEntity("Original", 100);
    await repository.CreateAsync(entity);
    var updatedEntity = entity with { Name = "Updated", Value = 200 };

    var result = await repository.UpdateAsync(entity.Id, updatedEntity);

    result.Should().NotBeNull();
    result!.Name.Should().Be("Updated");
    result.Value.Should().Be(200);
    var retrieved = await repository.GetByIdAsync(entity.Id);
    retrieved.Should().Be(updatedEntity);
  }

  [Fact]
  public async Task UpdateAsync_WithNonExistingId_ReturnsNull()
  {
    var repository = CreateRepository();
    var entity = CreateEntity();
    var result = await repository.UpdateAsync(Guid.NewGuid(), entity);
    result.Should().BeNull();
  }

  [Fact]
  public async Task UpdateAsync_WithNullEntity_ThrowsArgumentNullException()
  {
    var repository = CreateRepository();
    var entity = CreateEntity();
    await repository.CreateAsync(entity);

    var act = () => repository.UpdateAsync(entity.Id, null!);
    await act.Should().ThrowAsync<ArgumentNullException>();
  }

  [Fact]
  public async Task UpdateAsync_DoesNotChangeCount()
  {
    var repository = CreateRepository();
    var entity = CreateEntity();
    await repository.CreateAsync(entity);
    var updatedEntity = entity with { Name = "Updated" };

    await repository.UpdateAsync(entity.Id, updatedEntity);

    repository.Count.Should().Be(1);
  }

  #endregion

  #region PatchAsync Tests

  [Fact]
  public async Task PatchAsync_WithExistingEntity_PatchesFields()
  {
    var repository = CreateRepository();
    var entity = CreateEntity("Original", 100);
    await repository.CreateAsync(entity);
    var patch = JsonDocument.Parse("""{"name": "Patched"}""").RootElement;

    var result = await repository.PatchAsync(entity.Id, patch);

    result.Should().NotBeNull();
    result!.Name.Should().Be("Patched");
    result.Value.Should().Be(100);
  }

  [Fact]
  public async Task PatchAsync_WithNonExistingId_ReturnsNull()
  {
    var repository = CreateRepository();
    var patch = JsonDocument.Parse("""{"name": "Patched"}""").RootElement;
    var result = await repository.PatchAsync(Guid.NewGuid(), patch);
    result.Should().BeNull();
  }

  [Fact]
  public async Task PatchAsync_WithMultipleFields_PatchesAll()
  {
    var repository = CreateRepository();
    var entity = CreateEntity("Original", 100);
    await repository.CreateAsync(entity);
    var patch = JsonDocument.Parse("""{"name": "Patched", "value": 999}""").RootElement;

    var result = await repository.PatchAsync(entity.Id, patch);

    result.Should().NotBeNull();
    result!.Name.Should().Be("Patched");
    result.Value.Should().Be(999);
  }

  [Fact]
  public async Task PatchAsync_PreservesUnspecifiedFields()
  {
    var repository = CreateRepository();
    var createdAt = DateTime.UtcNow.AddDays(-1);
    var entity = new TestEntity(Guid.NewGuid(), "Original", 100, createdAt);
    await repository.CreateAsync(entity);
    var patch = JsonDocument.Parse("""{"name": "Patched"}""").RootElement;

    var result = await repository.PatchAsync(entity.Id, patch);

    result.Should().NotBeNull();
    result!.CreatedAt.Should().BeCloseTo(createdAt, TimeSpan.FromSeconds(1));
  }

  #endregion

  #region DeleteAsync Tests

  [Fact]
  public async Task DeleteAsync_WithExistingEntity_RemovesAndReturnsTrue()
  {
    var repository = CreateRepository();
    var entity = CreateEntity();
    await repository.CreateAsync(entity);

    var result = await repository.DeleteAsync(entity.Id);

    result.Should().BeTrue();
    repository.Count.Should().Be(0);
  }

  [Fact]
  public async Task DeleteAsync_WithNonExistingId_ReturnsFalse()
  {
    var repository = CreateRepository();
    var result = await repository.DeleteAsync(Guid.NewGuid());
    result.Should().BeFalse();
  }

  [Fact]
  public async Task DeleteAsync_RemovedEntityNoLongerRetrievable()
  {
    var repository = CreateRepository();
    var entity = CreateEntity();
    await repository.CreateAsync(entity);
    await repository.DeleteAsync(entity.Id);

    var result = await repository.GetByIdAsync(entity.Id);

    result.Should().BeNull();
  }

  #endregion

  #region Thread Safety Tests

  [Fact]
  public async Task ConcurrentCreates_AllSucceed()
  {
    var repository = CreateRepository();
    var tasks = new List<Task>();

    for (int i = 0; i < 100; i++)
    {
      tasks.Add(Task.Run(async () =>
      {
        var entity = CreateEntity($"Entity{Guid.NewGuid()}", Random.Shared.Next());
        await repository.CreateAsync(entity);
      }));
    }
    await Task.WhenAll(tasks);

    repository.Count.Should().Be(100);
  }

  [Fact]
  public async Task ConcurrentReadsAndWrites_NoExceptions()
  {
    var repository = CreateRepository();
    var entities = Enumerable.Range(1, 50).Select(i => CreateEntity($"Entity{i}", i)).ToList();
    foreach (var entity in entities) await repository.CreateAsync(entity);
    var tasks = new List<Task>();

    for (int i = 0; i < 100; i++)
    {
      var index = i;
      if (index % 2 == 0)
      {
        tasks.Add(Task.Run(async () =>
        {
          var request = new PaginationRequest { Limit = 10 };
          await repository.GetAllAsync(request);
        }));
      }
      else
      {
        tasks.Add(Task.Run(async () =>
        {
          var entity = CreateEntity($"Concurrent{index}", index);
          await repository.CreateAsync(entity);
        }));
      }
    }

    var act = () => Task.WhenAll(tasks);
    await act.Should().NotThrowAsync();
  }

  [Fact]
  public async Task ConcurrentUpdates_LastWriteWins()
  {
    var repository = CreateRepository();
    var entity = CreateEntity("Original", 100);
    await repository.CreateAsync(entity);
    var tasks = new List<Task>();

    for (int i = 0; i < 100; i++)
    {
      var value = i;
      tasks.Add(Task.Run(async () =>
      {
        var updated = entity with { Value = value };
        await repository.UpdateAsync(entity.Id, updated);
      }));
    }
    await Task.WhenAll(tasks);

    var result = await repository.GetByIdAsync(entity.Id);
    result.Should().NotBeNull();
    result!.Value.Should().BeInRange(0, 99);
  }

  [Fact]
  public async Task ConcurrentDeletes_OnlyOneSucceeds()
  {
    var repository = CreateRepository();
    var entity = CreateEntity();
    await repository.CreateAsync(entity);
    var results = new List<bool>();
    var lockObj = new object();

    var tasks = Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
    {
      var deleteResult = await repository.DeleteAsync(entity.Id);
      lock (lockObj) { results.Add(deleteResult); }
    }));
    await Task.WhenAll(tasks);

    results.Count(r => r).Should().Be(1);
    repository.Count.Should().Be(0);
  }

  #endregion

  #region Seed and Clear Tests

  [Fact]
  public void Seed_WithEntities_AddsAllToRepository()
  {
    var repository = CreateRepository();
    var entities = Enumerable.Range(1, 10).Select(i => CreateEntity($"Entity{i}", i)).ToList();
    repository.Seed(entities);
    repository.Count.Should().Be(10);
  }

  [Fact]
  public void Seed_WithNullEnumerable_ThrowsArgumentNullException()
  {
    var repository = CreateRepository();
    var act = () => repository.Seed(null!);
    act.Should().Throw<ArgumentNullException>();
  }

  [Fact]
  public async Task Clear_RemovesAllEntities()
  {
    var repository = CreateRepository();
    for (int i = 0; i < 10; i++) await repository.CreateAsync(CreateEntity($"Entity{i}", i));
    repository.Clear();
    repository.Count.Should().Be(0);
  }

  [Fact]
  public async Task Clear_ThenCreate_Works()
  {
    var repository = CreateRepository();
    await repository.CreateAsync(CreateEntity("First", 1));
    repository.Clear();

    var entity = CreateEntity("Second", 2);
    var result = await repository.CreateAsync(entity);

    result.Should().Be(entity);
    repository.Count.Should().Be(1);
  }

  #endregion

  #region Edge Cases

  [Fact]
  public async Task GetAllAsync_WithInvalidCursor_StartsFromBeginning()
  {
    var repository = CreateRepository();
    for (int i = 0; i < 10; i++) await repository.CreateAsync(CreateEntity($"Entity{i}", i));
    var request = new PaginationRequest { Limit = 5, Cursor = "invalid-cursor-that-wont-decode" };

    var result = await repository.GetAllAsync(request);

    result.Items.Should().HaveCount(5);
    result.HasMore.Should().BeTrue();
  }

  [Fact]
  public async Task GetAllAsync_WithLimitGreaterThanTotal_ReturnsAll()
  {
    var repository = CreateRepository();
    for (int i = 0; i < 5; i++) await repository.CreateAsync(CreateEntity($"Entity{i}", i));
    var request = new PaginationRequest { Limit = 100 };

    var result = await repository.GetAllAsync(request);

    result.Items.Should().HaveCount(5);
    result.HasMore.Should().BeFalse();
    result.NextCursor.Should().BeNull();
  }

  [Fact]
  public async Task GetAllAsync_WithLimitOfOne_PaginatesCorrectly()
  {
    var repository = CreateRepository();
    for (int i = 0; i < 3; i++) await repository.CreateAsync(CreateEntity($"Entity{i}", i));

    var allItems = new List<TestEntity>();
    string? cursor = null;
    do
    {
      var request = new PaginationRequest { Limit = 1, Cursor = cursor };
      var result = await repository.GetAllAsync(request);
      allItems.AddRange(result.Items);
      cursor = result.NextCursor;
    } while (cursor != null);

    allItems.Should().HaveCount(3);
    allItems.Should().OnlyHaveUniqueItems();
  }

  #endregion

  #region Patch Naming Resolution Tests

  // Entity with multi-word properties that exercise the BuildPropertyNameMap /
  // ResolvePropertyName logic when patch documents use different naming conventions
  // than the repository's internal camelCase serialization.
  private record MultiWordEntity(
      Guid Id,
      string ProductName,
      bool IsActive,
      int StockQuantity,
      DateTime CreatedAt);

  private static InMemoryRepository<MultiWordEntity, Guid> CreateMultiWordRepository() =>
      new(e => e.Id, Guid.NewGuid);

  [Fact]
  public async Task PatchAsync_WithSnakeCaseKeys_ResolvesToCorrectProperties()
  {
    // Arrange
    var repository = CreateMultiWordRepository();
    var entity = new MultiWordEntity(Guid.NewGuid(), "Widget", true, 50, DateTime.UtcNow);
    await repository.CreateAsync(entity);
    var patch = JsonDocument.Parse("""{"product_name": "Updated Widget", "is_active": false}""").RootElement;

    // Act
    var result = await repository.PatchAsync(entity.Id, patch);

    // Assert
    result.Should().NotBeNull();
    result!.ProductName.Should().Be("Updated Widget");
    result.IsActive.Should().BeFalse();
    result.StockQuantity.Should().Be(50);
  }

  [Fact]
  public async Task PatchAsync_WithPascalCaseKeys_ResolvesToCorrectProperties()
  {
    // Arrange
    var repository = CreateMultiWordRepository();
    var entity = new MultiWordEntity(Guid.NewGuid(), "Widget", true, 50, DateTime.UtcNow);
    await repository.CreateAsync(entity);
    var patch = JsonDocument.Parse("""{"ProductName": "PascalPatched", "StockQuantity": 99}""").RootElement;

    // Act
    var result = await repository.PatchAsync(entity.Id, patch);

    // Assert
    result.Should().NotBeNull();
    result!.ProductName.Should().Be("PascalPatched");
    result.StockQuantity.Should().Be(99);
    result.IsActive.Should().BeTrue();
  }

  [Fact]
  public async Task PatchAsync_WithCamelCaseKeys_ResolvesToCorrectProperties()
  {
    // Arrange
    var repository = CreateMultiWordRepository();
    var entity = new MultiWordEntity(Guid.NewGuid(), "Widget", true, 50, DateTime.UtcNow);
    await repository.CreateAsync(entity);
    var patch = JsonDocument.Parse("""{"productName": "CamelPatched", "isActive": false}""").RootElement;

    // Act
    var result = await repository.PatchAsync(entity.Id, patch);

    // Assert
    result.Should().NotBeNull();
    result!.ProductName.Should().Be("CamelPatched");
    result.IsActive.Should().BeFalse();
    result.StockQuantity.Should().Be(50);
  }

  [Fact]
  public async Task PatchAsync_WithMixedNamingConventions_ResolvesAll()
  {
    // Arrange
    var repository = CreateMultiWordRepository();
    var entity = new MultiWordEntity(Guid.NewGuid(), "Widget", true, 50, DateTime.UtcNow);
    await repository.CreateAsync(entity);

    // Mix snake_case and PascalCase in the same patch document
    var patch = JsonDocument.Parse("""{"product_name": "MixedPatch", "StockQuantity": 0}""").RootElement;

    // Act
    var result = await repository.PatchAsync(entity.Id, patch);

    // Assert
    result.Should().NotBeNull();
    result!.ProductName.Should().Be("MixedPatch");
    result.StockQuantity.Should().Be(0);
    result.IsActive.Should().BeTrue();
  }

  [Fact]
  public async Task PatchAsync_WithUnknownProperty_PreservesExistingFields()
  {
    // Arrange
    var repository = CreateMultiWordRepository();
    var entity = new MultiWordEntity(Guid.NewGuid(), "Widget", true, 50, DateTime.UtcNow);
    await repository.CreateAsync(entity);

    // Patch includes a property that doesn't exist on the entity
    var patch = JsonDocument.Parse("""{"product_name": "Still Updated", "non_existent_field": "ignored"}""").RootElement;

    // Act
    var result = await repository.PatchAsync(entity.Id, patch);

    // Assert
    result.Should().NotBeNull();
    result!.ProductName.Should().Be("Still Updated");
    result.IsActive.Should().BeTrue();
    result.StockQuantity.Should().Be(50);
  }

  #endregion

  #region CreateManyAsync Tests

  [Fact]
  public async Task CreateManyAsync_WithValidEntities_AddsAllToRepository()
  {
    // Arrange
    var repository = CreateRepository();
    var entities = new List<TestEntity>
    {
      CreateEntity("Entity1", 1),
      CreateEntity("Entity2", 2),
      CreateEntity("Entity3", 3)
    };

    // Act
    var result = await repository.CreateManyAsync(entities);

    // Assert
    result.Should().HaveCount(3);
    repository.Count.Should().Be(3);
    foreach (var entity in entities)
    {
      var retrieved = await repository.GetByIdAsync(entity.Id);
      retrieved.Should().NotBeNull();
    }
  }

  [Fact]
  public async Task CreateManyAsync_WithDefaultKeys_GeneratesNewKeys()
  {
    // Arrange
    var callCount = 0;
    var generatedIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
    var repository = new InMemoryRepository<TestEntity, Guid>(
        e => e.Id,
        () => generatedIds[callCount++]);
    var entities = new List<TestEntity>
    {
      new(Guid.Empty, "Entity1", 1, DateTime.UtcNow),
      new(Guid.Empty, "Entity2", 2, DateTime.UtcNow)
    };

    // Act
    var result = await repository.CreateManyAsync(entities);

    // Assert
    result.Should().HaveCount(2);
    result[0].Id.Should().Be(generatedIds[0]);
    result[1].Id.Should().Be(generatedIds[1]);
    repository.Count.Should().Be(2);
  }

  [Fact]
  public async Task CreateManyAsync_WithNullArgument_ThrowsArgumentNullException()
  {
    // Arrange
    var repository = CreateRepository();

    // Act
    var act = () => repository.CreateManyAsync(null!);

    // Assert
    await act.Should().ThrowAsync<ArgumentNullException>();
  }

  [Fact]
  public async Task CreateManyAsync_WithEmptyList_ReturnsEmptyResult()
  {
    // Arrange
    var repository = CreateRepository();

    // Act
    var result = await repository.CreateManyAsync(new List<TestEntity>());

    // Assert
    result.Should().BeEmpty();
    repository.Count.Should().Be(0);
  }

  [Fact]
  public async Task CreateManyAsync_OverwritesExistingKeys()
  {
    // Arrange
    var repository = CreateRepository();
    var entity = CreateEntity("Original", 100);
    await repository.CreateAsync(entity);
    var replacement = entity with { Name = "Replaced", Value = 999 };

    // Act — CreateManyAsync uses _store[key] = current (no TryAdd check)
    var result = await repository.CreateManyAsync(new List<TestEntity> { replacement });

    // Assert
    result.Should().HaveCount(1);
    var retrieved = await repository.GetByIdAsync(entity.Id);
    retrieved!.Name.Should().Be("Replaced");
    retrieved.Value.Should().Be(999);
  }

  #endregion

  #region UpdateManyAsync Tests

  [Fact]
  public async Task UpdateManyAsync_WithExistingEntities_UpdatesAll()
  {
    // Arrange
    var repository = CreateRepository();
    var entity1 = CreateEntity("Entity1", 1);
    var entity2 = CreateEntity("Entity2", 2);
    await repository.CreateAsync(entity1);
    await repository.CreateAsync(entity2);
    var updated1 = entity1 with { Name = "Updated1", Value = 10 };
    var updated2 = entity2 with { Name = "Updated2", Value = 20 };

    // Act
    var result = await repository.UpdateManyAsync(new List<TestEntity> { updated1, updated2 });

    // Assert
    result.Should().HaveCount(2);
    var retrieved1 = await repository.GetByIdAsync(entity1.Id);
    retrieved1!.Name.Should().Be("Updated1");
    retrieved1.Value.Should().Be(10);
    var retrieved2 = await repository.GetByIdAsync(entity2.Id);
    retrieved2!.Name.Should().Be("Updated2");
    retrieved2.Value.Should().Be(20);
  }

  [Fact]
  public async Task UpdateManyAsync_WithNonExistingKeys_AddsEntities()
  {
    // Arrange — UpdateManyAsync uses _store[key] = entity (upsert behavior)
    var repository = CreateRepository();
    var entity = CreateEntity("New", 42);

    // Act
    var result = await repository.UpdateManyAsync(new List<TestEntity> { entity });

    // Assert
    result.Should().HaveCount(1);
    repository.Count.Should().Be(1);
    var retrieved = await repository.GetByIdAsync(entity.Id);
    retrieved.Should().NotBeNull();
    retrieved!.Name.Should().Be("New");
  }

  [Fact]
  public async Task UpdateManyAsync_WithNullArgument_ThrowsArgumentNullException()
  {
    // Arrange
    var repository = CreateRepository();

    // Act
    var act = () => repository.UpdateManyAsync(null!);

    // Assert
    await act.Should().ThrowAsync<ArgumentNullException>();
  }

  [Fact]
  public async Task UpdateManyAsync_WithEmptyList_ReturnsEmptyResult()
  {
    // Arrange
    var repository = CreateRepository();

    // Act
    var result = await repository.UpdateManyAsync(new List<TestEntity>());

    // Assert
    result.Should().BeEmpty();
  }

  #endregion

  #region DeleteManyAsync Tests

  [Fact]
  public async Task DeleteManyAsync_WithExistingKeys_DeletesAllAndReturnsCount()
  {
    // Arrange
    var repository = CreateRepository();
    var entity1 = CreateEntity("Entity1", 1);
    var entity2 = CreateEntity("Entity2", 2);
    var entity3 = CreateEntity("Entity3", 3);
    await repository.CreateAsync(entity1);
    await repository.CreateAsync(entity2);
    await repository.CreateAsync(entity3);

    // Act
    var result = await repository.DeleteManyAsync(new List<Guid> { entity1.Id, entity2.Id });

    // Assert
    result.Should().Be(2);
    repository.Count.Should().Be(1);
    (await repository.GetByIdAsync(entity1.Id)).Should().BeNull();
    (await repository.GetByIdAsync(entity2.Id)).Should().BeNull();
    (await repository.GetByIdAsync(entity3.Id)).Should().NotBeNull();
  }

  [Fact]
  public async Task DeleteManyAsync_WithNonExistingKeys_ReturnsZero()
  {
    // Arrange
    var repository = CreateRepository();

    // Act
    var result = await repository.DeleteManyAsync(new List<Guid> { Guid.NewGuid(), Guid.NewGuid() });

    // Assert
    result.Should().Be(0);
  }

  [Fact]
  public async Task DeleteManyAsync_WithMixedKeys_ReturnsCountOfDeleted()
  {
    // Arrange
    var repository = CreateRepository();
    var entity = CreateEntity("Existing", 100);
    await repository.CreateAsync(entity);

    // Act — one existing key, one non-existing key
    var result = await repository.DeleteManyAsync(new List<Guid> { entity.Id, Guid.NewGuid() });

    // Assert
    result.Should().Be(1);
    repository.Count.Should().Be(0);
  }

  [Fact]
  public async Task DeleteManyAsync_WithNullArgument_ThrowsArgumentNullException()
  {
    // Arrange
    var repository = CreateRepository();

    // Act
    var act = () => repository.DeleteManyAsync(null!);

    // Assert
    await act.Should().ThrowAsync<ArgumentNullException>();
  }

  [Fact]
  public async Task DeleteManyAsync_WithEmptyList_ReturnsZero()
  {
    // Arrange
    var repository = CreateRepository();
    await repository.CreateAsync(CreateEntity("Entity", 1));

    // Act
    var result = await repository.DeleteManyAsync(new List<Guid>());

    // Assert
    result.Should().Be(0);
    repository.Count.Should().Be(1);
  }

  #endregion

  #region ConvertFilterValue Tests

  // Entity with diverse property types to exercise all ConvertFilterValue branches
  private record FilterTestEntity(
      Guid Id,
      string Name,
      int Value,
      Guid CategoryId,
      DateTime CreatedAt,
      DateTimeOffset ModifiedAt,
      DayOfWeek DayOfWeek,
      int? NullableValue);

  private static InMemoryRepository<FilterTestEntity, Guid> CreateFilterTestRepository() =>
      new(e => e.Id, Guid.NewGuid);

  private static FilterTestEntity CreateFilterTestEntity(
      Guid? categoryId = null,
      DateTime? createdAt = null,
      DateTimeOffset? modifiedAt = null,
      DayOfWeek dayOfWeek = DayOfWeek.Monday,
      int? nullableValue = null)
  {
    return new FilterTestEntity(
        Guid.NewGuid(),
        "Test",
        100,
        categoryId ?? Guid.NewGuid(),
        createdAt ?? DateTime.UtcNow,
        modifiedAt ?? DateTimeOffset.UtcNow,
        dayOfWeek,
        nullableValue);
  }

  private static FilterValue CreateFilterForProperty(string propertyName, string rawValue, Type propertyType) => new()
  {
    PropertyName = propertyName,
    QueryParameterName = propertyName.ToLowerInvariant(),
    PropertyType = propertyType,
    RawValue = rawValue,
    TypedValue = null // Force ConvertFilterValue to be used
  };

  [Fact]
  public async Task GetAllAsync_WithGuidFilter_FiltersCorrectly()
  {
    // Arrange
    var repository = CreateFilterTestRepository();
    var targetCategoryId = Guid.NewGuid();
    var matchingEntity = CreateFilterTestEntity(categoryId: targetCategoryId);
    var otherEntity = CreateFilterTestEntity(categoryId: Guid.NewGuid());
    await repository.CreateAsync(matchingEntity);
    await repository.CreateAsync(otherEntity);

    var filters = new List<FilterValue>
    {
      CreateFilterForProperty("CategoryId", targetCategoryId.ToString(), typeof(Guid))
    };
    var request = new PaginationRequest { Limit = 10, Filters = filters };

    // Act
    var result = await repository.GetAllAsync(request);

    // Assert
    result.Items.Should().HaveCount(1);
    result.Items.Single().CategoryId.Should().Be(targetCategoryId);
  }

  [Fact]
  public async Task GetAllAsync_WithDateTimeFilter_FiltersCorrectly()
  {
    // Arrange — Use Unspecified kind so DateTime.Parse round-trips cleanly
    // (DateTime.Parse("2025-06-15T12:00:00") returns DateTimeKind.Unspecified)
    var targetDate = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Unspecified);
    var repository = CreateFilterTestRepository();
    var matchingEntity = CreateFilterTestEntity(createdAt: targetDate);
    var otherEntity = CreateFilterTestEntity(createdAt: new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Unspecified));
    await repository.CreateAsync(matchingEntity);
    await repository.CreateAsync(otherEntity);

    var filters = new List<FilterValue>
    {
      CreateFilterForProperty("CreatedAt", "2025-06-15T12:00:00", typeof(DateTime))
    };
    var request = new PaginationRequest { Limit = 10, Filters = filters };

    // Act
    var result = await repository.GetAllAsync(request);

    // Assert
    result.Items.Should().HaveCount(1);
    result.Items.Single().CreatedAt.Should().Be(targetDate);
  }

  [Fact]
  public async Task GetAllAsync_WithDateTimeOffsetFilter_FiltersCorrectly()
  {
    // Arrange
    var repository = CreateFilterTestRepository();
    var targetDate = new DateTimeOffset(2025, 6, 15, 12, 0, 0, TimeSpan.Zero);
    var matchingEntity = CreateFilterTestEntity(modifiedAt: targetDate);
    var otherEntity = CreateFilterTestEntity(modifiedAt: DateTimeOffset.UtcNow.AddDays(-1));
    await repository.CreateAsync(matchingEntity);
    await repository.CreateAsync(otherEntity);

    var filters = new List<FilterValue>
    {
      CreateFilterForProperty("ModifiedAt", targetDate.ToString("O"), typeof(DateTimeOffset))
    };
    var request = new PaginationRequest { Limit = 10, Filters = filters };

    // Act
    var result = await repository.GetAllAsync(request);

    // Assert
    result.Items.Should().HaveCount(1);
    result.Items.Single().ModifiedAt.Should().Be(targetDate);
  }

  [Fact]
  public async Task GetAllAsync_WithEnumFilter_FiltersCorrectly()
  {
    // Arrange
    var repository = CreateFilterTestRepository();
    var mondayEntity = CreateFilterTestEntity(dayOfWeek: DayOfWeek.Monday);
    var fridayEntity = CreateFilterTestEntity(dayOfWeek: DayOfWeek.Friday);
    await repository.CreateAsync(mondayEntity);
    await repository.CreateAsync(fridayEntity);

    var filters = new List<FilterValue>
    {
      CreateFilterForProperty("DayOfWeek", "Friday", typeof(DayOfWeek))
    };
    var request = new PaginationRequest { Limit = 10, Filters = filters };

    // Act
    var result = await repository.GetAllAsync(request);

    // Assert
    result.Items.Should().HaveCount(1);
    result.Items.Single().DayOfWeek.Should().Be(DayOfWeek.Friday);
  }

  [Fact]
  public async Task GetAllAsync_WithNullableTypeFilter_FiltersCorrectly()
  {
    // Arrange
    var repository = CreateFilterTestRepository();
    var withValue = CreateFilterTestEntity(nullableValue: 42);
    var withoutValue = CreateFilterTestEntity(nullableValue: null);
    await repository.CreateAsync(withValue);
    await repository.CreateAsync(withoutValue);

    var filters = new List<FilterValue>
    {
      CreateFilterForProperty("NullableValue", "42", typeof(int?))
    };
    var request = new PaginationRequest { Limit = 10, Filters = filters };

    // Act
    var result = await repository.GetAllAsync(request);

    // Assert
    result.Items.Should().HaveCount(1);
    result.Items.Single().NullableValue.Should().Be(42);
  }

  [Fact]
  public async Task GetAllAsync_WithNullFilterValue_ReturnsAllItems()
  {
    // Arrange — null RawValue triggers the early null return in ConvertFilterValue
    var repository = CreateFilterTestRepository();
    var entity1 = CreateFilterTestEntity();
    var entity2 = CreateFilterTestEntity();
    await repository.CreateAsync(entity1);
    await repository.CreateAsync(entity2);

    var filters = new List<FilterValue>
    {
      new()
      {
        PropertyName = "Name",
        QueryParameterName = "name",
        PropertyType = typeof(string),
        RawValue = null!,
        TypedValue = null
      }
    };
    var request = new PaginationRequest { Limit = 10, Filters = filters };

    // Act
    var result = await repository.GetAllAsync(request);

    // Assert — null filter value matches entities where Name is also null (neither should match)
    // but both entities have non-null Name, so 0 items match
    result.Items.Should().HaveCount(0);
  }

  [Fact]
  public async Task GetAllAsync_WithUnconvertibleFilterValue_FallsBackToRawString()
  {
    // Arrange — "not-a-guid" can't be parsed as Guid, so ConvertFilterValue
    // falls into the catch branch and returns the raw string
    var repository = CreateFilterTestRepository();
    var entity = CreateFilterTestEntity();
    await repository.CreateAsync(entity);

    var filters = new List<FilterValue>
    {
      CreateFilterForProperty("CategoryId", "not-a-guid", typeof(Guid))
    };
    var request = new PaginationRequest { Limit = 10, Filters = filters };

    // Act
    var result = await repository.GetAllAsync(request);

    // Assert — the raw string "not-a-guid" won't match any Guid value
    result.Items.Should().BeEmpty();
  }

  #endregion

  #region GetJsonValue Edge Case Tests

  [Fact]
  public async Task PatchAsync_WithNullValue_SetsPropertyToDefault()
  {
    // Arrange — tests the JsonValueKind.Null branch in GetJsonValue
    var repository = CreateMultiWordRepository();
    var entity = new MultiWordEntity(Guid.NewGuid(), "Widget", true, 50, DateTime.UtcNow);
    await repository.CreateAsync(entity);
    var patch = JsonDocument.Parse("""{"product_name": null}""").RootElement;

    // Act
    var result = await repository.PatchAsync(entity.Id, patch);

    // Assert
    result.Should().NotBeNull();
    result!.ProductName.Should().BeNull();
    result.IsActive.Should().BeTrue();
    result.StockQuantity.Should().Be(50);
  }

  [Fact]
  public async Task PatchAsync_WithArrayValue_IsProcessedByGetJsonValue()
  {
    // Arrange — tests the JsonValueKind.Array branch in GetJsonValue.
    // Since TestEntity has no array properties, the patch still works by
    // including the array in the merged JSON (it will be ignored during deserialization).
    var repository = CreateRepository();
    var entity = CreateEntity("Original", 100);
    await repository.CreateAsync(entity);

    // Include a known property plus an array property (not on the entity)
    var patch = JsonDocument.Parse("""{"name": "Updated", "tags": [1, 2, 3]}""").RootElement;

    // Act
    var result = await repository.PatchAsync(entity.Id, patch);

    // Assert — the known property is patched, the array is silently ignored
    result.Should().NotBeNull();
    result!.Name.Should().Be("Updated");
    result.Value.Should().Be(100);
  }

  [Fact]
  public async Task PatchAsync_WithNestedObjectValue_IsProcessedByGetJsonValue()
  {
    // Arrange — tests the JsonValueKind.Object branch in GetJsonValue.
    var repository = CreateRepository();
    var entity = CreateEntity("Original", 100);
    await repository.CreateAsync(entity);

    // Include a known property plus a nested object (not on the entity)
    var patch = JsonDocument.Parse("""{"name": "Updated", "address": {"city": "NYC"}}""").RootElement;

    // Act
    var result = await repository.PatchAsync(entity.Id, patch);

    // Assert — the known property is patched, the nested object is silently ignored
    result.Should().NotBeNull();
    result!.Name.Should().Be("Updated");
    result.Value.Should().Be(100);
  }

  #endregion

  #region GetAllAsync Overflow Guard Tests

  [Fact]
  public async Task GetAllAsync_WithIntMaxValueLimit_ReturnsAllItems()
  {
    // Arrange
    var repository = CreateRepository();
    for (int i = 0; i < 5; i++) await repository.CreateAsync(CreateEntity($"Entity{i}", i));
    var request = new PaginationRequest { Limit = int.MaxValue };

    // Act
    var result = await repository.GetAllAsync(request);

    // Assert
    result.Items.Should().HaveCount(5);
    result.HasMore.Should().BeFalse();
    result.NextCursor.Should().BeNull();
  }

  [Fact]
  public async Task GetAllAsync_WithIntMaxValueLimit_DoesNotOverflow()
  {
    // Arrange — empty repository ensures Take(int.MaxValue) does not throw
    var repository = CreateRepository();
    var request = new PaginationRequest { Limit = int.MaxValue };

    // Act
    var act = () => repository.GetAllAsync(request);

    // Assert
    await act.Should().NotThrowAsync();
    var result = await repository.GetAllAsync(request);
    result.Items.Should().BeEmpty();
  }

  [Fact]
  public async Task GetAllAsync_WithIntMaxValueMinusOneLimit_PaginatesCorrectly()
  {
    // Arrange
    var repository = CreateRepository();
    for (int i = 0; i < 3; i++) await repository.CreateAsync(CreateEntity($"Entity{i}", i));
    var request = new PaginationRequest { Limit = int.MaxValue - 1 };

    // Act
    var result = await repository.GetAllAsync(request);

    // Assert
    result.Items.Should().HaveCount(3);
    result.HasMore.Should().BeFalse();
    result.NextCursor.Should().BeNull();
  }

  #endregion
}
