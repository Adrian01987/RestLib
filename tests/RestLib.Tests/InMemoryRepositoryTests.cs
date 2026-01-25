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
}
