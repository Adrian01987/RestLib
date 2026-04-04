using System.Text.Json;
using FluentAssertions;
using RestLib.Filtering;
using RestLib.InMemory;
using RestLib.Pagination;
using Xunit;

namespace RestLib.Tests;

public partial class InMemoryRepositoryTests
{
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
}
