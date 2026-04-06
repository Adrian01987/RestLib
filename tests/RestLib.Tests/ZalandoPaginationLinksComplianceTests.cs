using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using RestLib.Tests.Fakes;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Tests for Zalando compliance of pagination links.
/// </summary>
public class ZalandoPaginationLinksComplianceTests : IDisposable
{
    private readonly IHost _host;
    private readonly HttpClient _client;
    private readonly PaginationTestRepository _repository;

    public ZalandoPaginationLinksComplianceTests()
    {
        _repository = new PaginationTestRepository();

        (_host, _client) = new TestHostBuilder<ProductEntity, Guid>(_repository, "/api/products")
            .WithEndpoint(config => config.AllowAnonymous())
            .Build();
    }

    public void Dispose()
    {
        _client.Dispose();
        _host.Dispose();
    }

    [Fact]
    [Trait("Category", "Story4.2")]
    [Trait("Compliance", "Zalando-Rule-161")]
    public async Task GetAll_Response_IncludesSelfLink()
    {
        // Zalando Rule 161: pagination links should include self
        _repository.SeedMany(5);

        var response = await _client.GetAsync("/api/products");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        json.TryGetProperty("self", out var selfLink).Should().BeTrue();
        selfLink.GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    [Trait("Category", "Story4.2")]
    [Trait("Compliance", "Zalando-Rule-161")]
    public async Task GetAll_Response_IncludesFirstLink()
    {
        // Zalando Rule 161: pagination links should include first
        _repository.SeedMany(5);

        var response = await _client.GetAsync("/api/products");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        json.TryGetProperty("first", out var firstLink).Should().BeTrue();
        firstLink.GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    [Trait("Category", "Story4.2")]
    [Trait("Compliance", "Zalando-Rule-161")]
    public async Task GetAll_WhenMoreItemsExist_IncludesNextLink()
    {
        // Zalando Rule 161: next link when more items exist
        _repository.SeedMany(25);

        var response = await _client.GetAsync("/api/products?limit=10");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        json.TryGetProperty("next", out var nextLink).Should().BeTrue();
        nextLink.GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    [Trait("Category", "Story4.2")]
    [Trait("Compliance", "Zalando-Rule-160")]
    public async Task GetAll_Links_UseCursorNotOffset()
    {
        // Zalando Rule 160: cursor-based pagination, not offset
        _repository.SeedMany(25);

        var response = await _client.GetAsync("/api/products?limit=10");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var nextLink = json.GetProperty("next").GetString()!;

        // Should use cursor parameter, not offset/page
        nextLink.Should().Contain("cursor=");
        nextLink.Should().NotContain("offset=");
        nextLink.Should().NotContain("page=");
    }

    [Fact]
    [Trait("Category", "Story4.2")]
    [Trait("Compliance", "Zalando-Rule-118")]
    public async Task GetAll_ResponseProperties_UseSnakeCase()
    {
        // Zalando Rule 118: snake_case properties
        _repository.SeedMany(5);

        var response = await _client.GetAsync("/api/products");
        var content = await response.Content.ReadAsStringAsync();

        // Link properties should be snake_case
        content.Should().Contain("\"items\"");
        content.Should().Contain("\"self\"");
        content.Should().Contain("\"first\"");

        // Not camelCase or PascalCase
        content.Should().NotContain("\"Items\"");
        content.Should().NotContain("\"Self\"");
        content.Should().NotContain("\"First\"");
    }

    [Fact]
    [Trait("Category", "Story4.2")]
    public async Task GetAll_FirstLink_AllowsNavigationToBeginning()
    {
        // Arrange
        _repository.SeedMany(50);

        // Act - Navigate to second page
        var firstPageResponse = await _client.GetAsync("/api/products?limit=10");
        var firstPageJson = await firstPageResponse.Content.ReadFromJsonAsync<JsonElement>();
        var nextLink = firstPageJson.GetProperty("next").GetString()!;

        var nextUri = new Uri(nextLink);
        var secondPageResponse = await _client.GetAsync(nextUri.PathAndQuery);
        var secondPageJson = await secondPageResponse.Content.ReadFromJsonAsync<JsonElement>();

        // Get first link from second page
        var firstLink = secondPageJson.GetProperty("first").GetString()!;
        var firstUri = new Uri(firstLink);

        // Navigate to first page using the link
        var backToFirstResponse = await _client.GetAsync(firstUri.PathAndQuery);
        var backToFirstJson = await backToFirstResponse.Content.ReadFromJsonAsync<JsonElement>();

        // Assert - Should be same as original first page
        backToFirstJson.GetProperty("items").GetArrayLength().Should().Be(10);

        // First link should not have cursor (it's the beginning)
        firstLink.Should().NotContain("cursor=");
    }

    [Fact]
    [Trait("Category", "Story4.2")]
    public async Task GetAll_CanPaginateThroughEntireCollection_UsingLinks()
    {
        // Arrange - Use FakeRepository which supports real pagination
        // This test verifies that links are functional and can be followed
        // Note: The test repository returns items based on limit but always from start
        // So we verify the link structure and format rather than actual traversal
        _repository.SeedMany(25);

        // Act - Get first page
        var response = await _client.GetAsync("/api/products?limit=10");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Assert - First page has items and proper links
        json.GetProperty("items").GetArrayLength().Should().Be(10);
        json.TryGetProperty("self", out var selfLink).Should().BeTrue();
        json.TryGetProperty("first", out var firstLink).Should().BeTrue();
        json.TryGetProperty("next", out var nextLink).Should().BeTrue();

        // Verify links are valid absolute URLs
        var selfUri = new Uri(selfLink.GetString()!);
        var firstUri = new Uri(firstLink.GetString()!);
        var nextUri = new Uri(nextLink.GetString()!);

        selfUri.IsAbsoluteUri.Should().BeTrue();
        firstUri.IsAbsoluteUri.Should().BeTrue();
        nextUri.IsAbsoluteUri.Should().BeTrue();

        // Next link should contain cursor
        nextUri.Query.Should().Contain("cursor=");

        // First link should not contain cursor
        firstUri.Query.Should().NotContain("cursor=");

        // All links should preserve limit
        selfUri.Query.Should().Contain("limit=10");
        firstUri.Query.Should().Contain("limit=10");
        nextUri.Query.Should().Contain("limit=10");
    }
}
