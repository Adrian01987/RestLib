using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using RestLib.EntityFrameworkCore.Tests.Fakes;
using Xunit;

namespace RestLib.EntityFrameworkCore.Tests;

/// <summary>
/// Integration tests for sort field translation in the EF Core repository.
/// </summary>
[Trait("Category", "Story6.1.1")]
[Trait("Type", "Integration")]
public class EfCoreSortTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private TestDbContext _dbContext = null!;

    public async Task InitializeAsync()
    {
        (_host, _client, _dbContext) = await new EfCoreTestHostBuilder<ProductEntity, Guid>("/api/products")
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.AllowSorting(
                    p => p.UnitPrice,
                    p => p.ProductName,
                    p => p.StockQuantity,
                    p => p.CreatedAt);
                config.AllowFiltering(p => p.IsActive);
            })
            .BuildAsync();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    [Fact]
    public async Task GetAll_SingleFieldAscending_ReturnsSortedResults()
    {
        // Arrange
        await SeedProductsAsync(
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Product C", UnitPrice = 30m, StockQuantity = 3, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Product A", UnitPrice = 10m, StockQuantity = 1, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Product B", UnitPrice = 20m, StockQuantity = 2, CreatedAt = DateTime.UtcNow, IsActive = true });

        // Act
        var response = await _client.GetAsync("/api/products?sort=unit_price:asc");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(3);

        var prices = Enumerable.Range(0, items.GetArrayLength())
            .Select(i => items[i].GetProperty("unit_price").GetDecimal())
            .ToList();

        prices.Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task GetAll_SingleFieldDescending_ReturnsSortedResults()
    {
        // Arrange
        await SeedProductsAsync(
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Product A", UnitPrice = 10m, StockQuantity = 1, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Product B", UnitPrice = 20m, StockQuantity = 2, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Product C", UnitPrice = 30m, StockQuantity = 3, CreatedAt = DateTime.UtcNow, IsActive = true });

        // Act
        var response = await _client.GetAsync("/api/products?sort=unit_price:desc");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(3);

        var prices = Enumerable.Range(0, items.GetArrayLength())
            .Select(i => items[i].GetProperty("unit_price").GetDecimal())
            .ToList();

        prices.Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task GetAll_MultiFieldSort_ReturnsSortedResults()
    {
        // Arrange
        await SeedProductsAsync(
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Banana", UnitPrice = 20m, StockQuantity = 10, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Apple", UnitPrice = 10m, StockQuantity = 10, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Cherry", UnitPrice = 30m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Date", UnitPrice = 15m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true });

        // Act
        var response = await _client.GetAsync("/api/products?sort=stock_quantity:asc,product_name:asc");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(4);

        var names = Enumerable.Range(0, items.GetArrayLength())
            .Select(i => items[i].GetProperty("product_name").GetString()!)
            .ToList();

        names.Should().Equal("Cherry", "Date", "Apple", "Banana");
    }

    [Fact]
    public async Task GetAll_NoSortParameter_OrdersByKey()
    {
        // Arrange
        var id1 = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var id2 = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var id3 = Guid.Parse("00000000-0000-0000-0000-000000000003");

        await SeedProductsAsync(
            new ProductEntity { Id = id3, ProductName = "Product C", UnitPrice = 30m, StockQuantity = 3, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = id1, ProductName = "Product A", UnitPrice = 10m, StockQuantity = 1, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = id2, ProductName = "Product B", UnitPrice = 20m, StockQuantity = 2, CreatedAt = DateTime.UtcNow, IsActive = true });

        // Act
        var response = await _client.GetAsync("/api/products");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(3);

        var ids = Enumerable.Range(0, items.GetArrayLength())
            .Select(i => Guid.Parse(items[i].GetProperty("id").GetString()!))
            .ToList();

        ids.Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task GetAll_SortCombinedWithFilter_ReturnsFilteredAndSorted()
    {
        // Arrange
        await SeedProductsAsync(
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Active Cheap", UnitPrice = 5m, StockQuantity = 1, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Inactive Expensive", UnitPrice = 50m, StockQuantity = 2, CreatedAt = DateTime.UtcNow, IsActive = false },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Active Expensive", UnitPrice = 40m, StockQuantity = 3, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Active Mid", UnitPrice = 20m, StockQuantity = 4, CreatedAt = DateTime.UtcNow, IsActive = true });

        // Act
        var response = await _client.GetAsync("/api/products?is_active=true&sort=unit_price:desc");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(3);

        var prices = Enumerable.Range(0, items.GetArrayLength())
            .Select(i => items[i].GetProperty("unit_price").GetDecimal())
            .ToList();

        prices.Should().BeInDescendingOrder();
        prices.Should().Equal(40m, 20m, 5m);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task SeedProductsAsync(params ProductEntity[] products)
    {
        _dbContext.Products.AddRange(products);
        await _dbContext.SaveChangesAsync();
    }
}
