using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using RestLib.EntityFrameworkCore.Tests.Fakes;
using RestLib.Serialization;
using Xunit;

namespace RestLib.EntityFrameworkCore.Tests;

/// <summary>
/// Integration tests that verify sorting works correctly through the full HTTP
/// pipeline with the EF Core adapter.
/// </summary>
[Trait("Category", "Story9.2")]
[Trait("Type", "Integration")]
public class EfCoreSortingIntegrationTests : IAsyncLifetime
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
        await ClearProductsAsync();
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
        await ClearProductsAsync();
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
        await ClearProductsAsync();
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
        await ClearProductsAsync();
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
    public async Task GetAll_SortCombinedWithPagination_ReturnsSortedPages()
    {
        // Arrange
        await ClearProductsAsync();
        await SeedProductsAsync(
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Product E", UnitPrice = 50m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Product A", UnitPrice = 10m, StockQuantity = 1, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Product D", UnitPrice = 40m, StockQuantity = 4, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Product B", UnitPrice = 20m, StockQuantity = 2, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Product C", UnitPrice = 30m, StockQuantity = 3, CreatedAt = DateTime.UtcNow, IsActive = true });

        var collectedPrices = new List<decimal>();
        string? cursor = null;

        // Act
        do
        {
            var url = cursor is null
                ? "/api/products?sort=unit_price:asc&limit=2"
                : $"/api/products?sort=unit_price:asc&limit=2&cursor={Uri.EscapeDataString(cursor)}";
            var response = await _client.GetAsync(url);
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var page = await DeserializeCollectionResponseAsync(response);
            collectedPrices.AddRange(page.Items.Select(product => product.UnitPrice));
            cursor = GetCursorFromNextLink(page.Next);
        }
        while (cursor is not null);

        // Assert
        collectedPrices.Should().HaveCount(5);
        collectedPrices.Should().BeInAscendingOrder();
        collectedPrices.Should().Equal(10m, 20m, 30m, 40m, 50m);
    }

    [Fact]
    public async Task GetAll_SortCombinedWithFilterAndPagination_ReturnsFilteredSortedPages()
    {
        // Arrange
        await ClearProductsAsync();
        await SeedProductsAsync(
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Active 50", UnitPrice = 50m, StockQuantity = 1, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Active 10", UnitPrice = 10m, StockQuantity = 2, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Active 40", UnitPrice = 40m, StockQuantity = 3, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Active 20", UnitPrice = 20m, StockQuantity = 4, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Active 30", UnitPrice = 30m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Inactive 15", UnitPrice = 15m, StockQuantity = 6, CreatedAt = DateTime.UtcNow, IsActive = false },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Inactive 25", UnitPrice = 25m, StockQuantity = 7, CreatedAt = DateTime.UtcNow, IsActive = false },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Inactive 35", UnitPrice = 35m, StockQuantity = 8, CreatedAt = DateTime.UtcNow, IsActive = false });

        var collectedPrices = new List<decimal>();
        var collectedIds = new List<Guid>();
        var collectedItems = new List<ProductEntity>();
        string? cursor = null;

        // Act
        do
        {
            var url = cursor is null
                ? "/api/products?is_active=true&sort=unit_price:asc&limit=2"
                : $"/api/products?is_active=true&sort=unit_price:asc&limit=2&cursor={Uri.EscapeDataString(cursor)}";
            var response = await _client.GetAsync(url);
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var page = await DeserializeCollectionResponseAsync(response);
            foreach (var item in page.Items)
            {
                collectedPrices.Add(item.UnitPrice);
                collectedIds.Add(item.Id);
                collectedItems.Add(item);
            }

            cursor = GetCursorFromNextLink(page.Next);
        }
        while (cursor is not null);

        // Assert
        collectedPrices.Should().HaveCount(5);
        collectedPrices.Should().BeInAscendingOrder();
        collectedPrices.Should().Equal(10m, 20m, 30m, 40m, 50m);
        collectedIds.Should().OnlyHaveUniqueItems();
        collectedItems.Should().OnlyContain(product => product.IsActive);
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
