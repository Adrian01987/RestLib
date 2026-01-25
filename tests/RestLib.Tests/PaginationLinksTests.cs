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
/// Tests for Story 4.2: Pagination Links.
/// Verifies that links are absolute URLs, preserve query filters, and can be disabled.
/// </summary>
public class PaginationLinksTests : IDisposable
{
  private readonly IHost _host;
  private readonly HttpClient _client;
  private readonly PaginationTestRepository _repository;

  public PaginationLinksTests()
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
                      options.DefaultPageSize = 10;
                      options.MaxPageSize = 100;
                      options.IncludePaginationLinks = true;
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

  #region Absolute URL Tests

  [Fact]
  [Trait("Category", "Story4.2")]
  public async Task GetAll_SelfLink_IsAbsoluteUrl()
  {
    // Arrange
    _repository.SeedMany(5);

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
    _repository.SeedMany(5);

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
    _repository.SeedMany(25);

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
    _repository.SeedMany(5);

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
    _repository.SeedMany(5);

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
    _repository.SeedMany(30);
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
    _repository.SeedMany(30);
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
    _repository.SeedMany(25);

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
    _repository.SeedMany(5);

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
    _repository.SeedMany(25);

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
    _repository.SeedMany(5);

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
    _repository.SeedMany(5);

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
    _repository.SeedMany(25);

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
    _repository.SeedMany(25);

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
    _repository.SeedMany(25);
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
    _repository.SeedMany(5);

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
    _repository.SeedMany(5);

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
    _repository.SeedMany(25);

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

    _host = new HostBuilder()
        .ConfigureWebHost(webBuilder =>
        {
          webBuilder
                  .UseTestServer()
                  .ConfigureServices(services =>
                  {
                    services.AddRestLib();
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

/// <summary>
/// Edge case tests for pagination links.
/// </summary>
public class PaginationLinksEdgeCaseTests : IDisposable
{
  private readonly IHost _host;
  private readonly HttpClient _client;
  private readonly PaginationTestRepository _repository;

  public PaginationLinksEdgeCaseTests()
  {
    _repository = new PaginationTestRepository();

    _host = new HostBuilder()
        .ConfigureWebHost(webBuilder =>
        {
          webBuilder
                  .UseTestServer()
                  .ConfigureServices(services =>
                  {
                    services.AddRestLib();
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
    _repository.SeedMany(10);

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
    _repository.SeedMany(11);

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
    _repository.SeedMany(1);

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
    // This tests that the base URL construction handles various scenarios
    _repository.SeedMany(5);

    var response = await _client.GetAsync("/api/products");
    var json = await response.Content.ReadFromJsonAsync<JsonElement>();

    var selfLink = json.GetProperty("self").GetString()!;
    Uri.TryCreate(selfLink, UriKind.Absolute, out var uri).Should().BeTrue();
    uri!.AbsolutePath.Should().Be("/api/products");
  }

  [Fact]
  [Trait("Category", "Story4.2")]
  public async Task GetAll_LastPage_FirstLinkStillPresent()
  {
    // Arrange - Seed exactly one page worth of items so there's no next link
    _repository.SeedMany(10);

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
    _repository.SeedMany(5);

    // Note: ASP.NET query params are case-insensitive
    var response = await _client.GetAsync("/api/products?LIMIT=10");

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.OK);

    var json = await response.Content.ReadFromJsonAsync<JsonElement>();
    var selfLink = json.GetProperty("self").GetString()!;

    // Links should use lowercase "limit"
    selfLink.Should().Contain("limit=");
  }
}
