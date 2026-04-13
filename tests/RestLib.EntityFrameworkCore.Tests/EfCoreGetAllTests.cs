using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using RestLib.EntityFrameworkCore.Tests.Fakes;
using RestLib.Serialization;
using Xunit;

namespace RestLib.EntityFrameworkCore.Tests;

/// <summary>
/// Integration tests for the EF Core-backed GetAll endpoint.
/// </summary>
[Trait("Category", "Story3.1.2")]
public class EfCoreGetAllTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = RestLibJsonOptions.CreateDefault();

    private IHost _host = null!;
    private HttpClient _client = null!;
    private TestDbContext _db = null!;
    private List<ProductEntity> _seededProducts = null!;

    /// <summary>
    /// Sets up the test host and seeds products.
    /// </summary>
    public async Task InitializeAsync()
    {
        (_host, _client, _db) = await new EfCoreTestHostBuilder<ProductEntity, Guid>("/api/products")
            .WithEndpoint(config => config.AllowAnonymous())
            .BuildAsync();

        _seededProducts = SeedData.CreateProducts(5)
            .OrderBy(product => product.Id)
            .ToList();

        _db.Products.AddRange(_seededProducts);
        await _db.SaveChangesAsync();
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
    public async Task GetAll_ReturnsAllEntities_WhenCountIsLessThanOrEqualToLimit()
    {
        // Arrange

        // Act
        var response = await _client.GetAsync("/api/products?limit=10");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await DeserializeCollectionResponseAsync(response);

        content.Items.Should().HaveCount(5);
        content.TotalCount.Should().Be(5);
        content.Next.Should().BeNull();
        content.Items.Select(product => product.Id).Should().BeEquivalentTo(_seededProducts.Select(product => product.Id));
    }

    [Fact]
    public async Task GetAll_ReturnsFirstPage_WithHasMore_WhenCountExceedsLimit()
    {
        // Arrange

        // Act
        var response = await _client.GetAsync("/api/products?limit=2");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await DeserializeCollectionResponseAsync(response);

        content.Items.Should().HaveCount(2);
        content.TotalCount.Should().Be(5);
        content.Next.Should().NotBeNullOrEmpty();
        content.Items.Select(product => product.Id).Should().Equal(_seededProducts.Take(2).Select(product => product.Id));
    }

    [Fact]
    public async Task GetAll_FollowingCursor_ReturnsNextPage()
    {
        // Arrange
        var firstResponse = await _client.GetAsync("/api/products?limit=2");
        var firstPage = await DeserializeCollectionResponseAsync(firstResponse);
        var nextCursor = GetCursorFromNextLink(firstPage.Next);

        // Act
        var secondResponse = await _client.GetAsync($"/api/products?limit=2&cursor={Uri.EscapeDataString(nextCursor!)}");

        // Assert
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var secondPage = await DeserializeCollectionResponseAsync(secondResponse);

        secondPage.Items.Should().HaveCount(2);
        secondPage.Items.Select(product => product.Id).Should().Equal(_seededProducts.Skip(2).Take(2).Select(product => product.Id));
        secondPage.Items.Select(product => product.Id).Should().NotIntersectWith(firstPage.Items.Select(product => product.Id));
    }

    [Fact]
    public async Task GetAll_PaginateThroughAllItems_SeesAllEntities()
    {
        // Arrange
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
        collected.Should().HaveCount(5);
        collected.Select(product => product.Id).Should().OnlyHaveUniqueItems();
        collected.Select(product => product.Id).Should().BeEquivalentTo(_seededProducts.Select(product => product.Id));
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
