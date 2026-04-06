using System.Text.Json;
using FluentAssertions;
using RestLib.Filtering;
using RestLib.InMemory;
using RestLib.Pagination;
using Xunit;

namespace RestLib.Tests;

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
}
