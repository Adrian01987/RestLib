using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using RestLib.InMemory;
using RestLib.Tests.Fakes;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Edge case tests for pagination links.
/// </summary>
[Trait("Type", "Integration")]
[Trait("Feature", "Pagination")]
public class PaginationLinksEdgeCaseTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private InMemoryRepository<ProductEntity, Guid> _repository = null!;

    public async Task InitializeAsync()
    {
        _repository = new InMemoryRepository<ProductEntity, Guid>(e => e.Id, Guid.NewGuid);

        (_host, _client) = await new TestHostBuilder<ProductEntity, Guid>(_repository, "/api/products")
            .WithEndpoint(config => config.AllowAnonymous())
            .BuildAsync();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    [Fact]
    [Trait("Category", "Story4.2")]
    public async Task GetAll_EmptyCollection_StillHasLinksWhenEnabled()
    {
        // Arrange - Empty repository

        // Act
        var response = await _client.GetAsync("/api/products");

        // Assert
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        json.GetProperty("items").GetArrayLength().Should().Be(0);
        json.TryGetProperty("self", out _).Should().BeTrue();
        json.TryGetProperty("first", out _).Should().BeTrue();
        json.TryGetProperty("next", out _).Should().BeFalse(); // No next for empty
    }

    [Fact]
    [Trait("Category", "Story4.2")]
    public async Task GetAll_ExactlyOnePage_NoNextLink()
    {
        // Arrange
        _repository.SeedProducts(10);

        // Act
        var response = await _client.GetAsync("/api/products?limit=10");

        // Assert
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        json.GetProperty("items").GetArrayLength().Should().Be(10);
        json.TryGetProperty("next", out _).Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Story4.2")]
    public async Task GetAll_OneMoreThanPageSize_HasNextLink()
    {
        // Arrange
        _repository.SeedProducts(11);

        // Act
        var response = await _client.GetAsync("/api/products?limit=10");

        // Assert
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        json.GetProperty("items").GetArrayLength().Should().Be(10);
        json.TryGetProperty("next", out _).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Story4.2")]
    public async Task GetAll_SingleItem_NoNextLink()
    {
        // Arrange
        _repository.SeedProducts(1);

        // Act
        var response = await _client.GetAsync("/api/products");

        // Assert
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        json.GetProperty("items").GetArrayLength().Should().Be(1);
        json.TryGetProperty("next", out _).Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Story4.2")]
    public async Task GetAll_WithSpecialCharactersInPath_LinksAreProperlyFormed()
    {
        // Arrange
        _repository.SeedProducts(5);

        // Act
        var response = await _client.GetAsync("/api/products");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Assert
        var selfLink = json.GetProperty("self").GetString()!;
        Uri.TryCreate(selfLink, UriKind.Absolute, out var uri).Should().BeTrue();
        uri!.AbsolutePath.Should().Be("/api/products");
    }

    [Fact]
    [Trait("Category", "Story4.2")]
    public async Task GetAll_LastPage_FirstLinkStillPresent()
    {
        // Arrange - Seed exactly one page worth of items so there's no next link
        _repository.SeedProducts(10);

        // Act - Request items with limit matching the count
        var response = await _client.GetAsync("/api/products?limit=10");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Assert - This is the "last" (and only) page - no next link
        json.TryGetProperty("next", out _).Should().BeFalse();

        // But first link should still be present
        json.TryGetProperty("first", out var firstLink).Should().BeTrue();
        firstLink.GetString().Should().NotBeNullOrEmpty();
        firstLink.GetString().Should().NotContain("cursor=");
    }

    [Fact]
    [Trait("Category", "Story4.2")]
    public async Task GetAll_WithMixedCaseLimitParam_LinksUseConsistentCase()
    {
        // Arrange
        _repository.SeedProducts(5);

        // Act — ASP.NET query params are case-insensitive
        var response = await _client.GetAsync("/api/products?LIMIT=10");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var selfLink = json.GetProperty("self").GetString()!;

        // Links should use lowercase "limit"
        selfLink.Should().Contain("limit=");
    }
}
