using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RestLib.Abstractions;
using RestLib.Configuration;
using RestLib.Hypermedia;
using RestLib.InMemory;
using RestLib.Tests.Fakes;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Entity for HATEOAS integration tests.
/// </summary>
public class HateoasEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>
/// A custom HATEOAS link provider for testing the extensibility point.
/// </summary>
public class TestHateoasLinkProvider : IHateoasLinkProvider<HateoasEntity, Guid>
{
    /// <inheritdoc />
    public IReadOnlyDictionary<string, HateoasLink>? GetLinks(HateoasEntity entity, Guid key)
    {
        return new Dictionary<string, HateoasLink>
        {
            ["related"] = new HateoasLink { Href = $"/api/related/{key}" },
            ["reviews"] = new HateoasLink { Href = $"/api/items/{key}/reviews" }
        };
    }
}

/// <summary>
/// A custom link provider that overrides a standard link relation (self) for testing precedence.
/// </summary>
public class OverridingLinkProvider : IHateoasLinkProvider<HateoasEntity, Guid>
{
    /// <inheritdoc />
    public IReadOnlyDictionary<string, HateoasLink>? GetLinks(HateoasEntity entity, Guid key)
    {
        return new Dictionary<string, HateoasLink>
        {
            ["self"] = new HateoasLink { Href = $"/custom/self/{key}" },
            ["custom"] = new HateoasLink { Href = $"/custom/{key}", Method = "POST" }
        };
    }
}

/// <summary>
/// Integration tests for HATEOAS hypermedia links (Story 19).
/// </summary>
[Trait("Type", "Integration")]
[Trait("Feature", "HATEOAS")]
public class HateoasTests : IAsyncLifetime
{
    private readonly InMemoryRepository<HateoasEntity, Guid> _repository;
    private readonly Guid _knownId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private readonly Guid _secondId = Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff");
    private IHost? _host;
    private HttpClient? _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="HateoasTests"/> class.
    /// </summary>
    public HateoasTests()
    {
        _repository = new InMemoryRepository<HateoasEntity, Guid>(
            e => e.Id,
            Guid.NewGuid);
    }

