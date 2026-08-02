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
        // Arrange
        var repository = CreateRepository();
        var entity = CreateEntity();

        // Act
        var result = await repository.CreateAsync(entity);

        // Assert
        result.Should().Be(entity);
        repository.Count.Should().Be(1);
    }

    [Fact]
    public async Task CreateAsync_WithNullEntity_ThrowsArgumentNullException()
    {
        // Arrange
        var repository = CreateRepository();

        // Act
        var act = () => repository.CreateAsync(null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task CreateAsync_WithDuplicateKey_ThrowsInvalidOperationException()
    {
        // Arrange
        var repository = CreateRepository();
        var entity = CreateEntity();
        await repository.CreateAsync(entity);

        // Act
        var act = () => repository.CreateAsync(entity);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage($"*{entity.Id}*");
    }

    [Fact]
    public async Task CreateAsync_WithDefaultKey_GeneratesNewKey()
    {
        // Arrange
        var generatedId = Guid.NewGuid();
        var repository = new InMemoryRepository<TestEntity, Guid>(e => e.Id, () => generatedId);
        var entity = new TestEntity(Guid.Empty, "Test", 100, DateTime.UtcNow);

        // Act
        var result = await repository.CreateAsync(entity);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(generatedId);
        repository.Count.Should().Be(1);
    }

    [Fact]
    public async Task CreateAsync_WithNonStandardKeyName_GeneratesNewKey()
    {
        // Arrange — entity whose key property is named "Sku", not "Id"
        var generatedSku = Guid.NewGuid();
        var repository = new InMemoryRepository<SkuEntity, Guid>(e => e.Sku, () => generatedSku);
        var entity = new SkuEntity(Guid.Empty, "Widget");

        // Act
        var result = await repository.CreateAsync(entity);

        // Assert — key should have been detected and set despite non-standard name
        result.Should().NotBeNull();
        result.Sku.Should().Be(generatedSku);
        repository.Count.Should().Be(1);

        var retrieved = await repository.GetByIdAsync(generatedSku);
        retrieved.Should().NotBeNull();
        retrieved!.Name.Should().Be("Widget");
    }

    private record SkuEntity(Guid Sku, string Name);

    private sealed record MultiGuidEntity(Guid Id, Guid RelatedId, string Name);

    private sealed record AmbiguousKeyEntity(Guid PrimaryKey, Guid SecondaryKey, string Name);

    private readonly record struct CalculatedKey(Guid PartitionId, int Sequence);

    private sealed record CalculatedKeyEntity(Guid PartitionId, int Sequence, string Name);

    [Fact]
    public async Task CreateAsync_WithMultipleKeyProperties_InvokesGeneratorExactlyOnce()
    {
        // Arrange
        var generatedId = Guid.NewGuid();
        var generatorCalls = 0;
        var repository = new InMemoryRepository<MultiGuidEntity, Guid>(
            entity => entity.Id,
            () =>
            {
                generatorCalls++;
                return generatedId;
            });
        var entity = new MultiGuidEntity(Guid.Empty, Guid.Empty, "Widget");

        // Act
        var result = await repository.CreateAsync(entity);

        // Assert
        generatorCalls.Should().Be(1);
        result.Id.Should().Be(generatedId);
        result.RelatedId.Should().BeEmpty();
        (await repository.GetByIdAsync(generatedId)).Should().Be(result);
    }

    [Fact]
    public async Task CreateAsync_WithDifferentExplicitKeyAssigners_KeepsRepositoriesIndependent()
    {
        // Arrange
        var primaryKey = Guid.NewGuid();
        var secondaryKey = Guid.NewGuid();
        var primaryRepository = new InMemoryRepository<AmbiguousKeyEntity, Guid>(
            entity => entity.PrimaryKey,
            () => primaryKey,
            jsonOptions: null,
            keyAssigner: (entity, key) => entity with { PrimaryKey = key });
        var secondaryRepository = new InMemoryRepository<AmbiguousKeyEntity, Guid>(
            entity => entity.SecondaryKey,
            () => secondaryKey,
            jsonOptions: null,
            keyAssigner: (entity, key) => entity with { SecondaryKey = key });

        // Act
        var primaryResult = await primaryRepository.CreateAsync(
            new AmbiguousKeyEntity(Guid.Empty, Guid.Empty, "Primary"));
        var secondaryResult = await secondaryRepository.CreateAsync(
            new AmbiguousKeyEntity(Guid.Empty, Guid.Empty, "Secondary"));

        // Assert
        primaryResult.PrimaryKey.Should().Be(primaryKey);
        primaryResult.SecondaryKey.Should().BeEmpty();
        secondaryResult.PrimaryKey.Should().BeEmpty();
        secondaryResult.SecondaryKey.Should().Be(secondaryKey);
        (await primaryRepository.GetByIdAsync(primaryKey)).Should().Be(primaryResult);
        (await secondaryRepository.GetByIdAsync(secondaryKey)).Should().Be(secondaryResult);
    }

    [Fact]
    public async Task CreateAsync_WithUnassignableCalculatedKey_FailsBeforeInvokingGenerator()
    {
        // Arrange
        var generatorCalls = 0;
        var repository = new InMemoryRepository<CalculatedKeyEntity, CalculatedKey>(
            entity => new CalculatedKey(entity.PartitionId, entity.Sequence),
            () =>
            {
                generatorCalls++;
                return new CalculatedKey(Guid.NewGuid(), 1);
            });
        var entity = new CalculatedKeyEntity(Guid.Empty, 0, "Widget");

        // Act
        var act = () => repository.CreateAsync(entity);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*explicit key assigner*");
        generatorCalls.Should().Be(0);
        repository.Count.Should().Be(0);
    }

    [Fact]
    public async Task CreateAsync_WithCalculatedKeyAssigner_ReturnsConsistentEntityAndStorageKey()
    {
        // Arrange
        var generatedKey = new CalculatedKey(Guid.NewGuid(), 42);
        var repository = new InMemoryRepository<CalculatedKeyEntity, CalculatedKey>(
            entity => new CalculatedKey(entity.PartitionId, entity.Sequence),
            () => generatedKey,
            jsonOptions: null,
            keyAssigner: (entity, key) => entity with { PartitionId = key.PartitionId, Sequence = key.Sequence });
        var entity = new CalculatedKeyEntity(Guid.Empty, 0, "Widget");

        // Act
        var result = await repository.CreateAsync(entity);

        // Assert
        result.PartitionId.Should().Be(generatedKey.PartitionId);
        result.Sequence.Should().Be(generatedKey.Sequence);
        (await repository.GetByIdAsync(generatedKey)).Should().Be(result);
    }

    [Fact]
    public async Task CreateAsync_MultipleEntities_AllAdded()
    {
        // Arrange
        var repository = CreateRepository();
        var entities = Enumerable.Range(1, 100).Select(i => CreateEntity($"Entity{i}", i)).ToList();

        // Act
        foreach (var entity in entities) await repository.CreateAsync(entity);

        // Assert
        repository.Count.Should().Be(100);
    }

    #endregion

    #region UpdateAsync Tests

    [Fact]
    public async Task UpdateAsync_WithExistingEntity_UpdatesAndReturns()
    {
        // Arrange
        var repository = CreateRepository();
        var entity = CreateEntity("Original", 100);
        await repository.CreateAsync(entity);
        var updatedEntity = entity with { Name = "Updated", Value = 200 };

        // Act
        var result = await repository.UpdateAsync(entity.Id, updatedEntity);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("Updated");
        result.Value.Should().Be(200);
        var retrieved = await repository.GetByIdAsync(entity.Id);
        retrieved.Should().Be(updatedEntity);
    }

    [Fact]
    public async Task UpdateAsync_BodyKeyDiffersFromRouteKey_PreservesRouteIdentity()
    {
        // Arrange
        var repository = CreateRepository();
        var entity = CreateEntity("Original", 100);
        await repository.CreateAsync(entity);
        var bodyKey = Guid.NewGuid();
        var replacement = entity with { Id = bodyKey, Name = "Updated" };

        // Act
        var result = await repository.UpdateAsync(entity.Id, replacement);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(entity.Id);
        result.Name.Should().Be("Updated");
        (await repository.GetByIdAsync(entity.Id)).Should().BeEquivalentTo(result);
        (await repository.GetByIdAsync(bodyKey)).Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_WithNonExistingId_ReturnsNull()
    {
        // Arrange
        var repository = CreateRepository();
        var entity = CreateEntity();

        // Act
        var result = await repository.UpdateAsync(Guid.NewGuid(), entity);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_WithNullEntity_ThrowsArgumentNullException()
    {
        // Arrange
        var repository = CreateRepository();
        var entity = CreateEntity();
        await repository.CreateAsync(entity);

        // Act
        var act = () => repository.UpdateAsync(entity.Id, null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task UpdateAsync_DoesNotChangeCount()
    {
        // Arrange
        var repository = CreateRepository();
        var entity = CreateEntity();
        await repository.CreateAsync(entity);
        var updatedEntity = entity with { Name = "Updated" };

        // Act
        await repository.UpdateAsync(entity.Id, updatedEntity);

        // Assert
        repository.Count.Should().Be(1);
    }

    #endregion

    #region DeleteAsync Tests

    [Fact]
    public async Task DeleteAsync_WithExistingEntity_RemovesAndReturnsTrue()
    {
        // Arrange
        var repository = CreateRepository();
        var entity = CreateEntity();
        await repository.CreateAsync(entity);

        // Act
        var result = await repository.DeleteAsync(entity.Id);

        // Assert
        result.Should().BeTrue();
        repository.Count.Should().Be(0);
    }

    [Fact]
    public async Task DeleteAsync_WithNonExistingId_ReturnsFalse()
    {
        // Arrange
        var repository = CreateRepository();

        // Act
        var result = await repository.DeleteAsync(Guid.NewGuid());

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_RemovedEntityNoLongerRetrievable()
    {
        // Arrange
        var repository = CreateRepository();
        var entity = CreateEntity();
        await repository.CreateAsync(entity);
        await repository.DeleteAsync(entity.Id);

        // Act
        var result = await repository.GetByIdAsync(entity.Id);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region Seed and Clear Tests

    [Fact]
    public void Seed_WithEntities_AddsAllToRepository()
    {
        // Arrange
        var repository = CreateRepository();
        var entities = Enumerable.Range(1, 10).Select(i => CreateEntity($"Entity{i}", i)).ToList();

        // Act
        repository.Seed(entities);

        // Assert
        repository.Count.Should().Be(10);
    }

    [Fact]
    public void Seed_WithNullEnumerable_ThrowsArgumentNullException()
    {
        // Arrange
        var repository = CreateRepository();

        // Act
        var act = () => repository.Seed(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Clear_RemovesAllEntities()
    {
        // Arrange
        var repository = CreateRepository();
        for (int i = 0; i < 10; i++) await repository.CreateAsync(CreateEntity($"Entity{i}", i));

        // Act
        repository.Clear();

        // Assert
        repository.Count.Should().Be(0);
    }

    [Fact]
    public async Task Clear_ThenCreate_Works()
    {
        // Arrange
        var repository = CreateRepository();
        await repository.CreateAsync(CreateEntity("First", 1));
        repository.Clear();
        var entity = CreateEntity("Second", 2);

        // Act
        var result = await repository.CreateAsync(entity);

        // Assert
        result.Should().Be(entity);
        repository.Count.Should().Be(1);
    }

    #endregion
}
