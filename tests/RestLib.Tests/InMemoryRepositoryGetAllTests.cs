using System.Text.Json;
using FluentAssertions;
using RestLib.Filtering;
using RestLib.InMemory;
using RestLib.Pagination;
using Xunit;

namespace RestLib.Tests;

public partial class InMemoryRepositoryTests
{
    #region GetAllAsync Tests

    [Fact]
    public async Task GetAllAsync_WithEmptyRepository_ReturnsEmptyResult()
    {
        // Arrange
        var repository = CreateRepository();
        var request = new PaginationRequest { Limit = 10 };

        // Act
        var result = await repository.GetAllAsync(request);

        // Assert
        result.Items.Should().BeEmpty();
        result.HasMore.Should().BeFalse();
        result.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task GetAllAsync_WithEntities_ReturnsAllEntities()
    {
        // Arrange
        var repository = CreateRepository();
        var entities = new[] { CreateEntity("Entity1", 1), CreateEntity("Entity2", 2), CreateEntity("Entity3", 3) };
        foreach (var entity in entities) await repository.CreateAsync(entity);
        var request = new PaginationRequest { Limit = 10 };

        // Act
        var result = await repository.GetAllAsync(request);

        // Assert
        result.Items.Should().HaveCount(3);
        result.HasMore.Should().BeFalse();
        result.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task GetAllAsync_WithPagination_ReturnsFirstPage()
    {
        // Arrange
        var repository = CreateRepository();
        for (int i = 0; i < 10; i++) await repository.CreateAsync(CreateEntity($"Entity{i}", i));
        var request = new PaginationRequest { Limit = 5 };

        // Act
        var result = await repository.GetAllAsync(request);

        // Assert
        result.Items.Should().HaveCount(5);
        result.HasMore.Should().BeTrue();
        result.NextCursor.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetAllAsync_WithCursor_ReturnsNextPage()
    {
        // Arrange
        var repository = CreateRepository();
        for (int i = 0; i < 10; i++) await repository.CreateAsync(CreateEntity($"Entity{i}", i));
        var firstPageRequest = new PaginationRequest { Limit = 5 };
        var firstPage = await repository.GetAllAsync(firstPageRequest);

        // Act
        var secondPageRequest = new PaginationRequest { Limit = 5, Cursor = firstPage.NextCursor };
        var secondPage = await repository.GetAllAsync(secondPageRequest);

        // Assert
        secondPage.Items.Should().HaveCount(5);
        secondPage.HasMore.Should().BeFalse();
        secondPage.NextCursor.Should().BeNull();
        firstPage.Items.Should().NotIntersectWith(secondPage.Items);
    }

    [Fact]
    public async Task GetAllAsync_WithExactLimit_HasMoreIsFalse()
    {
        // Arrange
        var repository = CreateRepository();
        for (int i = 0; i < 5; i++) await repository.CreateAsync(CreateEntity($"Entity{i}", i));
        var request = new PaginationRequest { Limit = 5 };

        // Act
        var result = await repository.GetAllAsync(request);

        // Assert
        result.Items.Should().HaveCount(5);
        result.HasMore.Should().BeFalse();
        result.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task GetAllAsync_ConsistentOrderingAcrossPages()
    {
        // Arrange
        var repository = CreateRepository();
        var allEntities = new List<TestEntity>();
        for (int i = 0; i < 15; i++)
        {
            var entity = CreateEntity($"Entity{i}", i);
            await repository.CreateAsync(entity);
            allEntities.Add(entity);
        }

        // Act
        var retrievedEntities = new List<TestEntity>();
        string? cursor = null;
        do
        {
            var request = new PaginationRequest { Limit = 5, Cursor = cursor };
            var result = await repository.GetAllAsync(request);
            retrievedEntities.AddRange(result.Items);
            cursor = result.NextCursor;
        } while (cursor != null);

        // Assert
        retrievedEntities.Should().HaveCount(15);
        retrievedEntities.Should().OnlyHaveUniqueItems();
        retrievedEntities.Should().BeEquivalentTo(allEntities);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task GetAllAsync_WithInvalidCursor_StartsFromBeginning()
    {
        // Arrange
        var repository = CreateRepository();
        for (int i = 0; i < 10; i++) await repository.CreateAsync(CreateEntity($"Entity{i}", i));
        var request = new PaginationRequest { Limit = 5, Cursor = "invalid-cursor-that-wont-decode" };

        // Act
        var result = await repository.GetAllAsync(request);

        // Assert
        result.Items.Should().HaveCount(5);
        result.HasMore.Should().BeTrue();
    }

    [Fact]
    public async Task GetAllAsync_WithLimitGreaterThanTotal_ReturnsAll()
    {
        // Arrange
        var repository = CreateRepository();
        for (int i = 0; i < 5; i++) await repository.CreateAsync(CreateEntity($"Entity{i}", i));
        var request = new PaginationRequest { Limit = 100 };

        // Act
        var result = await repository.GetAllAsync(request);

        // Assert
        result.Items.Should().HaveCount(5);
        result.HasMore.Should().BeFalse();
        result.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task GetAllAsync_WithLimitOfOne_PaginatesCorrectly()
    {
        // Arrange
        var repository = CreateRepository();
        for (int i = 0; i < 3; i++) await repository.CreateAsync(CreateEntity($"Entity{i}", i));

        // Act
        var allItems = new List<TestEntity>();
        string? cursor = null;
        do
        {
            var request = new PaginationRequest { Limit = 1, Cursor = cursor };
            var result = await repository.GetAllAsync(request);
            allItems.AddRange(result.Items);
            cursor = result.NextCursor;
        } while (cursor != null);

        // Assert
        allItems.Should().HaveCount(3);
        allItems.Should().OnlyHaveUniqueItems();
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
