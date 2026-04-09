using System.Text.Json;
using FluentAssertions;
using RestLib.Filtering;
using RestLib.InMemory;
using RestLib.Pagination;
using Xunit;

namespace RestLib.Tests;

[Trait("Type", "Unit")]
[Trait("Feature", "Repository")]
public partial class InMemoryRepositoryTests
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
        // Act
        var act = () => new InMemoryRepository<TestEntity, Guid>(null!, Guid.NewGuid);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("keySelector");
    }

    [Fact]
    public void Constructor_WithNullKeyGenerator_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new InMemoryRepository<TestEntity, Guid>(e => e.Id, null!);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("keyGenerator");
    }

    [Fact]
    public void Constructor_WithValidParameters_CreatesEmptyRepository()
    {
        // Act
        var repository = CreateRepository();

        // Assert
        repository.Count.Should().Be(0);
    }

    #endregion

    #region GetByIdAsync Tests

    [Fact]
    public async Task GetByIdAsync_WithExistingEntity_ReturnsEntity()
    {
        // Arrange
        var repository = CreateRepository();
        var entity = CreateEntity();
        await repository.CreateAsync(entity);

        // Act
        var result = await repository.GetByIdAsync(entity.Id);

        // Assert
        result.Should().NotBeNull();
        result.Should().Be(entity);
    }

    [Fact]
    public async Task GetByIdAsync_WithNonExistingId_ReturnsNull()
    {
        // Arrange
        var repository = CreateRepository();

        // Act
        var result = await repository.GetByIdAsync(Guid.NewGuid());

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_WithEmptyRepository_ReturnsNull()
    {
        // Arrange
        var repository = CreateRepository();

        // Act
        var result = await repository.GetByIdAsync(Guid.NewGuid());

        // Assert
        result.Should().BeNull();
    }

    #endregion
}
