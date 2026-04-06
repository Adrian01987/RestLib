using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RestLib.Abstractions;
using RestLib.Configuration;
using RestLib.Pagination;
using RestLib.Tests.Fakes;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Tests for disabling pagination links via configuration.
/// </summary>
public class PaginationLinksDisabledTests : IDisposable
{
    private readonly IHost _host;
    private readonly HttpClient _client;
    private readonly PaginationTestRepository _repository;

    public PaginationLinksDisabledTests()
    {
        _repository = new PaginationTestRepository();

        _host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRestLib(options =>
                    {
                        options.IncludePaginationLinks = false; // Disabled
                    });
                        services.AddSingleton<IRepository<ProductEntity, Guid>>(_repository);
                        services.AddRouting();
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapRestLib<ProductEntity, Guid>("/api/products", config =>
                      {
                          config.AllowAnonymous();
                      });
                    });
                    });
            })
            .Build();

        _host.Start();
        _client = _host.GetTestClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _host.Dispose();
    }

    [Fact]
    [Trait("Category", "Story4.2")]
    public async Task GetAll_WhenLinksDisabled_SelfLinkIsOmitted()
    {
        // Arrange
        _repository.SeedMany(5);

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
        _repository.SeedMany(5);

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
        _repository.SeedMany(25);

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
        _repository.SeedMany(5);

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
        _repository.SeedMany(25);

        // Act
        var response = await _client.GetAsync("/api/products?limit=10");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("items").GetArrayLength().Should().Be(10);
    }
}
