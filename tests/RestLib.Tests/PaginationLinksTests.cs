using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using RestLib.InMemory;
using RestLib.Pagination;
using RestLib.Tests.Fakes;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Tests for Story 4.2: Pagination Links.
/// Verifies that links are absolute URLs, preserve query filters, and can be disabled.
/// </summary>
public class PaginationLinksTests : IDisposable
{
    private readonly IHost _host;
    private readonly HttpClient _client;
    private readonly InMemoryRepository<ProductEntity, Guid> _repository;

    public PaginationLinksTests()
    {
        _repository = new InMemoryRepository<ProductEntity, Guid>(e => e.Id, Guid.NewGuid);

        (_host, _client) = new TestHostBuilder<ProductEntity, Guid>(_repository, "/api/products")
            .WithOptions(options =>
            {
                options.DefaultPageSize = 10;
                options.MaxPageSize = 100;
                options.IncludePaginationLinks = true;
            })
            .WithEndpoint(config => config.AllowAnonymous())
            .Build();
    }

    public void Dispose()
    {
        _client.Dispose();
        _host.Dispose();
    }

    #region Absolute URL Tests

    [Fact]
    [Trait("Category", "Story4.2")]
    public async Task GetAll_SelfLink_IsAbsoluteUrl()
    {
        // Arrange
        _repository.SeedProducts(5);

        // Act
        var response = await _client.GetAsync("/api/products");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var selfLink = json.GetProperty("self").GetString();

        selfLink.Should().NotBeNull();
        selfLink.Should().StartWith("http://");
        selfLink.Should().Contain("/api/products");

        // Validate it's a proper absolute URL
        Uri.TryCreate(selfLink, UriKind.Absolute, out var uri).Should().BeTrue();
        uri!.IsAbsoluteUri.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Story4.2")]
    public async Task GetAll_FirstLink_IsAbsoluteUrl()
    {
        // Arrange
        _repository.SeedProducts(5);

        // Act
        var response = await _client.GetAsync("/api/products");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var firstLink = json.GetProperty("first").GetString();

        firstLink.Should().NotBeNull();
        firstLink.Should().StartWith("http://");

        Uri.TryCreate(firstLink, UriKind.Absolute, out var uri).Should().BeTrue();
        uri!.IsAbsoluteUri.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Story4.2")]
    public async Task GetAll_NextLink_IsAbsoluteUrl()
    {
        // Arrange
        _repository.SeedProducts(25);

        // Act
        var response = await _client.GetAsync("/api/products?limit=10");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var nextLink = json.GetProperty("next").GetString();

        nextLink.Should().NotBeNull();
        nextLink.Should().StartWith("http://");

        Uri.TryCreate(nextLink, UriKind.Absolute, out var uri).Should().BeTrue();
        uri!.IsAbsoluteUri.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Story4.2")]
    public async Task GetAll_LinksContainSchemeHostAndPath()
    {
        // Arrange
        _repository.SeedProducts(5);

        // Act
        var response = await _client.GetAsync("/api/products");

        // Assert
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var selfLink = json.GetProperty("self").GetString()!;

        var uri = new Uri(selfLink);
        uri.Scheme.Should().Be("http");
        uri.Host.Should().Be("localhost");
        uri.AbsolutePath.Should().Be("/api/products");
    }

    #endregion

    #region Link Content Tests

    [Fact]
    [Trait("Category", "Story4.2")]
    public async Task GetAll_SelfLink_IncludesLimitParameter()
    {
        // Arrange
        _repository.SeedProducts(5);

        // Act
        var response = await _client.GetAsync("/api/products?limit=15");

        // Assert
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var selfLink = json.GetProperty("self").GetString()!;

        selfLink.Should().Contain("limit=15");
    }

    [Fact]
    [Trait("Category", "Story4.2")]
    public async Task GetAll_SelfLink_IncludesCursorWhenProvided()
    {
        // Arrange
        _repository.SeedProducts(30);
        var cursor = CursorEncoder.Encode(Guid.NewGuid());

        // Act
        var response = await _client.GetAsync($"/api/products?cursor={Uri.EscapeDataString(cursor)}&limit=10");

        // Assert
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var selfLink = json.GetProperty("self").GetString()!;

        selfLink.Should().Contain($"cursor={Uri.EscapeDataString(cursor)}");
        selfLink.Should().Contain("limit=10");
    }

    [Fact]
    [Trait("Category", "Story4.2")]
    public async Task GetAll_FirstLink_DoesNotIncludeCursor()
    {
        // Arrange
        _repository.SeedProducts(30);
        var cursor = CursorEncoder.Encode(Guid.NewGuid());

        // Act
        var response = await _client.GetAsync($"/api/products?cursor={Uri.EscapeDataString(cursor)}&limit=10");

        // Assert
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var firstLink = json.GetProperty("first").GetString()!;

        firstLink.Should().NotContain("cursor=");
        firstLink.Should().Contain("limit=10");
    }

    [Fact]
    [Trait("Category", "Story4.2")]
    public async Task GetAll_NextLink_IncludesNextCursor()
    {
        // Arrange
        _repository.SeedProducts(25);

        // Act
        var response = await _client.GetAsync("/api/products?limit=10");

        // Assert
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var nextLink = json.GetProperty("next").GetString()!;

        nextLink.Should().Contain("cursor=");
        nextLink.Should().Contain("limit=10");

        // Verify the cursor in the link is a valid base64url string
        var uri = new Uri(nextLink);
        var queryParams = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var nextCursor = queryParams["cursor"];
        CursorEncoder.IsValid(nextCursor!).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Story4.2")]
    public async Task GetAll_WhenNoMoreItems_NextLinkIsOmitted()
    {
        // Arrange
        _repository.SeedProducts(5);

        // Act
        var response = await _client.GetAsync("/api/products?limit=10");

        // Assert
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        json.TryGetProperty("next", out var nextProperty).Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Story4.2")]
    public async Task GetAll_FollowingNextLink_ReturnsNextPage()
    {
        // Arrange
        _repository.SeedProducts(25);

        // Act - Get first page
        var firstResponse = await _client.GetAsync("/api/products?limit=10");
        var firstJson = await firstResponse.Content.ReadFromJsonAsync<JsonElement>();
        var nextLink = firstJson.GetProperty("next").GetString()!;

        // Extract path and query from the absolute URL for the test client
        var nextUri = new Uri(nextLink);
        var pathAndQuery = nextUri.PathAndQuery;

        // Follow next link
        var secondResponse = await _client.GetAsync(pathAndQuery);

        // Assert
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondJson = await secondResponse.Content.ReadFromJsonAsync<JsonElement>();
        secondJson.GetProperty("items").GetArrayLength().Should().Be(10);
    }

    #endregion

    #region Query Filter Preservation Tests

    [Fact]
    [Trait("Category", "Story4.2")]
    public async Task GetAll_SelfLink_PreservesQueryFilters()
    {
        // Arrange
        _repository.SeedProducts(5);

        // Act - Request with custom query param (simulating a filter)
        var response = await _client.GetAsync("/api/products?custom_filter=value&limit=10");

        // Assert
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var selfLink = json.GetProperty("self").GetString()!;

        selfLink.Should().Contain("custom_filter=value");
        selfLink.Should().Contain("limit=10");
    }

    [Fact]
    [Trait("Category", "Story4.2")]
    public async Task GetAll_FirstLink_PreservesQueryFilters()
    {
        // Arrange
        _repository.SeedProducts(5);

        // Act
        var response = await _client.GetAsync("/api/products?category=electronics&is_active=true");

        // Assert
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var firstLink = json.GetProperty("first").GetString()!;

        firstLink.Should().Contain("category=electronics");
        firstLink.Should().Contain("is_active=true");
    }

    [Fact]
    [Trait("Category", "Story4.2")]
    public async Task GetAll_NextLink_PreservesQueryFilters()
    {
        // Arrange
        _repository.SeedProducts(25);

        // Act
        var response = await _client.GetAsync("/api/products?status=active&limit=10");

        // Assert
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var nextLink = json.GetProperty("next").GetString()!;

        nextLink.Should().Contain("status=active");
        nextLink.Should().Contain("limit=10");
    }

    [Fact]
    [Trait("Category", "Story4.2")]
    public async Task GetAll_AllLinks_PreserveMultipleFilters()
    {
        // Arrange
        _repository.SeedProducts(25);

        // Act
        var response = await _client.GetAsync("/api/products?category=books&price_min=10&price_max=50&limit=10");

        // Assert
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        var selfLink = json.GetProperty("self").GetString()!;
        var firstLink = json.GetProperty("first").GetString()!;
        var nextLink = json.GetProperty("next").GetString()!;

        // All links should preserve all filters
        foreach (var link in new[] { selfLink, firstLink, nextLink })
        {
            link.Should().Contain("category=books");
            link.Should().Contain("price_min=10");
            link.Should().Contain("price_max=50");
            link.Should().Contain("limit=10");
        }
    }

    [Fact]
    [Trait("Category", "Story4.2")]
    public async Task GetAll_Links_ExcludeCursorAndLimitFromFilters()
    {
        // Arrange
        _repository.SeedProducts(25);
        var cursor = CursorEncoder.Encode(Guid.NewGuid());

        // Act - Cursor and limit are pagination params, not filters
        var response = await _client.GetAsync($"/api/products?filter=test&cursor={Uri.EscapeDataString(cursor)}&limit=10");

        // Assert
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var firstLink = json.GetProperty("first").GetString()!;

        // First link should have the filter but NOT the cursor
        firstLink.Should().Contain("filter=test");
        firstLink.Should().NotContain("cursor=");
        firstLink.Should().Contain("limit=10");
    }

    [Fact]
    [Trait("Category", "Story4.2")]
    public async Task GetAll_Links_PreserveUrlEncodedFilterValues()
    {
        // Arrange
        _repository.SeedProducts(5);

        // Act - Filter value with special characters
        var response = await _client.GetAsync("/api/products?search=hello%20world&tag=c%23");

        // Assert
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var selfLink = json.GetProperty("self").GetString()!;

        // Values should be properly URL encoded
        selfLink.Should().Contain("search=hello%20world");
        selfLink.Should().Contain("tag=c%23");
    }

    [Fact]
    [Trait("Category", "Story4.2")]
    public async Task GetAll_Links_PreserveMultiValueFilters()
    {
        // Arrange
        _repository.SeedProducts(5);

        // Act - Same parameter with multiple values
        var response = await _client.GetAsync("/api/products?tag=red&tag=blue&tag=green");

        // Assert
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var selfLink = json.GetProperty("self").GetString()!;

        selfLink.Should().Contain("tag=red");
        selfLink.Should().Contain("tag=blue");
        selfLink.Should().Contain("tag=green");
    }

    #endregion

    #region Link Order Tests

    [Fact]
    [Trait("Category", "Story4.2")]
    public async Task GetAll_Links_HaveConsistentParameterOrder()
    {
        // Arrange
        _repository.SeedProducts(25);

        // Act
        var response = await _client.GetAsync("/api/products?filter=test&limit=10");

        // Assert
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var selfLink = json.GetProperty("self").GetString()!;
        var nextLink = json.GetProperty("next").GetString()!;

        // Cursor (if present) comes first, then limit, then filters
        var selfUri = new Uri(selfLink);
        var nextUri = new Uri(nextLink);

        selfUri.Query.Should().Be("?limit=10&filter=test");
        nextUri.Query.Should().StartWith("?cursor=");
    }

    #endregion
}
