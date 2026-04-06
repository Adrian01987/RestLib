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
