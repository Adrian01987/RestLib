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
/// Integration tests for comparison filter operators (Eq, Neq, Gt, Lt, Gte, Lte)
/// in the EF Core repository.
/// </summary>
[Trait("Category", "Story5.1.1")]
[Trait("Type", "Integration")]
public class EfCoreComparisonFilterTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private TestDbContext _dbContext = null!;

    /// <summary>
    /// Sets up the test host with filtering enabled.
    /// </summary>
    public async Task InitializeAsync()
    {
        (_host, _client, _dbContext) = await new EfCoreTestHostBuilder<ProductEntity, Guid>("/api/products")
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.AllowFiltering(p => p.Id);
                config.AllowFiltering(p => p.ProductName);
                config.AllowFiltering(p => p.UnitPrice, FilterOperators.Comparison);
                config.AllowFiltering(p => p.StockQuantity, FilterOperators.Comparison);
                config.AllowFiltering(p => p.CreatedAt, FilterOperators.Comparison);
                config.AllowFiltering(p => p.IsActive);
                config.AllowFiltering(p => p.CategoryId);
                config.AllowFiltering(p => p.Status);
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
    public async Task GetAll_EqFilterOnGuid_ReturnsMatchingEntity()
    {
        // Arrange
        var targetId = Guid.NewGuid();
        await SeedProductsAsync(
            new ProductEntity { Id = targetId, ProductName = "Target", UnitPrice = 10m, StockQuantity = 1, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Other", UnitPrice = 20m, StockQuantity = 2, CreatedAt = DateTime.UtcNow, IsActive = true });

        // Act
        var response = await _client.GetAsync($"/api/products?id={targetId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(1);
        items[0].GetProperty("id").GetString().Should().Be(targetId.ToString());
    }

    [Fact]
    public async Task GetAll_EqFilterOnString_ReturnsMatchingEntities()
    {
        // Arrange
        await SeedProductsAsync(
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Widget", UnitPrice = 10m, StockQuantity = 1, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Gadget", UnitPrice = 20m, StockQuantity = 2, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Widget", UnitPrice = 30m, StockQuantity = 3, CreatedAt = DateTime.UtcNow, IsActive = true });

        // Act
        var response = await _client.GetAsync("/api/products?product_name=Widget");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        json.GetProperty("items").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task GetAll_EqFilterOnBool_ReturnsMatchingEntities()
    {
        // Arrange
        await SeedProductsAsync(
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Active 1", UnitPrice = 10m, StockQuantity = 1, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Active 2", UnitPrice = 20m, StockQuantity = 2, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Inactive", UnitPrice = 30m, StockQuantity = 3, CreatedAt = DateTime.UtcNow, IsActive = false });

        // Act
        var response = await _client.GetAsync("/api/products?is_active=true");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        json.GetProperty("items").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task GetAll_EqFilterOnInt_ReturnsMatchingEntities()
    {
        // Arrange
        await SeedProductsAsync(
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "P1", UnitPrice = 10m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "P2", UnitPrice = 20m, StockQuantity = 10, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "P3", UnitPrice = 30m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true });

        // Act
        var response = await _client.GetAsync("/api/products?stock_quantity=5");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        json.GetProperty("items").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task GetAll_EqFilterOnDecimal_ReturnsMatchingEntities()
    {
        // Arrange
        await SeedProductsAsync(
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "P1", UnitPrice = 10m, StockQuantity = 1, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "P2", UnitPrice = 20m, StockQuantity = 2, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "P3", UnitPrice = 10m, StockQuantity = 3, CreatedAt = DateTime.UtcNow, IsActive = true });

        // Act
        var response = await _client.GetAsync("/api/products?unit_price=10");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        json.GetProperty("items").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task GetAll_NeqFilter_ExcludesMatchingEntities()
    {
        // Arrange
        await SeedProductsAsync(
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Widget", UnitPrice = 10m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Gadget", UnitPrice = 20m, StockQuantity = 10, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Sprocket", UnitPrice = 30m, StockQuantity = 15, CreatedAt = DateTime.UtcNow, IsActive = true });

        // Act
        var response = await _client.GetAsync("/api/products?stock_quantity[neq]=10");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        json.GetProperty("items").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task GetAll_GtFilter_ReturnsEntitiesAboveThreshold()
    {
        // Arrange
        await SeedProductsAsync(
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "P1", UnitPrice = 10m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "P2", UnitPrice = 20m, StockQuantity = 10, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "P3", UnitPrice = 30m, StockQuantity = 15, CreatedAt = DateTime.UtcNow, IsActive = true });

        // Act
        var response = await _client.GetAsync("/api/products?unit_price[gt]=15");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        json.GetProperty("items").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task GetAll_LtFilter_ReturnsEntitiesBelowThreshold()
    {
        // Arrange
        await SeedProductsAsync(
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "P1", UnitPrice = 10m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "P2", UnitPrice = 20m, StockQuantity = 10, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "P3", UnitPrice = 30m, StockQuantity = 15, CreatedAt = DateTime.UtcNow, IsActive = true });

        // Act
        var response = await _client.GetAsync("/api/products?unit_price[lt]=25");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        json.GetProperty("items").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task GetAll_GteFilter_ReturnsEntitiesAtOrAboveThreshold()
    {
        // Arrange
        await SeedProductsAsync(
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "P1", UnitPrice = 10m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "P2", UnitPrice = 20m, StockQuantity = 10, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "P3", UnitPrice = 30m, StockQuantity = 15, CreatedAt = DateTime.UtcNow, IsActive = true });

        // Act
        var response = await _client.GetAsync("/api/products?unit_price[gte]=20");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        json.GetProperty("items").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task GetAll_LteFilter_ReturnsEntitiesAtOrBelowThreshold()
    {
        // Arrange
        await SeedProductsAsync(
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "P1", UnitPrice = 10m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "P2", UnitPrice = 20m, StockQuantity = 10, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "P3", UnitPrice = 30m, StockQuantity = 15, CreatedAt = DateTime.UtcNow, IsActive = true });

        // Act
        var response = await _client.GetAsync("/api/products?unit_price[lte]=20");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        json.GetProperty("items").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task GetAll_GtFilterOnDateTime_ReturnsEntitiesAfterDate()
    {
        // Arrange
        var baseDate = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedProductsAsync(
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Old", UnitPrice = 10m, StockQuantity = 1, CreatedAt = baseDate, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Mid", UnitPrice = 20m, StockQuantity = 2, CreatedAt = baseDate.AddDays(5), IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "New", UnitPrice = 30m, StockQuantity = 3, CreatedAt = baseDate.AddDays(10), IsActive = true });

        // Act
        var filterDate = Uri.EscapeDataString(baseDate.AddDays(3).ToString("O"));
        var response = await _client.GetAsync($"/api/products?created_at[gt]={filterDate}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        json.GetProperty("items").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task GetAll_LtFilterOnDateTime_ReturnsEntitiesBeforeDate()
    {
        // Arrange
        var baseDate = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedProductsAsync(
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Old", UnitPrice = 10m, StockQuantity = 1, CreatedAt = baseDate, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Mid", UnitPrice = 20m, StockQuantity = 2, CreatedAt = baseDate.AddDays(5), IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "New", UnitPrice = 30m, StockQuantity = 3, CreatedAt = baseDate.AddDays(10), IsActive = true });

        // Act
        var filterDate = Uri.EscapeDataString(baseDate.AddDays(7).ToString("O"));
        var response = await _client.GetAsync($"/api/products?created_at[lt]={filterDate}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        json.GetProperty("items").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task GetAll_MultipleComparisonFilters_ReturnIntersection()
    {
        // Arrange
        await SeedProductsAsync(
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Cheap Active", UnitPrice = 5m, StockQuantity = 10, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Mid Active", UnitPrice = 25m, StockQuantity = 20, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Expensive Active", UnitPrice = 50m, StockQuantity = 30, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Mid Inactive", UnitPrice = 25m, StockQuantity = 20, CreatedAt = DateTime.UtcNow, IsActive = false });

        // Act
        var response = await _client.GetAsync("/api/products?unit_price[gte]=10&unit_price[lte]=30&is_active=true");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(1);
        items[0].GetProperty("product_name").GetString().Should().Be("Mid Active");
    }

    [Fact]
    public async Task GetAll_FilterWithNoMatches_ReturnsEmptyCollection()
    {
        // Arrange
        await SeedProductsAsync(
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "P1", UnitPrice = 10m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "P2", UnitPrice = 20m, StockQuantity = 10, CreatedAt = DateTime.UtcNow, IsActive = true });

        // Act
        var response = await _client.GetAsync("/api/products?unit_price[gt]=1000");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        json.GetProperty("items").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task GetAll_NoFilters_ReturnsAllEntities()
    {
        // Arrange
        await SeedProductsAsync(
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "P1", UnitPrice = 10m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "P2", UnitPrice = 20m, StockQuantity = 10, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "P3", UnitPrice = 30m, StockQuantity = 15, CreatedAt = DateTime.UtcNow, IsActive = true });

        // Act
        var response = await _client.GetAsync("/api/products");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        json.GetProperty("items").GetArrayLength().Should().Be(3);
    }

    [Fact]
    public async Task GetAll_FilteredCount_ReflectsFilteredDataset()
    {
        // Arrange
        await SeedProductsAsync(
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Active 1", UnitPrice = 10m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Active 2", UnitPrice = 20m, StockQuantity = 10, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Inactive", UnitPrice = 30m, StockQuantity = 15, CreatedAt = DateTime.UtcNow, IsActive = false });

        // Act
        var response = await _client.GetAsync("/api/products?is_active=true");

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
