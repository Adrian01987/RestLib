using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using RestLib.EntityFrameworkCore.Tests.Fakes;
using RestLib.Filtering;
using RestLib.Serialization;
using Xunit;

namespace RestLib.EntityFrameworkCore.Tests;

/// <summary>
/// Integration tests that verify all filtering features work correctly through the
/// full HTTP pipeline with the EF Core adapter.
/// </summary>
[Trait("Category", "Story9.2")]
[Trait("Type", "Integration")]
public class EfCoreFilteringIntegrationTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = RestLibJsonOptions.CreateDefault();

    private IHost _host = null!;
    private HttpClient _client = null!;
    private TestDbContext _dbContext = null!;

    public async Task InitializeAsync()
    {
        (_host, _client, _dbContext) = await new EfCoreTestHostBuilder<ProductEntity, Guid>("/api/products")
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.AllowFiltering(p => p.Id);
                config.AllowFiltering(p => p.ProductName, FilterOperators.All);
                config.AllowFiltering(p => p.UnitPrice, FilterOperators.Comparison);
                config.AllowFiltering(p => p.StockQuantity, FilterOperators.Comparison);
                config.AllowFiltering(p => p.IsActive);
                config.AllowFiltering(p => p.Status, FilterOperators.All);
                config.AllowFiltering(p => p.CategoryId);
                config.AllowFiltering(p => p.CreatedAt, FilterOperators.Comparison);
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
    public async Task GetAll_EqFilterOnGuid_ReturnsMatchingEntity()
    {
        // Arrange
        await ClearProductsAsync();
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
        await ClearProductsAsync();
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
        await ClearProductsAsync();
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
        await ClearProductsAsync();
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
        await ClearProductsAsync();
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
        await ClearProductsAsync();
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
        await ClearProductsAsync();
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
        await ClearProductsAsync();
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
        await ClearProductsAsync();
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
        await ClearProductsAsync();
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
    public async Task GetAll_ContainsFilter_ReturnsEntitiesWithSubstring()
    {
        // Arrange
        await ClearProductsAsync();
        await SeedProductsAsync(
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Blue Widget", UnitPrice = 10m, StockQuantity = 1, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Red Widget", UnitPrice = 20m, StockQuantity = 2, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Green Gadget", UnitPrice = 30m, StockQuantity = 3, CreatedAt = DateTime.UtcNow, IsActive = true });

        // Act
        var response = await _client.GetAsync("/api/products?product_name[contains]=Widget");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        json.GetProperty("items").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task GetAll_StartsWithFilter_ReturnsEntitiesWithPrefix()
    {
        // Arrange
        await ClearProductsAsync();
        await SeedProductsAsync(
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Widget Alpha", UnitPrice = 10m, StockQuantity = 1, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Widget Beta", UnitPrice = 20m, StockQuantity = 2, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Gadget Alpha", UnitPrice = 30m, StockQuantity = 3, CreatedAt = DateTime.UtcNow, IsActive = true });

        // Act
        var response = await _client.GetAsync("/api/products?product_name[starts_with]=Widget");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        json.GetProperty("items").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task GetAll_EndsWithFilter_ReturnsEntitiesWithSuffix()
    {
        // Arrange
        await ClearProductsAsync();
        await SeedProductsAsync(
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Widget Alpha", UnitPrice = 10m, StockQuantity = 1, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Gadget Alpha", UnitPrice = 20m, StockQuantity = 2, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Widget Beta", UnitPrice = 30m, StockQuantity = 3, CreatedAt = DateTime.UtcNow, IsActive = true });

        // Act
        var response = await _client.GetAsync("/api/products?product_name[ends_with]=Alpha");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        json.GetProperty("items").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task GetAll_InFilter_ReturnsEntitiesMatchingAnyValue()
    {
        // Arrange
        await ClearProductsAsync();
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
    public async Task GetAll_MultipleCombinedFilters_ReturnIntersection()
    {
        // Arrange
        await ClearProductsAsync();
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
        await ClearProductsAsync();
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
    public async Task GetAll_FilterCombinedWithPagination_ReturnsFilteredPages()
    {
        // Arrange
        await ClearProductsAsync();
        await SeedProductsAsync(
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Active 1", UnitPrice = 10m, StockQuantity = 1, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Inactive 1", UnitPrice = 15m, StockQuantity = 2, CreatedAt = DateTime.UtcNow, IsActive = false },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Active 2", UnitPrice = 20m, StockQuantity = 3, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Inactive 2", UnitPrice = 25m, StockQuantity = 4, CreatedAt = DateTime.UtcNow, IsActive = false },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Active 3", UnitPrice = 30m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Inactive 3", UnitPrice = 35m, StockQuantity = 6, CreatedAt = DateTime.UtcNow, IsActive = false },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Active 4", UnitPrice = 40m, StockQuantity = 7, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Active 5", UnitPrice = 45m, StockQuantity = 8, CreatedAt = DateTime.UtcNow, IsActive = true });

        var collected = new List<ProductEntity>();
        string? cursor = null;

        // Act
        do
        {
            var url = cursor is null
                ? "/api/products?is_active=true&limit=2"
                : $"/api/products?is_active=true&limit=2&cursor={Uri.EscapeDataString(cursor)}";
            var response = await _client.GetAsync(url);
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var page = await DeserializeCollectionResponseAsync(response);
            collected.AddRange(page.Items);
            cursor = GetCursorFromNextLink(page.Next);
        }
        while (cursor is not null);

        // Assert
        collected.Should().HaveCount(5);
        collected.Select(product => product.Id).Should().OnlyHaveUniqueItems();
        collected.Should().OnlyContain(product => product.IsActive);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<CollectionResponse> DeserializeCollectionResponseAsync(HttpResponseMessage response)
    {
        return JsonSerializer.Deserialize<CollectionResponse>(
            await response.Content.ReadAsStringAsync(),
            JsonOptions)!;
    }

    private static string? GetCursorFromNextLink(string? nextLink)
    {
        if (string.IsNullOrEmpty(nextLink))
        {
            return null;
        }

        var query = new Uri(nextLink).Query;
        if (string.IsNullOrEmpty(query))
        {
            return null;
        }

        foreach (var segment in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = segment.Split('=', 2);
            if (parts.Length == 2 && string.Equals(parts[0], "cursor", StringComparison.Ordinal))
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        return null;
    }

    private async Task SeedProductsAsync(params ProductEntity[] products)
    {
        _dbContext.Products.AddRange(products);
        await _dbContext.SaveChangesAsync();
    }

    private async Task ClearProductsAsync()
    {
        _dbContext.Products.RemoveRange(_dbContext.Products);
        await _dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// Deserialization model for collection responses.
    /// </summary>
    private class CollectionResponse
    {
        /// <summary>
        /// Gets or sets the returned items.
        /// </summary>
        public List<ProductEntity> Items { get; set; } = [];

        /// <summary>
        /// Gets or sets the total count.
        /// </summary>
        public long? TotalCount { get; set; }

        /// <summary>
        /// Gets or sets the next page link.
        /// </summary>
        public string? Next { get; set; }
    }
}
