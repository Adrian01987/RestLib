using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using RestLib.Filtering;
using RestLib.InMemory;
using RestLib.Tests.Fakes;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Tests for ICountableRepository integration: the optional total_count
/// field in collection responses.
/// </summary>
[Trait("Category", "Story20")]
public class CountableRepositoryTests : IDisposable
{
    private readonly IHost _host;
    private readonly HttpClient _client;
    private readonly InMemoryRepository<ProductEntity, Guid> _repository;

    public CountableRepositoryTests()
    {
        _repository = new InMemoryRepository<ProductEntity, Guid>(e => e.Id, Guid.NewGuid);

        (_host, _client) = new TestHostBuilder<ProductEntity, Guid>(_repository, "/api/products")
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.AllowFiltering(p => p.IsActive, p => p.UnitPrice);
                config.AllowFieldSelection(p => p.Id, p => p.ProductName, p => p.UnitPrice);
            })
            .Build();
    }

    [Fact]
    public async Task GetAll_WithCountableRepository_ReturnsTotalCount()
    {
        // Arrange
        SeedProducts(5);

        // Act
        var response = await _client.GetAsync("/api/products");
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        doc.RootElement.TryGetProperty("total_count", out var totalCount).Should().BeTrue();
        totalCount.GetInt64().Should().Be(5);
    }

    [Fact]
    public async Task GetAll_WithCountableRepository_TotalCountReflectsAllEntities()
    {
        // Arrange — seed more items than one page
        SeedProducts(25);

        // Act — request only 5 items per page
        var response = await _client.GetAsync("/api/products?limit=5");
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        // Assert — total_count should be 25 regardless of page size
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        doc.RootElement.GetProperty("items").GetArrayLength().Should().Be(5);
        doc.RootElement.GetProperty("total_count").GetInt64().Should().Be(25);
    }

    [Fact]
    public async Task GetAll_WithFilters_TotalCountReflectsFilteredCount()
    {
        // Arrange
        _repository.Seed(
        [
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Active 1", UnitPrice = 10m, StockQuantity = 1, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Active 2", UnitPrice = 20m, StockQuantity = 2, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Inactive", UnitPrice = 30m, StockQuantity = 3, CreatedAt = DateTime.UtcNow, IsActive = false }
        ]);

        // Act — filter to active only
        var response = await _client.GetAsync("/api/products?is_active=true");
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        doc.RootElement.GetProperty("items").GetArrayLength().Should().Be(2);
        doc.RootElement.GetProperty("total_count").GetInt64().Should().Be(2);
    }

    [Fact]
    public async Task GetAll_EmptyCollection_TotalCountIsZero()
    {
        // Act
        var response = await _client.GetAsync("/api/products");
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        doc.RootElement.GetProperty("items").GetArrayLength().Should().Be(0);
        doc.RootElement.GetProperty("total_count").GetInt64().Should().Be(0);
    }

    [Fact]
    public async Task GetAll_WithFieldSelection_IncludesTotalCount()
    {
        // Arrange
        SeedProducts(3);

        // Act
        var response = await _client.GetAsync("/api/products?fields=product_name,unit_price");
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        doc.RootElement.TryGetProperty("total_count", out var totalCount).Should().BeTrue();
        totalCount.GetInt64().Should().Be(3);
    }

    public void Dispose()
    {
        _client.Dispose();
        _host.Dispose();
    }

    private void SeedProducts(int count)
    {
        var products = new List<ProductEntity>();
        for (int i = 0; i < count; i++)
        {
            products.Add(new ProductEntity
            {
                Id = Guid.NewGuid(),
                ProductName = $"Product {i + 1}",
                UnitPrice = 10m * (i + 1),
                StockQuantity = i + 1,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            });
        }

        _repository.Seed(products);
    }
}

/// <summary>
/// Tests that total_count is omitted when the repository does not implement
/// ICountableRepository.
/// </summary>
[Trait("Category", "Story20")]
public class NonCountableRepositoryTests : IDisposable
{
    private readonly IHost _host;
    private readonly HttpClient _client;
    private readonly TestEntityRepository _repository;

    public NonCountableRepositoryTests()
    {
        _repository = new TestEntityRepository();

        (_host, _client) = new TestHostBuilder<TestEntity, Guid>(_repository, "/api/items")
            .WithEndpoint(config => config.AllowAnonymous())
            .Build();
    }

    [Fact]
    public async Task GetAll_WithNonCountableRepository_OmitsTotalCount()
    {
        // Arrange
        _repository.Seed(
            new TestEntity { Id = Guid.NewGuid(), Name = "Item 1", Price = 10m },
            new TestEntity { Id = Guid.NewGuid(), Name = "Item 2", Price = 20m });

        // Act
        var response = await _client.GetAsync("/api/items");
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        doc.RootElement.TryGetProperty("total_count", out _).Should().BeFalse(
            "total_count should be omitted when repository does not implement ICountableRepository");
    }

    public void Dispose()
    {
        _client.Dispose();
        _host.Dispose();
    }
}

/// <summary>
/// Unit tests for InMemoryRepository.CountAsync.
/// </summary>
[Trait("Category", "Story20")]
public class InMemoryCountAsyncTests
{
    [Fact]
    public async Task CountAsync_EmptyRepository_ReturnsZero()
    {
        // Arrange
        var repository = new InMemoryRepository<ProductEntity, Guid>(e => e.Id, Guid.NewGuid);

        // Act
        var count = await repository.CountAsync([]);

        // Assert
        count.Should().Be(0);
    }

    [Fact]
    public async Task CountAsync_NoFilters_ReturnsAllEntities()
    {
        // Arrange
        var repository = new InMemoryRepository<ProductEntity, Guid>(e => e.Id, Guid.NewGuid);
        for (int i = 0; i < 10; i++)
        {
            await repository.CreateAsync(new ProductEntity
            {
                ProductName = $"Product {i}",
                UnitPrice = 10m,
                StockQuantity = 1,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            });
        }

        // Act
        var count = await repository.CountAsync([]);

        // Assert
        count.Should().Be(10);
    }

    [Fact]
    public async Task CountAsync_WithFilters_ReturnsFilteredCount()
    {
        // Arrange
        var repository = new InMemoryRepository<ProductEntity, Guid>(e => e.Id, Guid.NewGuid);
        await repository.CreateAsync(new ProductEntity
        {
            ProductName = "Active",
            UnitPrice = 10m,
            StockQuantity = 1,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        });
        await repository.CreateAsync(new ProductEntity
        {
            ProductName = "Inactive",
            UnitPrice = 20m,
            StockQuantity = 2,
            CreatedAt = DateTime.UtcNow,
            IsActive = false
        });
        await repository.CreateAsync(new ProductEntity
        {
            ProductName = "Also Active",
            UnitPrice = 30m,
            StockQuantity = 3,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        });

        var filters = new List<FilterValue>
        {
            new()
            {
                PropertyName = "IsActive",
                QueryParameterName = "is_active",
                PropertyType = typeof(bool),
                RawValue = "true",
                TypedValue = true
            }
        };

        // Act
        var count = await repository.CountAsync(filters);

        // Assert
        count.Should().Be(2);
    }
}
