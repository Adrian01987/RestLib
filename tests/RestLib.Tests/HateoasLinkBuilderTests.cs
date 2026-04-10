using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using RestLib.Configuration;
using RestLib.Hypermedia;
using RestLib.Tests.Fakes;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Unit tests for <see cref="HateoasLinkBuilder"/> and <see cref="HateoasHelper"/>.
/// </summary>
[Trait("Type", "Unit")]
[Trait("Feature", "HATEOAS")]
public class HateoasLinkBuilderTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private static HttpRequest CreateFakeRequest(string scheme = "https", string host = "api.example.com")
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = scheme;
        context.Request.Host = new HostString(host);
        return context.Request;
    }

    private static RestLibEndpointConfiguration<TestEntity, Guid> CreateConfig(
        Action<RestLibEndpointConfiguration<TestEntity, Guid>>? configure = null)
    {
        var config = new RestLibEndpointConfiguration<TestEntity, Guid>();
        configure?.Invoke(config);
        return config;
    }

    // ──────────────────────── BuildEntityLinks ────────────────────────

    #region BuildEntityLinks

    [Fact]
    [Trait("Category", "Story19")]
    public void BuildEntityLinks_AllOperationsEnabled_ReturnsAllLinks()
    {
        // Arrange
        var request = CreateFakeRequest();
        var config = CreateConfig();
        var entityKey = Guid.NewGuid();

        // Act
        var links = HateoasLinkBuilder.BuildEntityLinks(request, "/api/items", entityKey, config);

        // Assert
        links.Should().ContainKey("self");
        links.Should().ContainKey("collection");
        links.Should().ContainKey("update");
        links.Should().ContainKey("patch");
        links.Should().ContainKey("delete");
        links.Should().HaveCount(5);
    }

    [Fact]
    [Trait("Category", "Story19")]
    public void BuildEntityLinks_SelfLink_IsAbsoluteUrlWithEntityKey()
    {
        // Arrange
        var request = CreateFakeRequest();
        var config = CreateConfig();
        var entityKey = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        // Act
        var links = HateoasLinkBuilder.BuildEntityLinks(request, "/api/items", entityKey, config);

        // Assert
        links["self"].Href.Should().Be($"https://api.example.com/api/items/{entityKey}");
        links["self"].Method.Should().BeNull("GET is implied per HAL convention");
    }

    [Fact]
    [Trait("Category", "Story19")]
    public void BuildEntityLinks_CollectionLink_IsAbsoluteUrlWithoutEntityKey()
    {
        // Arrange
        var request = CreateFakeRequest();
        var config = CreateConfig();
        var entityKey = Guid.NewGuid();

        // Act
        var links = HateoasLinkBuilder.BuildEntityLinks(request, "/api/items", entityKey, config);

        // Assert
        links["collection"].Href.Should().Be("https://api.example.com/api/items");
        links["collection"].Method.Should().BeNull("GET is implied");
    }

    [Fact]
    [Trait("Category", "Story19")]
    public void BuildEntityLinks_CrudLinks_HaveCorrectMethods()
    {
        // Arrange
        var request = CreateFakeRequest();
        var config = CreateConfig();
        var entityKey = Guid.NewGuid();

        // Act
        var links = HateoasLinkBuilder.BuildEntityLinks(request, "/api/items", entityKey, config);

        // Assert
        links["update"].Method.Should().Be("PUT");
        links["patch"].Method.Should().Be("PATCH");
        links["delete"].Method.Should().Be("DELETE");
    }

    [Fact]
    [Trait("Category", "Story19")]
    public void BuildEntityLinks_DeleteDisabled_NoDeleteLink()
    {
        // Arrange
        var request = CreateFakeRequest();
        var config = CreateConfig(c => c.ExcludeOperations(RestLibOperation.Delete));
        var entityKey = Guid.NewGuid();

        // Act
        var links = HateoasLinkBuilder.BuildEntityLinks(request, "/api/items", entityKey, config);

        // Assert
        links.Should().ContainKey("self");
        links.Should().ContainKey("collection");
        links.Should().ContainKey("update");
        links.Should().ContainKey("patch");
        links.Should().NotContainKey("delete");
    }

    [Fact]
    [Trait("Category", "Story19")]
    public void BuildEntityLinks_OnlyGetByIdEnabled_OnlySelfLink()
    {
        // Arrange
        var request = CreateFakeRequest();
        var config = CreateConfig(c => c.IncludeOperations(RestLibOperation.GetById));
        var entityKey = Guid.NewGuid();

        // Act
        var links = HateoasLinkBuilder.BuildEntityLinks(request, "/api/items", entityKey, config);

        // Assert
        links.Should().ContainKey("self");
        links.Should().NotContainKey("collection");
        links.Should().NotContainKey("update");
        links.Should().NotContainKey("patch");
        links.Should().NotContainKey("delete");
        links.Should().HaveCount(1);
    }

    [Fact]
    [Trait("Category", "Story19")]
    public void BuildEntityLinks_CustomLinks_AreMerged()
    {
        // Arrange
        var request = CreateFakeRequest();
        var config = CreateConfig();
        var entityKey = Guid.NewGuid();
        var customLinks = new Dictionary<string, HateoasLink>
        {
            ["related"] = new HateoasLink { Href = "/api/related/123" },
            ["reviews"] = new HateoasLink { Href = "/api/reviews", Method = "POST" }
        };

        // Act
        var links = HateoasLinkBuilder.BuildEntityLinks(
            request, "/api/items", entityKey, config, customLinks);

        // Assert
        links.Should().ContainKey("related");
        links["related"].Href.Should().Be("/api/related/123");
        links.Should().ContainKey("reviews");
        links["reviews"].Method.Should().Be("POST");
        // Standard links still present
        links.Should().ContainKey("self");
        links.Should().ContainKey("collection");
    }

    [Fact]
    [Trait("Category", "Story19")]
    public void BuildEntityLinks_CustomLinkOverridesStandard_CustomWins()
    {
        // Arrange
        var request = CreateFakeRequest();
        var config = CreateConfig();
        var entityKey = Guid.NewGuid();
        var customLinks = new Dictionary<string, HateoasLink>
        {
            ["self"] = new HateoasLink { Href = "/custom/self" }
        };

        // Act
        var links = HateoasLinkBuilder.BuildEntityLinks(
            request, "/api/items", entityKey, config, customLinks);

        // Assert
        links["self"].Href.Should().Be("/custom/self");
    }

    [Fact]
    [Trait("Category", "Story19")]
    public void BuildEntityLinks_HttpScheme_LinksUseHttp()
    {
        // Arrange
        var request = CreateFakeRequest(scheme: "http", host: "localhost:5000");
        var config = CreateConfig();
        var entityKey = Guid.NewGuid();

        // Act
        var links = HateoasLinkBuilder.BuildEntityLinks(request, "/api/items", entityKey, config);

        // Assert
        links["self"].Href.Should().StartWith("http://localhost:5000/api/items/");
    }

    #endregion

    // ──────────────────────── GetCollectionPath ────────────────────────

    #region GetCollectionPath

    [Fact]
    [Trait("Category", "Story19")]
    public void GetCollectionPath_CollectionEndpoint_ReturnsPathAsIs()
    {
        // Act
        var result = HateoasLinkBuilder.GetCollectionPath("/api/items", isCollectionEndpoint: true);

        // Assert
        result.Should().Be("/api/items");
    }

    [Fact]
    [Trait("Category", "Story19")]
    public void GetCollectionPath_EntityEndpoint_StripsLastSegment()
    {
        // Act
        var result = HateoasLinkBuilder.GetCollectionPath(
            "/api/items/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", isCollectionEndpoint: false);

        // Assert
        result.Should().Be("/api/items");
    }

    [Fact]
    [Trait("Category", "Story19")]
    public void GetCollectionPath_NestedRoute_StripsOnlyLastSegment()
    {
        // Act
        var result = HateoasLinkBuilder.GetCollectionPath(
            "/api/v2/products/some-id", isCollectionEndpoint: false);

        // Assert
        result.Should().Be("/api/v2/products");
    }

    #endregion

    // ──────────────────────── HateoasHelper.EntityWithLinks ────────────────────────

    #region EntityWithLinks

    [Fact]
    [Trait("Category", "Story19")]
    public void EntityWithLinks_InjectsLinksAlongsideEntityProperties()
    {
        // Arrange
        var entity = new TestEntity { Id = Guid.NewGuid(), Name = "Widget", Price = 9.99m };
        var links = new Dictionary<string, HateoasLink>
        {
            ["self"] = new HateoasLink { Href = "/api/items/123" }
        };

        // Act
        var result = HateoasHelper.EntityWithLinks<TestEntity, Guid>(entity, links, JsonOptions);

        // Assert
        result.Should().ContainKey("id");
        result.Should().ContainKey("name");
        result.Should().ContainKey("price");
        result.Should().ContainKey("_links");

        // Verify _links content
        var linksElement = result["_links"];
        linksElement.GetProperty("self").GetProperty("href").GetString().Should().Be("/api/items/123");
    }

    [Fact]
    [Trait("Category", "Story19")]
    public void EntityWithLinks_UsesSnakeCasePropertyNames()
    {
        // Arrange
        var entity = new ProductEntity
        {
            Id = Guid.NewGuid(),
            ProductName = "Test",
            UnitPrice = 10m,
            StockQuantity = 5,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };
        var links = new Dictionary<string, HateoasLink>
        {
            ["self"] = new HateoasLink { Href = "/test" }
        };

        // Act
        var result = HateoasHelper.EntityWithLinks<ProductEntity, Guid>(entity, links, JsonOptions);

        // Assert
        result.Should().ContainKey("product_name");
        result.Should().ContainKey("unit_price");
        result.Should().ContainKey("stock_quantity");
        result.Should().ContainKey("is_active");
        result.Should().ContainKey("_links");
    }

    #endregion

    // ──────────────────────── HateoasHelper.InjectLinksIntoProjected ────────────────────────

    #region InjectLinksIntoProjected

    [Fact]
    [Trait("Category", "Story19")]
    public void InjectLinksIntoProjected_AddsLinksToExistingDictionary()
    {
        // Arrange
        var projected = new Dictionary<string, JsonElement>
        {
            ["id"] = JsonSerializer.SerializeToElement(Guid.NewGuid()),
            ["name"] = JsonSerializer.SerializeToElement("Widget")
        };
        var links = new Dictionary<string, HateoasLink>
        {
            ["self"] = new HateoasLink { Href = "/api/items/123" }
        };

        // Act
        HateoasHelper.InjectLinksIntoProjected(projected, links, JsonOptions);

        // Assert
        projected.Should().ContainKey("_links");
        projected.Should().ContainKey("id");
        projected.Should().ContainKey("name");
        projected["_links"].GetProperty("self").GetProperty("href").GetString().Should().Be("/api/items/123");
    }

    #endregion

    // ──────────────────────── HateoasLink JSON serialization ────────────────────────

    #region HateoasLink Serialization

    [Fact]
    [Trait("Category", "Story19")]
    public void HateoasLink_MethodNull_OmittedInJson()
    {
        // Arrange
        var link = new HateoasLink { Href = "/api/items/1" };

        // Act
        var json = JsonSerializer.Serialize(link, JsonOptions);
        using var doc = JsonDocument.Parse(json);

        // Assert
        doc.RootElement.GetProperty("href").GetString().Should().Be("/api/items/1");
        doc.RootElement.TryGetProperty("method", out _).Should().BeFalse("null method should be omitted");
    }

    [Fact]
    [Trait("Category", "Story19")]
    public void HateoasLink_MethodPresent_IncludedInJson()
    {
        // Arrange
        var link = new HateoasLink { Href = "/api/items/1", Method = "PUT" };

        // Act
        var json = JsonSerializer.Serialize(link, JsonOptions);
        using var doc = JsonDocument.Parse(json);

        // Assert
        doc.RootElement.GetProperty("href").GetString().Should().Be("/api/items/1");
        doc.RootElement.GetProperty("method").GetString().Should().Be("PUT");
    }

    #endregion
}
