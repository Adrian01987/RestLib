using System.Text.Json;
using FluentAssertions;
using RestLib.Filtering;
using RestLib.InMemory;
using RestLib.Pagination;
using Xunit;

namespace RestLib.Tests;

public partial class InMemoryRepositoryTests
{
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
  public async Task CreateManyAsync_WithDuplicateKey_ThrowsInvalidOperationException()
  {
    // Arrange
    var repository = CreateRepository();
    var entity = CreateEntity("Original", 100);
    await repository.CreateAsync(entity);
    var replacement = entity with { Name = "Replaced", Value = 999 };

    // Act
    var act = () => repository.CreateManyAsync(new List<TestEntity> { replacement });

    // Assert — consistent with CreateAsync, which throws on duplicate keys
    await act.Should().ThrowAsync<InvalidOperationException>()
        .WithMessage("*already exists*");
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
}
