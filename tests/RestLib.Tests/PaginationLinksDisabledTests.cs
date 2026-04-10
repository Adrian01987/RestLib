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
/// Tests for disabling pagination links via configuration.
/// </summary>
[Trait("Type", "Integration")]
[Trait("Feature", "Pagination")]
public class PaginationLinksDisabledTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private InMemoryRepository<ProductEntity, Guid> _repository = null!;

    public async Task InitializeAsync()
    {
        _repository = new InMemoryRepository<ProductEntity, Guid>(e => e.Id, Guid.NewGuid);

        (_host, _client) = await new TestHostBuilder<ProductEntity, Guid>(_repository, "/api/products")
            .WithOptions(options => options.IncludePaginationLinks = false)
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
    public async Task GetAll_WhenLinksDisabled_SelfLinkIsOmitted()
    {
        // Arrange
        _repository.SeedProducts(5);

        // Act
        var response = await _client.GetAsync("/api/products");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.TryGetProperty("self", out _).Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Story4.2")]
    public async Task GetAll_WhenLinksDisabled_FirstLinkIsOmitted()
    {
        // Arrange
        _repository.SeedProducts(5);

        // Act
        var response = await _client.GetAsync("/api/products");

        // Assert
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.TryGetProperty("first", out _).Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Story4.2")]
    public async Task GetAll_WhenLinksDisabled_NextLinkIsOmitted()
    {
        // Arrange
        _repository.SeedProducts(25);

        // Act
        var response = await _client.GetAsync("/api/products?limit=10");

        // Assert
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.TryGetProperty("next", out _).Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Story4.2")]
    public async Task GetAll_WhenLinksDisabled_ItemsStillReturned()
    {
        // Arrange
        _repository.SeedProducts(5);

        // Act
        var response = await _client.GetAsync("/api/products");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.TryGetProperty("items", out var items).Should().BeTrue();
        items.GetArrayLength().Should().Be(5);
    }

    [Fact]
    [Trait("Category", "Story4.2")]
    public async Task GetAll_WhenLinksDisabled_PaginationStillWorks()
    {
        // Arrange
        _repository.SeedProducts(25);

        // Act
        var response = await _client.GetAsync("/api/products?limit=10");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("items").GetArrayLength().Should().Be(10);
    }
}
