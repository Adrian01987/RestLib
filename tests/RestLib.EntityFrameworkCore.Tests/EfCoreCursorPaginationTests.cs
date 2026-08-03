using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using RestLib.EntityFrameworkCore.Tests.Fakes;
using RestLib.Pagination;
using RestLib.Serialization;
using Xunit;

namespace RestLib.EntityFrameworkCore.Tests;

/// <summary>
/// Integration tests for cursor-based pagination with the EF Core repository.
/// Validates cursor encoding/decoding, pagination through all items, pagination
/// combined with filters, pagination combined with sorting, and invalid cursor handling.
/// </summary>
[Trait("Category", "Story7.1")]
[Trait("Type", "Integration")]
public class EfCoreCursorPaginationTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = RestLibJsonOptions.CreateDefault();

    private IHost _host = null!;
    private HttpClient _client = null!;
    private TestDbContext _db = null!;

    public async Task InitializeAsync()
    {
        (_host, _client, _db) = await new EfCoreTestHostBuilder<ProductEntity, Guid>("/api/products")
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.AllowSorting(
                    p => p.UnitPrice,
                    p => p.ProductName,
                    p => p.LastModifiedAt,
                    p => p.Lifecycle,
                    p => p.IsActive);
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
    public async Task GetAll_PaginateThroughAllItems_WithLimit2_SeesAllEntities()
    {
        // Arrange
        var products = Enumerable.Range(1, 7)
            .Select(i => new ProductEntity
            {
                Id = Guid.NewGuid(),
                ProductName = $"Product {i}",
                UnitPrice = 10m * i,
                StockQuantity = i,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            })
            .ToArray();

        await SeedProductsAsync(products);

        var collected = new List<ProductEntity>();
        string? cursor = null;

        // Act
        do
        {
            var url = cursor is null
                ? "/api/products?limit=2"
                : $"/api/products?limit=2&cursor={Uri.EscapeDataString(cursor)}";
            var response = await _client.GetAsync(url);
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var page = await DeserializeCollectionResponseAsync(response);
            collected.AddRange(page.Items);
            cursor = GetCursorFromNextLink(page.Next);
        }
        while (cursor is not null);

        // Assert
        collected.Should().HaveCount(7);
        collected.Select(product => product.Id).Should().OnlyHaveUniqueItems();
        collected.Select(product => product.Id).Should().BeEquivalentTo(products.Select(product => product.Id));
    }

    [Fact]
    public async Task GetAll_CursorWithFilters_ReturnsCorrectSubsequentPages()
    {
        // Arrange
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
        collected.Select(product => product.ProductName).Should().BeEquivalentTo(
            new[] { "Active 1", "Active 2", "Active 3", "Active 4", "Active 5" });
    }

    [Fact]
    public async Task GetAll_CursorWithSorting_ReturnsCorrectSubsequentPages()
    {
        // Arrange
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
    public async Task GetAll_CursorWithDuplicateSortValues_SeesAllEntitiesExactlyOnce()
    {
        // Arrange
        await SeedProductsAsync(
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "A", UnitPrice = 10m, StockQuantity = 1, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "B", UnitPrice = 10m, StockQuantity = 2, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "C", UnitPrice = 10m, StockQuantity = 3, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "D", UnitPrice = 20m, StockQuantity = 4, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "E", UnitPrice = 20m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "F", UnitPrice = 30m, StockQuantity = 6, CreatedAt = DateTime.UtcNow, IsActive = true });

        var collectedIds = new List<Guid>();
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
            collectedIds.AddRange(page.Items.Select(product => product.Id));
            cursor = GetCursorFromNextLink(page.Next);
        }
        while (cursor is not null);

        // Assert
        collectedIds.Should().HaveCount(6);
        collectedIds.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task GetAll_CursorWithDescendingSorting_ReturnsCorrectSubsequentPages()
    {
        // Arrange
        await SeedProductsAsync(
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Product A", UnitPrice = 10m, StockQuantity = 1, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Product B", UnitPrice = 20m, StockQuantity = 2, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Product C", UnitPrice = 30m, StockQuantity = 3, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Product D", UnitPrice = 40m, StockQuantity = 4, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Product E", UnitPrice = 50m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true });

        var collectedPrices = new List<decimal>();
        string? cursor = null;

        // Act
        do
        {
            var url = cursor is null
                ? "/api/products?sort=unit_price:desc&limit=2"
                : $"/api/products?sort=unit_price:desc&limit=2&cursor={Uri.EscapeDataString(cursor)}";
            var response = await _client.GetAsync(url);
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var page = await DeserializeCollectionResponseAsync(response);
            collectedPrices.AddRange(page.Items.Select(product => product.UnitPrice));
            cursor = GetCursorFromNextLink(page.Next);
        }
        while (cursor is not null);

        // Assert
        collectedPrices.Should().Equal(50m, 40m, 30m, 20m, 10m);
    }

    [Fact]
    public async Task GetAll_CursorWithNullableSort_UsesConsumableOffsetCursorsInBothDirections()
    {
        // Arrange
        await SeedProductsAsync(
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "No Modified", UnitPrice = 10m, CreatedAt = DateTime.UtcNow, LastModifiedAt = null, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Modified Later", UnitPrice = 20m, CreatedAt = DateTime.UtcNow, LastModifiedAt = new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc), IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Modified Earlier", UnitPrice = 30m, CreatedAt = DateTime.UtcNow, LastModifiedAt = new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc), IsActive = true });

        var firstResponse = await _client.GetAsync("/api/products?sort=last_modified_at:asc&limit=1");
        var firstPage = await DeserializeCollectionResponseAsync(firstResponse);
        var firstCursor = GetCursorFromNextLink(firstPage.Next);

        // Act
        var ascending = await CollectAllPagesAsync("last_modified_at:asc", limit: 1);
        var descending = await CollectAllPagesAsync("last_modified_at:desc", limit: 1);

        // Assert
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        CursorEncoder.TryDecode<int>(firstCursor!, out var offset).Should().BeTrue();
        offset.Should().Be(1);
        ascending.Select(product => product.ProductName)
            .Should().Equal("No Modified", "Modified Earlier", "Modified Later");
        descending.Select(product => product.ProductName)
            .Should().Equal("Modified Later", "Modified Earlier", "No Modified");
    }

    [Fact]
    public async Task GetAll_CursorWithEnumSort_UsesConsumableOffsetCursorsInBothDirections()
    {
        // Arrange
        await SeedProductsAsync(
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Archived", UnitPrice = 30m, CreatedAt = DateTime.UtcNow, IsActive = true, Lifecycle = ProductLifecycle.Archived },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Draft", UnitPrice = 10m, CreatedAt = DateTime.UtcNow, IsActive = true, Lifecycle = ProductLifecycle.Draft },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Active", UnitPrice = 20m, CreatedAt = DateTime.UtcNow, IsActive = true, Lifecycle = ProductLifecycle.Active });

        // Act
        var ascending = await CollectAllPagesAsync("lifecycle:asc", limit: 1);
        var descending = await CollectAllPagesAsync("lifecycle:desc", limit: 1);

        // Assert
        ascending.Select(product => product.Lifecycle)
            .Should().Equal(ProductLifecycle.Draft, ProductLifecycle.Active, ProductLifecycle.Archived);
        descending.Select(product => product.Lifecycle)
            .Should().Equal(ProductLifecycle.Archived, ProductLifecycle.Active, ProductLifecycle.Draft);
    }

    [Fact]
    public async Task GetAll_CursorWithBooleanSort_UsesConsumableOffsetCursorsInBothDirections()
    {
        // Arrange
        await SeedProductsAsync(
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Enabled", UnitPrice = 30m, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Disabled A", UnitPrice = 10m, CreatedAt = DateTime.UtcNow, IsActive = false },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Disabled B", UnitPrice = 20m, CreatedAt = DateTime.UtcNow, IsActive = false });

        // Act
        var ascending = await CollectAllPagesAsync("is_active:asc", limit: 1);
        var descending = await CollectAllPagesAsync("is_active:desc", limit: 1);

        // Assert
        ascending.Select(product => product.IsActive).Should().Equal(false, false, true);
        descending.Select(product => product.IsActive).Should().Equal(true, false, false);
        ascending.Select(product => product.Id).Should().OnlyHaveUniqueItems();
        descending.Select(product => product.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task GetAll_CursorWithStringSort_RemainsConsumableInBothDirections()
    {
        // Arrange
        await SeedProductsAsync(
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Charlie", UnitPrice = 30m, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Alpha", UnitPrice = 10m, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Bravo", UnitPrice = 20m, CreatedAt = DateTime.UtcNow, IsActive = true });

        // Act
        var ascending = await CollectAllPagesAsync("product_name:asc", limit: 1);
        var descending = await CollectAllPagesAsync("product_name:desc", limit: 1);

        // Assert
        ascending.Select(product => product.ProductName).Should().Equal("Alpha", "Bravo", "Charlie");
        descending.Select(product => product.ProductName).Should().Equal("Charlie", "Bravo", "Alpha");
    }

    [Fact]
    public async Task GetAll_CursorWithSorting_RemainsStableAfterInsertBetweenPages()
    {
        // Arrange
        await SeedProductsAsync(
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Product A", UnitPrice = 10m, StockQuantity = 1, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Product B", UnitPrice = 20m, StockQuantity = 2, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Product C", UnitPrice = 30m, StockQuantity = 3, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Product D", UnitPrice = 40m, StockQuantity = 4, CreatedAt = DateTime.UtcNow, IsActive = true });

        var firstResponse = await _client.GetAsync("/api/products?sort=unit_price:asc&limit=2");
        var firstPage = await DeserializeCollectionResponseAsync(firstResponse);
        var cursor = GetCursorFromNextLink(firstPage.Next);

        _db.Products.Add(new ProductEntity
        {
            Id = Guid.NewGuid(),
            ProductName = "Inserted Between Pages",
            UnitPrice = 15m,
            StockQuantity = 99,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        });
        await _db.SaveChangesAsync();

        // Act
        var secondResponse = await _client.GetAsync($"/api/products?sort=unit_price:asc&limit=2&cursor={Uri.EscapeDataString(cursor!)}");

        // Assert
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondPage = await DeserializeCollectionResponseAsync(secondResponse);
        secondPage.Items.Select(product => product.UnitPrice).Should().Equal(30m, 40m);
        secondPage.Items.Select(product => product.Id)
            .Should().NotIntersectWith(firstPage.Items.Select(product => product.Id));
    }

    [Fact]
    public async Task GetAll_OldOffsetCursorAgainstSortedKeysetRequest_Returns400WithProblemDetails()
    {
        // Arrange
        var oldOffsetCursor = RestLib.Pagination.CursorEncoder.Encode(2);

        // Act
        var response = await _client.GetAsync($"/api/products?sort=unit_price:asc&cursor={Uri.EscapeDataString(oldOffsetCursor)}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonSerializer.Deserialize<JsonElement>(content);

        json.GetProperty("type").GetString().Should().Be("/problems/invalid-cursor");
        json.GetProperty("status").GetInt32().Should().Be(400);
    }

    [Fact]
    public async Task GetAll_KeysetCursorAgainstOffsetFallbackSort_Returns400WithProblemDetails()
    {
        // Arrange
        await SeedProductsAsync(
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "A", UnitPrice = 10m, StockQuantity = 1, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "B", UnitPrice = 20m, StockQuantity = 2, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "C", UnitPrice = 30m, StockQuantity = 3, CreatedAt = DateTime.UtcNow, IsActive = true });

        var firstResponse = await _client.GetAsync("/api/products?sort=unit_price:asc&limit=2");
        var firstPage = await DeserializeCollectionResponseAsync(firstResponse);
        var keysetCursor = GetCursorFromNextLink(firstPage.Next);

        // Act
        var response = await _client.GetAsync($"/api/products?sort=product_name:asc&limit=2&cursor={Uri.EscapeDataString(keysetCursor!)}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonSerializer.Deserialize<JsonElement>(content);

        json.GetProperty("type").GetString().Should().Be("/problems/invalid-cursor");
        json.GetProperty("status").GetInt32().Should().Be(400);
    }

    [Fact]
    public async Task GetAll_InvalidCursor_Returns400WithProblemDetails()
    {
        // Arrange
        var invalidCursor = "not-a-valid-cursor";

        // Act
        var response = await _client.GetAsync($"/api/products?cursor={invalidCursor}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonSerializer.Deserialize<JsonElement>(content);

        json.GetProperty("type").GetString().Should().Be("/problems/invalid-cursor");
        json.GetProperty("status").GetInt32().Should().Be(400);
        json.GetProperty("title").GetString().Should().Be("Invalid Cursor");
    }

    [Fact]
    public async Task GetAll_NegativeOffsetCursor_Returns400WithProblemDetails()
    {
        // Arrange
        var cursor = CursorEncoder.Encode(-1);

        // Act
        var response = await _client.GetAsync($"/api/products?sort=lifecycle:asc&cursor={Uri.EscapeDataString(cursor)}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        json.GetProperty("type").GetString().Should().Be("/problems/invalid-cursor");
    }

    [Fact]
    public async Task GetAll_KeysetCursorWithWrongValueType_Returns400WithProblemDetails()
    {
        // Arrange
        var cursor = CursorEncoder.Encode(new EfCoreKeysetCursor
        {
            Version = 1,
            SortSignature = "unit_price:Asc,id:Asc",
            Values =
            [
                JsonSerializer.SerializeToElement("not-a-decimal"),
                JsonSerializer.SerializeToElement(Guid.NewGuid())
            ]
        });

        // Act
        var response = await _client.GetAsync($"/api/products?sort=unit_price:asc&cursor={Uri.EscapeDataString(cursor)}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        json.GetProperty("type").GetString().Should().Be("/problems/invalid-cursor");
    }

    [Fact]
    public async Task GetAll_KeysetCursorWithNullValues_Returns400WithProblemDetails()
    {
        // Arrange
        var cursor = CursorEncoder.Encode(new EfCoreKeysetCursor
        {
            Version = 1,
            SortSignature = "unit_price:Asc,id:Asc",
            Values = null!
        });

        // Act
        var response = await _client.GetAsync($"/api/products?sort=unit_price:asc&cursor={Uri.EscapeDataString(cursor)}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        json.GetProperty("type").GetString().Should().Be("/problems/invalid-cursor");
    }

    [Fact]
    public async Task GetAll_KeysetCursorWithUnsupportedVersion_Returns400WithProblemDetails()
    {
        // Arrange
        var cursor = CursorEncoder.Encode(new EfCoreKeysetCursor
        {
            Version = 2,
            SortSignature = "unit_price:Asc,id:Asc",
            Values =
            [
                JsonSerializer.SerializeToElement(10m),
                JsonSerializer.SerializeToElement(Guid.NewGuid())
            ]
        });

        // Act
        var response = await _client.GetAsync($"/api/products?sort=unit_price:asc&cursor={Uri.EscapeDataString(cursor)}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        json.GetProperty("type").GetString().Should().Be("/problems/invalid-cursor");
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

    private async Task<List<ProductEntity>> CollectAllPagesAsync(string sort, int limit)
    {
        var collected = new List<ProductEntity>();
        string? cursor = null;

        do
        {
            var url = cursor is null
                ? $"/api/products?sort={sort}&limit={limit}"
                : $"/api/products?sort={sort}&limit={limit}&cursor={Uri.EscapeDataString(cursor)}";
            var response = await _client.GetAsync(url);
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var page = await DeserializeCollectionResponseAsync(response);
            collected.AddRange(page.Items);
            cursor = GetCursorFromNextLink(page.Next);
        }
        while (cursor is not null);

        return collected;
    }

    private async Task SeedProductsAsync(params ProductEntity[] products)
    {
        _db.Products.AddRange(products);
        await _db.SaveChangesAsync();
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
