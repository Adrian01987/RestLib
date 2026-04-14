using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using RestLib.EntityFrameworkCore.Tests.Fakes;
using RestLib.Filtering;
using Xunit;

namespace RestLib.EntityFrameworkCore.Tests;

/// <summary>
/// Integration tests for the In filter operator in the EF Core repository.
/// </summary>
[Trait("Category", "Story5.3.1")]
[Trait("Type", "Integration")]
public class EfCoreInFilterTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private TestDbContext _dbContext = null!;

    /// <summary>
    /// Sets up the test host with In filtering enabled.
    /// </summary>
    public async Task InitializeAsync()
    {
        (_host, _client, _dbContext) = await new EfCoreTestHostBuilder<ProductEntity, Guid>("/api/products")
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.AllowFiltering(p => p.Status, FilterOperators.All);
                config.AllowFiltering(p => p.Id, FilterOperators.All);
                config.AllowFiltering(p => p.StockQuantity, FilterOperators.All);
                config.AllowFiltering(p => p.ProductName, FilterOperators.All);
                config.AllowFiltering(p => p.UnitPrice, FilterOperators.Comparison);
            })
            .BuildAsync();
    }

    /// <summary>
    /// Cleans up the test host and HTTP client.
    /// </summary>
    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    [Fact]
    public async Task GetAll_InFilter_MultipleStringValues_ReturnsMatchingEntities()
    {
        // Arrange
        await SeedProductsAsync(
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Product A", UnitPrice = 10m, StockQuantity = 1, CreatedAt = DateTime.UtcNow, IsActive = true, Status = "Active" },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Product B", UnitPrice = 20m, StockQuantity = 2, CreatedAt = DateTime.UtcNow, IsActive = true, Status = "Draft" },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Product C", UnitPrice = 30m, StockQuantity = 3, CreatedAt = DateTime.UtcNow, IsActive = true, Status = "Archived" },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Product D", UnitPrice = 40m, StockQuantity = 4, CreatedAt = DateTime.UtcNow, IsActive = true, Status = "Active" });

        // Act
        var response = await _client.GetAsync("/api/products?status[in]=Active,Draft");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        json.GetProperty("items").GetArrayLength().Should().Be(3);
    }

    [Fact]
    public async Task GetAll_InFilter_MultipleGuidValues_ReturnsMatchingEntities()
    {
        // Arrange
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var id3 = Guid.NewGuid();

        await SeedProductsAsync(
            new ProductEntity { Id = id1, ProductName = "Product 1", UnitPrice = 10m, StockQuantity = 1, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = id2, ProductName = "Product 2", UnitPrice = 20m, StockQuantity = 2, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = id3, ProductName = "Product 3", UnitPrice = 30m, StockQuantity = 3, CreatedAt = DateTime.UtcNow, IsActive = true });

        // Act
        var response = await _client.GetAsync($"/api/products?id[in]={id1},{id3}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        json.GetProperty("items").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task GetAll_InFilter_SingleValue_ReturnsMatchingEntity()
    {
        // Arrange
        await SeedProductsAsync(
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Product A", UnitPrice = 10m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Product B", UnitPrice = 20m, StockQuantity = 10, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Product C", UnitPrice = 30m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true });

        // Act
        var response = await _client.GetAsync("/api/products?stock_quantity[in]=5");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        json.GetProperty("items").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task GetAll_InFilter_CombinedWithOtherOperators_ReturnsIntersection()
    {
        // Arrange
        await SeedProductsAsync(
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Cheap Widget", UnitPrice = 5m, StockQuantity = 1, CreatedAt = DateTime.UtcNow, IsActive = true, Status = "Active" },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Expensive Widget", UnitPrice = 50m, StockQuantity = 2, CreatedAt = DateTime.UtcNow, IsActive = true, Status = "Active" },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Cheap Gadget", UnitPrice = 5m, StockQuantity = 3, CreatedAt = DateTime.UtcNow, IsActive = true, Status = "Draft" },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Expensive Gadget", UnitPrice = 50m, StockQuantity = 4, CreatedAt = DateTime.UtcNow, IsActive = true, Status = "Archived" });

        // Act
        var response = await _client.GetAsync("/api/products?status[in]=Active,Draft&unit_price[gte]=10");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(1);
        items[0].GetProperty("product_name").GetString().Should().Be("Expensive Widget");
    }

    [Fact]
    public async Task GetAll_InFilter_CountReflectsFilteredDataset()
    {
        // Arrange
        await SeedProductsAsync(
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Product A", UnitPrice = 10m, StockQuantity = 1, CreatedAt = DateTime.UtcNow, IsActive = true, Status = "Active" },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Product B", UnitPrice = 20m, StockQuantity = 2, CreatedAt = DateTime.UtcNow, IsActive = true, Status = "Draft" },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Product C", UnitPrice = 30m, StockQuantity = 3, CreatedAt = DateTime.UtcNow, IsActive = true, Status = "Archived" });

        // Act
        var response = await _client.GetAsync("/api/products?status[in]=Active,Draft");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        json.GetProperty("items").GetArrayLength().Should().Be(2);
        json.TryGetProperty("total_count", out var totalCount).Should().BeTrue();
        totalCount.GetInt64().Should().Be(2);
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
