using System.Text.Json;
using FluentAssertions;
using RestLib.Filtering;
using RestLib.InMemory;
using RestLib.Pagination;
using Xunit;

namespace RestLib.Tests;

public partial class InMemoryRepositoryTests
{
    #region PatchAsync Tests

    [Fact]
    public async Task PatchAsync_WithExistingEntity_PatchesFields()
    {
        // Arrange
        var repository = CreateRepository();
        var entity = CreateEntity("Original", 100);
        await repository.CreateAsync(entity);
        var patch = JsonDocument.Parse("""{"name": "Patched"}""").RootElement;

        // Act
        var result = await repository.PatchAsync(entity.Id, patch);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("Patched");
        result.Value.Should().Be(100);
    }

    [Fact]
    public async Task PatchAsync_WithNonExistingId_ReturnsNull()
    {
        // Arrange
        var repository = CreateRepository();
        var patch = JsonDocument.Parse("""{"name": "Patched"}""").RootElement;

        // Act
        var result = await repository.PatchAsync(Guid.NewGuid(), patch);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task PatchAsync_WithMultipleFields_PatchesAll()
    {
        // Arrange
        var repository = CreateRepository();
        var entity = CreateEntity("Original", 100);
        await repository.CreateAsync(entity);
        var patch = JsonDocument.Parse("""{"name": "Patched", "value": 999}""").RootElement;

        // Act
        var result = await repository.PatchAsync(entity.Id, patch);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("Patched");
        result.Value.Should().Be(999);
    }

    [Fact]
    public async Task PatchAsync_PreservesUnspecifiedFields()
    {
        // Arrange
        var repository = CreateRepository();
        var createdAt = DateTime.UtcNow.AddDays(-1);
        var entity = new TestEntity(Guid.NewGuid(), "Original", 100, createdAt);
        await repository.CreateAsync(entity);
        var patch = JsonDocument.Parse("""{"name": "Patched"}""").RootElement;

        // Act
        var result = await repository.PatchAsync(entity.Id, patch);

        // Assert
        result.Should().NotBeNull();
        result!.CreatedAt.Should().BeCloseTo(createdAt, TimeSpan.FromSeconds(1));
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
}