    /// <inheritdoc />
    public Task InitializeAsync() => Task.CompletedTask;

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_host is not null)
        {
            await _host.StopAsync();
        }

        _host?.Dispose();
    }

    private static StringContent BatchJson(object payload)
    {
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private static void AssertHasLinksObject(JsonElement json)
    {
        json.TryGetProperty("_links", out var links).Should().BeTrue("entity should have _links object");
        links.ValueKind.Should().Be(JsonValueKind.Object);
    }

    private static void AssertLinkHref(JsonElement linksElement, string rel, string expectedContains)
    {
        linksElement.TryGetProperty(rel, out var link).Should().BeTrue($"_links should have '{rel}' relation");
        link.GetProperty("href").GetString().Should().Contain(expectedContains);
    }

    private static void AssertLinkMethod(JsonElement linksElement, string rel, string expectedMethod)
    {
        var link = linksElement.GetProperty(rel);
        link.GetProperty("method").GetString().Should().Be(expectedMethod);
    }

    private static void AssertNoLinkMethod(JsonElement linksElement, string rel)
    {
        var link = linksElement.GetProperty(rel);
        link.TryGetProperty("method", out _).Should().BeFalse($"'{rel}' link should not have a method (GET is implied)");
    }

    private async Task CreateHostAsync(
        Action<RestLibEndpointConfiguration<HateoasEntity, Guid>>? configureEndpoint = null,
        Action<RestLibOptions>? configureOptions = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var builder = new TestHostBuilder<HateoasEntity, Guid>(_repository, "/api/items")
            .WithEndpoint(configureEndpoint ?? (config => config.AllowAnonymous()))
            .WithOptions(configureOptions ?? (opts => opts.EnableHateoas = true));

        if (configureServices is not null)
        {
            builder.WithServices(configureServices);
        }

        (_host, _client) = await builder.BuildAsync();
    }

    private void SeedData()
    {
        _repository.Seed([
            new HateoasEntity
            {
                Id = _knownId,
                Name = "Keyboard",
                Price = 49.99m,
                IsActive = true
            },
            new HateoasEntity
            {
                Id = _secondId,
                Name = "Mouse",
                Price = 29.99m,
                IsActive = true
            }
        ]);
    }

    // ──────────────────────── Disabled by default ────────────────────────

    #region HATEOAS Disabled by Default

    [Fact]
    [Trait("Category", "Story19")]
    public async Task GetById_HateoasDisabled_NoLinksInResponse()
    {
        // Arrange
        SeedData();
        await CreateHostAsync(
            configureEndpoint: config => config.AllowAnonymous(),
            configureOptions: opts => { /* EnableHateoas defaults to false */ });

        // Act
        var response = await _client!.GetAsync($"/api/items/{_knownId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.TryGetProperty("_links", out _).Should().BeFalse("HATEOAS is disabled by default");
        json.TryGetProperty("name", out _).Should().BeTrue("entity properties should still be present");
    }

    [Fact]
    [Trait("Category", "Story19")]
    public async Task GetAll_HateoasDisabled_NoLinksInItems()
    {
        // Arrange
        SeedData();
        await CreateHostAsync(
            configureEndpoint: config => config.AllowAnonymous(),
            configureOptions: opts => { });

        // Act
        var response = await _client!.GetAsync("/api/items");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().BeGreaterThan(0);
        for (int i = 0; i < items.GetArrayLength(); i++)
        {
            items[i].TryGetProperty("_links", out _).Should().BeFalse("HATEOAS is disabled by default");
        }
    }

    [Fact]
    [Trait("Category", "Story19")]
    public async Task Create_HateoasDisabled_NoLinksInResponse()
    {
        // Arrange
        await CreateHostAsync(
            configureEndpoint: config => config.AllowAnonymous(),
            configureOptions: opts => { });
        var payload = new StringContent(
            JsonSerializer.Serialize(new { name = "Widget", price = 9.99, is_active = true }),
            Encoding.UTF8, "application/json");

        // Act
        var response = await _client!.PostAsync("/api/items", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.TryGetProperty("_links", out _).Should().BeFalse("HATEOAS is disabled by default");
    }

    #endregion

    // ──────────────────────── GetById ────────────────────────

    #region GetById with HATEOAS

    [Fact]
    [Trait("Category", "Story19")]
    public async Task GetById_HateoasEnabled_ReturnsLinksObject()
    {
        // Arrange
        SeedData();
        await CreateHostAsync(config => config.AllowAnonymous());

        // Act
        var response = await _client!.GetAsync($"/api/items/{_knownId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Entity properties should still be present
        json.GetProperty("name").GetString().Should().Be("Keyboard");
        json.GetProperty("price").GetDecimal().Should().Be(49.99m);

        // _links should be present
        AssertHasLinksObject(json);
        var links = json.GetProperty("_links");

        // self link
        AssertLinkHref(links, "self", $"/api/items/{_knownId}");
        AssertNoLinkMethod(links, "self");

        // collection link
        AssertLinkHref(links, "collection", "/api/items");
        AssertNoLinkMethod(links, "collection");

        // CRUD links
        AssertLinkHref(links, "update", $"/api/items/{_knownId}");
        AssertLinkMethod(links, "update", "PUT");

        AssertLinkHref(links, "patch", $"/api/items/{_knownId}");
        AssertLinkMethod(links, "patch", "PATCH");

        AssertLinkHref(links, "delete", $"/api/items/{_knownId}");
        AssertLinkMethod(links, "delete", "DELETE");
    }

    [Fact]
    [Trait("Category", "Story19")]
    public async Task GetById_HateoasEnabled_SelfLinkIsAbsoluteUrl()
    {
        // Arrange
        SeedData();
        await CreateHostAsync(config => config.AllowAnonymous());

        // Act
        var response = await _client!.GetAsync($"/api/items/{_knownId}");

        // Assert
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var links = json.GetProperty("_links");
        var selfHref = links.GetProperty("self").GetProperty("href").GetString()!;
        selfHref.Should().StartWith("http", "links should be absolute URLs");
        selfHref.Should().Contain($"/api/items/{_knownId}");
    }

    #endregion

    // ──────────────────────── GetById with Field Selection ────────────────────────

    #region GetById with Field Selection + HATEOAS

    [Fact]
    [Trait("Category", "Story19")]
    public async Task GetById_WithFieldSelection_HateoasEnabled_LinksStillPresent()
    {
        // Arrange
        SeedData();
        await CreateHostAsync(config =>
        {
            config.AllowAnonymous();
            config.AllowFieldSelection(e => e.Id, e => e.Name, e => e.Price);
        });

        // Act
        var response = await _client!.GetAsync($"/api/items/{_knownId}?fields=id,name");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Selected fields present
        json.TryGetProperty("id", out _).Should().BeTrue();
        json.TryGetProperty("name", out _).Should().BeTrue();

        // Unselected fields absent
        json.TryGetProperty("price", out _).Should().BeFalse();

        // _links still injected
        AssertHasLinksObject(json);
        var links = json.GetProperty("_links");
        AssertLinkHref(links, "self", $"/api/items/{_knownId}");
    }

    #endregion

    // ──────────────────────── GetAll ────────────────────────

    #region GetAll with HATEOAS

    [Fact]
    [Trait("Category", "Story19")]
    public async Task GetAll_HateoasEnabled_EachItemHasLinks()
    {
        // Arrange
        SeedData();
        await CreateHostAsync(config => config.AllowAnonymous());

        // Act
        var response = await _client!.GetAsync("/api/items");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(2);

        for (int i = 0; i < items.GetArrayLength(); i++)
        {
            var item = items[i];
            AssertHasLinksObject(item);
            var links = item.GetProperty("_links");
            links.TryGetProperty("self", out _).Should().BeTrue();
            links.TryGetProperty("collection", out _).Should().BeTrue();
        }
    }

    [Fact]
    [Trait("Category", "Story19")]
    public async Task GetAll_HateoasEnabled_CollectionWrapperPreserved()
    {
        // Arrange
        SeedData();
        await CreateHostAsync(config => config.AllowAnonymous());

        // Act
        var response = await _client!.GetAsync("/api/items");

        // Assert
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.TryGetProperty("items", out _).Should().BeTrue();
        json.TryGetProperty("self", out _).Should().BeTrue();
        json.TryGetProperty("first", out _).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Story19")]
    public async Task GetAll_HateoasEnabled_ItemLinksHaveCorrectEntityUrls()
    {
        // Arrange
        SeedData();
        await CreateHostAsync(config => config.AllowAnonymous());

        // Act
        var response = await _client!.GetAsync("/api/items");

        // Assert
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");

        // Each item's self link should contain that item's id
        for (int i = 0; i < items.GetArrayLength(); i++)
        {
            var item = items[i];
            var id = item.GetProperty("id").GetString();
            var selfHref = item.GetProperty("_links").GetProperty("self").GetProperty("href").GetString()!;
            selfHref.Should().Contain($"/api/items/{id}");
        }
    }

    #endregion

    // ──────────────────────── GetAll with Field Selection ────────────────────────

    #region GetAll with Field Selection + HATEOAS

    [Fact]
    [Trait("Category", "Story19")]
    public async Task GetAll_WithFieldSelection_HateoasEnabled_ItemsHaveLinks()
    {
        // Arrange
        SeedData();
        await CreateHostAsync(config =>
        {
            config.AllowAnonymous();
            config.AllowFieldSelection(e => e.Id, e => e.Name);
        });

        // Act
        var response = await _client!.GetAsync("/api/items?fields=id,name");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(2);

        for (int i = 0; i < items.GetArrayLength(); i++)
        {
            var item = items[i];
            // Selected fields present
            item.TryGetProperty("id", out _).Should().BeTrue();
            item.TryGetProperty("name", out _).Should().BeTrue();

            // Unselected fields absent
            item.TryGetProperty("price", out _).Should().BeFalse();

            // _links still injected
            AssertHasLinksObject(item);
            var links = item.GetProperty("_links");
            AssertLinkHref(links, "self", "/api/items/");
        }
    }

    #endregion

    // ──────────────────────── Create ────────────────────────

    #region Create with HATEOAS

    [Fact]
    [Trait("Category", "Story19")]
    public async Task Create_HateoasEnabled_ResponseHasLinks()
    {
        // Arrange
        await CreateHostAsync(config => config.AllowAnonymous());
        var payload = new StringContent(
            JsonSerializer.Serialize(new { name = "Widget", price = 19.99, is_active = true }),
            Encoding.UTF8, "application/json");

        // Act
        var response = await _client!.PostAsync("/api/items", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Entity properties present
        json.GetProperty("name").GetString().Should().Be("Widget");

        // _links present
        AssertHasLinksObject(json);
        var links = json.GetProperty("_links");
        AssertLinkHref(links, "self", "/api/items/");
        AssertLinkHref(links, "collection", "/api/items");
        AssertLinkHref(links, "update", "/api/items/");
        AssertLinkMethod(links, "update", "PUT");
    }

    [Fact]
    [Trait("Category", "Story19")]
    public async Task Create_HateoasEnabled_SelfLinkMatchesLocationHeader()
    {
        // Arrange
        await CreateHostAsync(config => config.AllowAnonymous());
        var payload = new StringContent(
            JsonSerializer.Serialize(new { name = "Gadget", price = 5.99, is_active = true }),
            Encoding.UTF8, "application/json");

        // Act
        var response = await _client!.PostAsync("/api/items", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var selfHref = json.GetProperty("_links").GetProperty("self").GetProperty("href").GetString()!;

        // The Location header and self link should reference the same resource
        var locationHeader = response.Headers.Location?.ToString();
        locationHeader.Should().NotBeNull();
        selfHref.Should().Contain(locationHeader!.TrimStart('/'));
    }

    #endregion

    // ──────────────────────── Update ────────────────────────

    #region Update with HATEOAS

    [Fact]
    [Trait("Category", "Story19")]
    public async Task Update_HateoasEnabled_ResponseHasLinks()
    {
        // Arrange
        SeedData();
        await CreateHostAsync(config => config.AllowAnonymous());
        var payload = new StringContent(
            JsonSerializer.Serialize(new { id = _knownId, name = "Updated Keyboard", price = 59.99, is_active = true }),
            Encoding.UTF8, "application/json");

        // Act
        var response = await _client!.PutAsync($"/api/items/{_knownId}", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        json.GetProperty("name").GetString().Should().Be("Updated Keyboard");
        AssertHasLinksObject(json);
        var links = json.GetProperty("_links");
        AssertLinkHref(links, "self", $"/api/items/{_knownId}");
        AssertLinkHref(links, "collection", "/api/items");
    }

    #endregion

    // ──────────────────────── Patch ────────────────────────

    #region Patch with HATEOAS

    [Fact]
    [Trait("Category", "Story19")]
    public async Task Patch_HateoasEnabled_ResponseHasLinks()
    {
        // Arrange
        SeedData();
        await CreateHostAsync(config => config.AllowAnonymous());
        var payload = new StringContent(
            JsonSerializer.Serialize(new { name = "Patched Keyboard" }),
            Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/items/{_knownId}")
        {
            Content = payload
        };

        // Act
        var response = await _client!.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        AssertHasLinksObject(json);
        var links = json.GetProperty("_links");
        AssertLinkHref(links, "self", $"/api/items/{_knownId}");
        AssertLinkHref(links, "collection", "/api/items");
    }

    #endregion

    // ──────────────────────── Disabled Operations ────────────────────────

    #region Disabled Operations Omit Links

    [Fact]
    [Trait("Category", "Story19")]
    public async Task GetById_DeleteDisabled_NoDeleteLink()
    {
        // Arrange
        SeedData();
        await CreateHostAsync(config =>
        {
            config.AllowAnonymous();
            config.ExcludeOperations(RestLibOperation.Delete);
        });

        // Act
        var response = await _client!.GetAsync($"/api/items/{_knownId}");

        // Assert
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var links = json.GetProperty("_links");
        links.TryGetProperty("self", out _).Should().BeTrue();
        links.TryGetProperty("update", out _).Should().BeTrue();
        links.TryGetProperty("patch", out _).Should().BeTrue();
        links.TryGetProperty("delete", out _).Should().BeFalse("delete operation is disabled");
    }

    [Fact]
    [Trait("Category", "Story19")]
    public async Task GetById_UpdateAndPatchDisabled_NoUpdateOrPatchLinks()
    {
        // Arrange
        SeedData();
        await CreateHostAsync(config =>
        {
            config.AllowAnonymous();
            config.ExcludeOperations(RestLibOperation.Update, RestLibOperation.Patch);
        });

        // Act
        var response = await _client!.GetAsync($"/api/items/{_knownId}");

        // Assert
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var links = json.GetProperty("_links");
        links.TryGetProperty("self", out _).Should().BeTrue();
        links.TryGetProperty("collection", out _).Should().BeTrue();
        links.TryGetProperty("update", out _).Should().BeFalse("update operation is disabled");
        links.TryGetProperty("patch", out _).Should().BeFalse("patch operation is disabled");
        links.TryGetProperty("delete", out _).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Story19")]
    public async Task GetById_GetAllDisabled_NoCollectionLink()
    {
        // Arrange
        SeedData();
        await CreateHostAsync(config =>
        {
            config.AllowAnonymous();
            config.ExcludeOperations(RestLibOperation.GetAll);
        });

        // Act
        var response = await _client!.GetAsync($"/api/items/{_knownId}");

        // Assert
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var links = json.GetProperty("_links");
        links.TryGetProperty("self", out _).Should().BeTrue();
        links.TryGetProperty("collection", out _).Should().BeFalse("GetAll operation is disabled");
    }

    [Fact]
    [Trait("Category", "Story19")]
    public async Task GetById_OnlyGetByIdEnabled_OnlySelfLink()
    {
        // Arrange
        SeedData();
        await CreateHostAsync(config =>
        {
            config.AllowAnonymous();
            config.IncludeOperations(RestLibOperation.GetById);
        });

        // Act
        var response = await _client!.GetAsync($"/api/items/{_knownId}");

        // Assert
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var links = json.GetProperty("_links");
        links.TryGetProperty("self", out _).Should().BeTrue("self link is always present");
        links.TryGetProperty("collection", out _).Should().BeFalse();
        links.TryGetProperty("update", out _).Should().BeFalse();
        links.TryGetProperty("patch", out _).Should().BeFalse();
        links.TryGetProperty("delete", out _).Should().BeFalse();
    }

    #endregion

    // ──────────────────────── Custom Link Provider ────────────────────────

    #region Custom IHateoasLinkProvider

    [Fact]
    [Trait("Category", "Story19")]
    public async Task GetById_WithCustomLinkProvider_IncludesCustomLinks()
    {
        // Arrange
        SeedData();
        await CreateHostAsync(
            configureEndpoint: config => config.AllowAnonymous(),
            configureServices: services =>
                services.AddHateoasLinkProvider<HateoasEntity, Guid, TestHateoasLinkProvider>());

        // Act
        var response = await _client!.GetAsync($"/api/items/{_knownId}");

        // Assert
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var links = json.GetProperty("_links");

        // Standard links still present
        AssertLinkHref(links, "self", $"/api/items/{_knownId}");
        AssertLinkHref(links, "collection", "/api/items");

        // Custom links present
        AssertLinkHref(links, "related", $"/api/related/{_knownId}");
        AssertLinkHref(links, "reviews", $"/api/items/{_knownId}/reviews");
    }

    [Fact]
    [Trait("Category", "Story19")]
    public async Task GetAll_WithCustomLinkProvider_EachItemHasCustomLinks()
    {
        // Arrange
        SeedData();
        await CreateHostAsync(
            configureEndpoint: config => config.AllowAnonymous(),
            configureServices: services =>
                services.AddHateoasLinkProvider<HateoasEntity, Guid, TestHateoasLinkProvider>());

        // Act
        var response = await _client!.GetAsync("/api/items");

        // Assert
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        for (int i = 0; i < items.GetArrayLength(); i++)
        {
            var item = items[i];
            var links = item.GetProperty("_links");
            links.TryGetProperty("related", out _).Should().BeTrue();
            links.TryGetProperty("reviews", out _).Should().BeTrue();
        }
    }

    [Fact]
    [Trait("Category", "Story19")]
    public async Task GetById_CustomLinkOverridesStandard_CustomTakesPrecedence()
    {
        // Arrange
        SeedData();
        await CreateHostAsync(
            configureEndpoint: config => config.AllowAnonymous(),
            configureServices: services =>
                services.AddHateoasLinkProvider<HateoasEntity, Guid, OverridingLinkProvider>());

        // Act
        var response = await _client!.GetAsync($"/api/items/{_knownId}");

        // Assert
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var links = json.GetProperty("_links");

        // The custom "self" link should override the standard one
        var selfHref = links.GetProperty("self").GetProperty("href").GetString()!;
        selfHref.Should().Be($"/custom/self/{_knownId}");

        // Custom link with method
        AssertLinkHref(links, "custom", $"/custom/{_knownId}");
        AssertLinkMethod(links, "custom", "POST");
    }

    [Fact]
    [Trait("Category", "Story19")]
    public async Task Create_WithCustomLinkProvider_ResponseHasCustomLinks()
    {
        // Arrange
        await CreateHostAsync(
            configureEndpoint: config => config.AllowAnonymous(),
            configureServices: services =>
                services.AddHateoasLinkProvider<HateoasEntity, Guid, TestHateoasLinkProvider>());
        var payload = new StringContent(
            JsonSerializer.Serialize(new { name = "Custom Widget", price = 15.00, is_active = true }),
            Encoding.UTF8, "application/json");

        // Act
        var response = await _client!.PostAsync("/api/items", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var links = json.GetProperty("_links");
        links.TryGetProperty("related", out _).Should().BeTrue();
        links.TryGetProperty("reviews", out _).Should().BeTrue();
    }

    #endregion

    // ──────────────────────── Batch Operations ────────────────────────

    #region Batch Create with HATEOAS

    [Fact]
    [Trait("Category", "Story19")]
    public async Task BatchCreate_HateoasEnabled_ItemEntitiesHaveLinks()
    {
        // Arrange
        await CreateHostAsync(config =>
        {
            config.AllowAnonymous();
            config.EnableBatch();
        });

        var payload = new
        {
            action = "create",
            items = new[]
            {
                new { name = "Batch Item 1", price = 10.00m, is_active = true },
                new { name = "Batch Item 2", price = 20.00m, is_active = true }
            }
        };

        // Act
        var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(2);

        for (int i = 0; i < items.GetArrayLength(); i++)
        {
            var entity = items[i].GetProperty("entity");
            AssertHasLinksObject(entity);
            var links = entity.GetProperty("_links");
            links.TryGetProperty("self", out _).Should().BeTrue();
            links.TryGetProperty("collection", out _).Should().BeTrue();
        }
    }

    [Fact]
    [Trait("Category", "Story19")]
    public async Task BatchCreate_HateoasEnabled_LinksPointToCollectionPath()
    {
        // Arrange
        await CreateHostAsync(config =>
        {
            config.AllowAnonymous();
            config.EnableBatch();
        });

        var payload = new
        {
            action = "create",
            items = new[]
            {
                new { name = "Linked Item", price = 5.00m, is_active = true }
            }
        };

        // Act
        var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

        // Assert
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var entity = json.GetProperty("items")[0].GetProperty("entity");
        var links = entity.GetProperty("_links");

        // Self link should be an absolute URL under the collection path
        var selfHref = links.GetProperty("self").GetProperty("href").GetString()!;
        selfHref.Should().StartWith("http");
        selfHref.Should().Contain("/api/items/");

        // Collection link should be the collection URL (not batch URL)
        var collectionHref = links.GetProperty("collection").GetProperty("href").GetString()!;
        collectionHref.Should().Contain("/api/items");
        collectionHref.Should().NotContain("/batch");
    }

    #endregion

    #region Batch Update with HATEOAS

    [Fact]
    [Trait("Category", "Story19")]
    public async Task BatchUpdate_HateoasEnabled_ItemEntitiesHaveLinks()
    {
        // Arrange
        SeedData();
        await CreateHostAsync(config =>
        {
            config.AllowAnonymous();
            config.EnableBatch();
        });

        var payload = new
        {
            action = "update",
            items = new[]
            {
                new { id = _knownId, body = new { name = "Updated 1", price = 99.99m, is_active = true } },
                new { id = _secondId, body = new { name = "Updated 2", price = 88.88m, is_active = false } }
            }
        };

        // Act
        var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");

        for (int i = 0; i < items.GetArrayLength(); i++)
        {
            var batchItem = items[i];
            if (batchItem.GetProperty("status").GetInt32() is >= 200 and < 300)
            {
                var entity = batchItem.GetProperty("entity");
                AssertHasLinksObject(entity);
                var links = entity.GetProperty("_links");
                links.TryGetProperty("self", out _).Should().BeTrue();
            }
        }
    }

    #endregion

    #region Batch Patch with HATEOAS

    [Fact]
    [Trait("Category", "Story19")]
    public async Task BatchPatch_HateoasEnabled_ItemEntitiesHaveLinks()
    {
        // Arrange
        SeedData();
        await CreateHostAsync(config =>
        {
            config.AllowAnonymous();
            config.EnableBatch();
        });

        var payload = new
        {
            action = "patch",
            items = new object[]
            {
                new { id = _knownId, body = new { name = "Patched 1" } },
                new { id = _secondId, body = new { name = "Patched 2" } }
            }
        };

        // Act
        var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");

        for (int i = 0; i < items.GetArrayLength(); i++)
        {
            var batchItem = items[i];
            if (batchItem.GetProperty("status").GetInt32() is >= 200 and < 300)
            {
                var entity = batchItem.GetProperty("entity");
                AssertHasLinksObject(entity);
            }
        }
    }

    #endregion

    #region Batch with HATEOAS Disabled

    [Fact]
    [Trait("Category", "Story19")]
    public async Task BatchCreate_HateoasDisabled_NoLinksInEntities()
    {
        // Arrange
        await CreateHostAsync(
            configureEndpoint: config =>
            {
                config.AllowAnonymous();
                config.EnableBatch();
            },
            configureOptions: opts => { });

        var payload = new
        {
            action = "create",
            items = new[]
            {
                new { name = "No Links Item", price = 5.00m, is_active = true }
            }
        };

        // Act
        var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var entity = json.GetProperty("items")[0].GetProperty("entity");
        entity.TryGetProperty("_links", out _).Should().BeFalse("HATEOAS is disabled");
    }

    #endregion

    #region Batch with Custom Link Provider

    [Fact]
    [Trait("Category", "Story19")]
    public async Task BatchCreate_WithCustomLinkProvider_EntitiesHaveCustomLinks()
    {
        // Arrange
        await CreateHostAsync(
            configureEndpoint: config =>
            {
                config.AllowAnonymous();
                config.EnableBatch();
            },
            configureServices: services =>
                services.AddHateoasLinkProvider<HateoasEntity, Guid, TestHateoasLinkProvider>());

        var payload = new
        {
            action = "create",
            items = new[]
            {
                new { name = "Custom Batch Item", price = 7.50m, is_active = true }
            }
        };

        // Act
        var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var entity = json.GetProperty("items")[0].GetProperty("entity");
        var links = entity.GetProperty("_links");
        links.TryGetProperty("related", out _).Should().BeTrue();
        links.TryGetProperty("reviews", out _).Should().BeTrue();
    }

    #endregion
}
