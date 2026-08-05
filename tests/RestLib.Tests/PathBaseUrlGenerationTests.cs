using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RestLib.Abstractions;
using RestLib.Configuration;
using RestLib.InMemory;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Integration tests for application-aware URL generation under mounted paths and proxies.
/// </summary>
[Trait("Type", "Integration")]
[Trait("Feature", "URL Generation")]
public class PathBaseUrlGenerationTests
{
    private const string MountedPathBase = "/gateway";
    private static readonly Guid KnownId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    [Fact]
    public async Task GetAll_MountedPathBase_PaginationLinksIncludePathBaseAndRemainNavigable()
    {
        // Arrange
        var repository = CreateRepository();
        repository.Seed(
        [
            new MountedItem { Id = KnownId, Name = "First", Details = "One" },
            new MountedItem { Id = Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff"), Name = "Second", Details = "Two" },
            new MountedItem { Id = Guid.Parse("cccccccc-dddd-eeee-ffff-aaaaaaaaaaaa"), Name = "Third", Details = "Three" }
        ]);
        var (host, client) = await CreateDirectHostAsync(repository);
        using var hostHandle = host;
        using var clientHandle = client;

        // Act
        var response = await client.GetAsync($"{MountedPathBase}/api/items?limit=1&tag=featured");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var self = json.GetProperty("self").GetString();
        var first = json.GetProperty("first").GetString();
        var next = json.GetProperty("next").GetString();

        self.Should().Be("http://localhost/gateway/api/items?limit=1&tag=featured");
        first.Should().Be("http://localhost/gateway/api/items?limit=1&tag=featured");
        next.Should().NotBeNull();

        var nextUri = new Uri(next!);
        nextUri.GetLeftPart(UriPartial.Path).Should().Be("http://localhost/gateway/api/items");
        nextUri.Query.Should().StartWith("?cursor=");
        nextUri.Query.Should().Contain("&limit=1&tag=featured");

        // Act
        var nextResponse = await client.GetAsync(nextUri.PathAndQuery);

        // Assert
        nextResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var nextJson = await nextResponse.Content.ReadFromJsonAsync<JsonElement>();
        nextJson.GetProperty("items").GetArrayLength().Should().Be(1);
        nextJson.GetProperty("self").GetString().Should().Be(next);
    }

    [Fact]
    public async Task GetById_MountedPathBase_FullHateoasLinksIncludePathBase()
    {
        // Arrange
        var repository = CreateRepository();
        repository.Seed([new MountedItem { Id = KnownId, Name = "Mounted", Details = "Full" }]);
        var (host, client) = await CreateDirectHostAsync(repository);
        using var hostHandle = host;
        using var clientHandle = client;

        // Act
        var response = await client.GetAsync($"{MountedPathBase}/api/items/{KnownId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var links = json.GetProperty("_links");
        var entityUrl = $"http://localhost/gateway/api/items/{KnownId}";
        var collectionUrl = "http://localhost/gateway/api/items";

        GetHref(links, "self").Should().Be(entityUrl);
        GetHref(links, "collection").Should().Be(collectionUrl);
        GetHref(links, "update").Should().Be(entityUrl);
        GetHref(links, "patch").Should().Be(entityUrl);
        GetHref(links, "delete").Should().Be(entityUrl);
    }

    [Fact]
    public async Task GetAll_MountedPathBase_FieldSelectedHateoasLinksIncludePathBase()
    {
        // Arrange
        var repository = CreateRepository();
        repository.Seed([new MountedItem { Id = KnownId, Name = "Projected", Details = "Hidden" }]);
        var (host, client) = await CreateDirectHostAsync(repository);
        using var hostHandle = host;
        using var clientHandle = client;

        // Act
        var response = await client.GetAsync($"{MountedPathBase}/api/items?fields=id,name&limit=10");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var item = json.GetProperty("items")[0];
        item.GetProperty("id").GetGuid().Should().Be(KnownId);
        item.GetProperty("name").GetString().Should().Be("Projected");
        item.TryGetProperty("details", out _).Should().BeFalse();

        var links = item.GetProperty("_links");
        GetHref(links, "self").Should().Be($"http://localhost/gateway/api/items/{KnownId}");
        GetHref(links, "collection").Should().Be("http://localhost/gateway/api/items");
    }

    [Fact]
    public async Task Create_MountedPathBase_LocationAndSelfLinkIncludePathBaseAndRemainNavigable()
    {
        // Arrange
        var repository = CreateRepository();
        var (host, client) = await CreateDirectHostAsync(repository);
        using var hostHandle = host;
        using var clientHandle = client;

        // Act
        var response = await client.PostAsJsonAsync(
            $"{MountedPathBase}/api/items",
            new { name = "Created", details = "Mounted" });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = json.GetProperty("id").GetGuid();
        var expectedLocation = $"/gateway/api/items/{id}";
        var expectedSelf = $"http://localhost/gateway/api/items/{id}";

        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().Be(expectedLocation);
        GetHref(json.GetProperty("_links"), "self").Should().Be(expectedSelf);

        // Act
        var locationResponse = await client.GetAsync(response.Headers.Location);
        var selfResponse = await client.GetAsync(new Uri(expectedSelf).PathAndQuery);

        // Assert
        locationResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        selfResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetAll_ForwardedOriginAndPrefix_GeneratedLinksUseExternalRequestValues()
    {
        // Arrange
        var repository = CreateRepository();
        repository.Seed([new MountedItem { Id = KnownId, Name = "Proxied", Details = "External" }]);
        var (host, client) = await CreateDirectHostAsync(repository, useForwardedHeaders: true);
        using var hostHandle = host;
        using var clientHandle = client;
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/items?limit=1");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Host", "public.example.test");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Prefix", "/edge/app");

        // Act
        var response = await client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("self").GetString()
            .Should().Be("https://public.example.test/edge/app/api/items?limit=1");
        json.GetProperty("first").GetString()
            .Should().Be("https://public.example.test/edge/app/api/items?limit=1");

        var itemLinks = json.GetProperty("items")[0].GetProperty("_links");
        GetHref(itemLinks, "self")
            .Should().Be($"https://public.example.test/edge/app/api/items/{KnownId}");
        GetHref(itemLinks, "collection")
            .Should().Be("https://public.example.test/edge/app/api/items");
    }

    [Fact]
    public async Task MappedCreate_MountedPathBase_LocationAndSelfLinkIncludePathBase()
    {
        // Arrange
        var repository = new InMemoryRepository<MappedDbItem, Guid>(item => item.Id, Guid.NewGuid);
        var (host, client) = await CreateMappedHostAsync(repository);
        using var hostHandle = host;
        using var clientHandle = client;

        // Act
        var response = await client.PostAsJsonAsync(
            $"{MountedPathBase}/api/mapped-items",
            new { name = "Mapped" });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = json.GetProperty("id").GetGuid();
        var expectedLocation = $"/gateway/api/mapped-items/{id}";
        var expectedSelf = $"http://localhost/gateway/api/mapped-items/{id}";

        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().Be(expectedLocation);
        GetHref(json.GetProperty("_links"), "self").Should().Be(expectedSelf);

        // Act
        var followResponse = await client.GetAsync(response.Headers.Location);

        // Assert
        followResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static InMemoryRepository<MountedItem, Guid> CreateRepository()
    {
        return new InMemoryRepository<MountedItem, Guid>(item => item.Id, Guid.NewGuid);
    }

    private static string GetHref(JsonElement links, string relation)
    {
        return links.GetProperty(relation).GetProperty("href").GetString()!;
    }

    private static void ConfigureOptions(RestLibOptions options)
    {
        options.DefaultPageSize = 1;
        options.MaxPageSize = 10;
        options.IncludePaginationLinks = true;
        options.EnableHateoas = true;
    }

    private static async Task<(IHost Host, HttpClient Client)> CreateDirectHostAsync(
        InMemoryRepository<MountedItem, Guid> repository,
        bool useForwardedHeaders = false)
    {
        var host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRestLib(ConfigureOptions);
                        services.AddSingleton<IRepository<MountedItem, Guid>>(repository);
                        services.AddRouting();
                    })
                    .Configure(app =>
                    {
                        if (useForwardedHeaders)
                        {
                            var forwardedOptions = new ForwardedHeadersOptions
                            {
                                ForwardedHeaders = ForwardedHeaders.XForwardedHost |
                                    ForwardedHeaders.XForwardedProto |
                                    ForwardedHeaders.XForwardedPrefix
                            };
                            forwardedOptions.KnownProxies.Clear();
                            forwardedOptions.KnownIPNetworks.Clear();
                            app.UseForwardedHeaders(forwardedOptions);
                        }
                        else
                        {
                            app.UsePathBase(MountedPathBase);
                        }

                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapRestLib<MountedItem, Guid>("/api/items", config =>
                            {
                                config.AllowAnonymous();
                                config.AllowFieldSelection(item => item.Id, item => item.Name, item => item.Details);
                            });
                        });
                    });
            })
            .Build();

        await host.StartAsync();
        return (host, host.GetTestClient());
    }

    private static async Task<(IHost Host, HttpClient Client)> CreateMappedHostAsync(
        InMemoryRepository<MappedDbItem, Guid> repository)
    {
        var host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRestLib(ConfigureOptions);
                        services.AddSingleton<IRepository<MappedDbItem, Guid>>(repository);
                        services.AddRestLibMapper<MappedApiItem, MappedDbItem>(_ => new MappedItemMapper());
                        services.AddRouting();
                    })
                    .Configure(app =>
                    {
                        app.UsePathBase(MountedPathBase);
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapRestLib<MappedApiItem, MappedDbItem, Guid>(
                                "/api/mapped-items",
                                config => config.AllowAnonymous());
                        });
                    });
            })
            .Build();

        await host.StartAsync();
        return (host, host.GetTestClient());
    }

    private sealed class MountedItem
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Details { get; set; } = string.Empty;
    }

    private sealed class MappedApiItem
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class MappedDbItem
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class MappedItemMapper : IRestLibMapper<MappedApiItem, MappedDbItem>
    {
        public MappedApiItem ToApi(MappedDbItem dbModel)
        {
            return new MappedApiItem
            {
                Id = dbModel.Id,
                Name = dbModel.Name
            };
        }

        public MappedDbItem ToDb(MappedApiItem apiModel)
        {
            return new MappedDbItem
            {
                Id = apiModel.Id,
                Name = apiModel.Name
            };
        }
    }
}
