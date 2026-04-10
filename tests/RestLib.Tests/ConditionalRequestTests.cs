using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using RestLib.Caching;
using RestLib.Tests.Fakes;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Tests for Story 5.2: Conditional Requests
/// Verifies If-Match and If-None-Match header handling per RFC 9110.
/// </summary>
[Trait("Type", "Integration")]
[Trait("Feature", "ConditionalRequests")]
public class ConditionalRequestTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private ProductEntityRepository _repository = null!;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _repository = new ProductEntityRepository();

        (_host, _client) = await new TestHostBuilder<ProductEntity, Guid>(_repository, "/api/products")
            .WithOptions(options => options.EnableETagSupport = true)
            .WithEndpoint(config => config.AllowAnonymous())
            .BuildAsync();
    }

    #region If-None-Match - 304 Not Modified

    [Fact]
    public async Task GetById_WithMatchingIfNoneMatch_Returns_304_NotModified()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.Seed(
            new ProductEntity { Id = id, ProductName = "Product", UnitPrice = 10.00m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true }
        );

        // First GET to obtain ETag
        var firstResponse = await _client.GetAsync($"/api/products/{id}");
        var etag = firstResponse.Headers.ETag!.Tag;

        // Second GET with If-None-Match
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/products/{id}");
        request.Headers.TryAddWithoutValidation("If-None-Match", etag);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotModified);
    }

    [Fact]
    public async Task GetById_WithMatchingIfNoneMatch_Returns_ETag_Header()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.Seed(
            new ProductEntity { Id = id, ProductName = "Product", UnitPrice = 10.00m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true }
        );

        var firstResponse = await _client.GetAsync($"/api/products/{id}");
        var etag = firstResponse.Headers.ETag!.Tag;

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/products/{id}");
        request.Headers.TryAddWithoutValidation("If-None-Match", etag);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotModified);
        response.Headers.ETag.Should().NotBeNull();
        response.Headers.ETag!.Tag.Should().Be(etag);
    }

    [Fact]
    public async Task GetById_WithNonMatchingIfNoneMatch_Returns_200_WithContent()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.Seed(
            new ProductEntity { Id = id, ProductName = "Product", UnitPrice = 10.00m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true }
        );

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/products/{id}");
        request.Headers.TryAddWithoutValidation("If-None-Match", "\"different-etag\"");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetById_WithWildcardIfNoneMatch_Returns_304_NotModified()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.Seed(
            new ProductEntity { Id = id, ProductName = "Product", UnitPrice = 10.00m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true }
        );

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/products/{id}");
        request.Headers.TryAddWithoutValidation("If-None-Match", "*");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotModified);
    }

    [Fact]
    public async Task GetById_WithoutIfNoneMatch_Returns_200()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.Seed(
            new ProductEntity { Id = id, ProductName = "Product", UnitPrice = 10.00m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true }
        );

        // Act
        var response = await _client.GetAsync($"/api/products/{id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetById_NotFound_WithIfNoneMatch_Returns_404()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/products/{nonExistentId}");
        request.Headers.TryAddWithoutValidation("If-None-Match", "\"some-etag\"");

        // Act
        var response = await _client.SendAsync(request);

        // Assert - 404 takes precedence
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region Multiple ETags Support

    [Fact]
    public async Task GetById_WithMultipleIfNoneMatchETags_Returns_304_WhenOneMatches()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.Seed(
            new ProductEntity { Id = id, ProductName = "Product", UnitPrice = 10.00m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true }
        );

        var firstResponse = await _client.GetAsync($"/api/products/{id}");
        var etag = firstResponse.Headers.ETag!.Tag;

        // Include multiple ETags, one of which matches
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/products/{id}");
        request.Headers.TryAddWithoutValidation("If-None-Match", $"\"wrong-etag-1\", {etag}, \"wrong-etag-2\"");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotModified);
    }

    [Fact]
    public async Task GetById_WithMultipleIfNoneMatchETags_Returns_200_WhenNoneMatch()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.Seed(
            new ProductEntity { Id = id, ProductName = "Product", UnitPrice = 10.00m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true }
        );

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/products/{id}");
        request.Headers.TryAddWithoutValidation("If-None-Match", "\"wrong-etag-1\", \"wrong-etag-2\", \"wrong-etag-3\"");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Update_WithMultipleIfMatchETags_Succeeds_WhenOneMatches()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.Seed(
            new ProductEntity { Id = id, ProductName = "Product", UnitPrice = 10.00m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true }
        );

        var getResponse = await _client.GetAsync($"/api/products/{id}");
        var etag = getResponse.Headers.ETag!.Tag;

        var updatedProduct = new { product_name = "Updated", unit_price = 20.00, stock_quantity = 10, is_active = true };

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/products/{id}")
        {
            Content = JsonContent.Create(updatedProduct)
        };
        // Include multiple ETags, one of which matches
        request.Headers.TryAddWithoutValidation("If-Match", $"\"wrong-etag-1\", {etag}, \"wrong-etag-2\"");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Update_WithMultipleIfMatchETags_Returns_412_WhenNoneMatch()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.Seed(
            new ProductEntity { Id = id, ProductName = "Product", UnitPrice = 10.00m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true }
        );

        var updatedProduct = new { product_name = "Updated", unit_price = 20.00, stock_quantity = 10, is_active = true };

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/products/{id}")
        {
            Content = JsonContent.Create(updatedProduct)
        };
        request.Headers.TryAddWithoutValidation("If-Match", "\"wrong-etag-1\", \"wrong-etag-2\", \"wrong-etag-3\"");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
    }

    #endregion

    #region Weak ETag Support

    [Fact]
    public async Task GetById_WithWeakIfNoneMatch_Returns_304_WhenOpaqueTagMatches()
    {
        // Arrange - RFC 9110 specifies weak comparison for If-None-Match on GET
        var id = Guid.NewGuid();
        _repository.Seed(
            new ProductEntity { Id = id, ProductName = "Product", UnitPrice = 10.00m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true }
        );

        var firstResponse = await _client.GetAsync($"/api/products/{id}");
        var strongETag = firstResponse.Headers.ETag!.Tag;
        var weakETag = $"W/{strongETag}";

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/products/{id}");
        request.Headers.TryAddWithoutValidation("If-None-Match", weakETag);

        // Act
        var response = await _client.SendAsync(request);

        // Assert - Weak comparison should match
        response.StatusCode.Should().Be(HttpStatusCode.NotModified);
    }

    [Fact]
    public async Task Update_WithWeakIfMatch_Returns_412_PerStrongComparison()
    {
        // Arrange - RFC 9110: If-Match uses strong comparison, weak ETags should not match
        var id = Guid.NewGuid();
        _repository.Seed(
            new ProductEntity { Id = id, ProductName = "Product", UnitPrice = 10.00m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true }
        );

        var getResponse = await _client.GetAsync($"/api/products/{id}");
        var strongETag = getResponse.Headers.ETag!.Tag;
        var weakETag = $"W/{strongETag}";

        var updatedProduct = new { product_name = "Updated", unit_price = 20.00, stock_quantity = 10, is_active = true };

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/products/{id}")
        {
            Content = JsonContent.Create(updatedProduct)
        };
        request.Headers.TryAddWithoutValidation("If-Match", weakETag);

        // Act
        var response = await _client.SendAsync(request);

        // Assert - Strong comparison should fail for weak ETags
        response.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
    }

    #endregion

    #region If-Match Behavior (from Story 5.1)

    [Fact]
    public async Task Update_WithMatchingIfMatch_Succeeds()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.Seed(
            new ProductEntity { Id = id, ProductName = "Product", UnitPrice = 10.00m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true }
        );

        var getResponse = await _client.GetAsync($"/api/products/{id}");
        var etag = getResponse.Headers.ETag!.Tag;

        var updatedProduct = new { product_name = "Updated", unit_price = 20.00, stock_quantity = 10, is_active = true };

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/products/{id}")
        {
            Content = JsonContent.Create(updatedProduct)
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Patch_WithMatchingIfMatch_Succeeds()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.Seed(
            new ProductEntity { Id = id, ProductName = "Product", UnitPrice = 10.00m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true }
        );

        var getResponse = await _client.GetAsync($"/api/products/{id}");
        var etag = getResponse.Headers.ETag!.Tag;

        var patchContent = new StringContent(
            "{\"product_name\": \"Patched\"}",
            System.Text.Encoding.UTF8,
            "application/merge-patch+json");

        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/products/{id}")
        {
            Content = patchContent
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Patch_WithMultipleIfMatchETags_Succeeds_WhenOneMatches()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.Seed(
            new ProductEntity { Id = id, ProductName = "Product", UnitPrice = 10.00m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true }
        );

        var getResponse = await _client.GetAsync($"/api/products/{id}");
        var etag = getResponse.Headers.ETag!.Tag;

        var patchContent = new StringContent(
            "{\"product_name\": \"Patched\"}",
            System.Text.Encoding.UTF8,
            "application/merge-patch+json");

        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/products/{id}")
        {
            Content = patchContent
        };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"wrong-1\", {etag}, \"wrong-2\"");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion

    #region Delete If-Match Behavior

    [Fact]
    public async Task Delete_WithMatchingIfMatch_Returns_204()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.Seed(
            new ProductEntity { Id = id, ProductName = "Product", UnitPrice = 10.00m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true }
        );

        var getResponse = await _client.GetAsync($"/api/products/{id}");
        var etag = getResponse.Headers.ETag!.Tag;

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/products/{id}");
        request.Headers.TryAddWithoutValidation("If-Match", etag);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Delete_WithNonMatchingIfMatch_Returns_412()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.Seed(
            new ProductEntity { Id = id, ProductName = "Product", UnitPrice = 10.00m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true }
        );

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/products/{id}");
        request.Headers.TryAddWithoutValidation("If-Match", "\"wrong-etag\"");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
    }

    [Fact]
    public async Task Delete_WithoutIfMatch_Returns_204()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.Seed(
            new ProductEntity { Id = id, ProductName = "Product", UnitPrice = 10.00m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true }
        );

        // Act
        var response = await _client.DeleteAsync($"/api/products/{id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Delete_NotFound_WithIfMatch_Returns_404()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();
        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/products/{nonExistentId}");
        request.Headers.TryAddWithoutValidation("If-Match", "\"some-etag\"");

        // Act
        var response = await _client.SendAsync(request);

        // Assert — 404 takes precedence
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_WithWildcardIfMatch_Returns_204()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.Seed(
            new ProductEntity { Id = id, ProductName = "Product", UnitPrice = 10.00m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true }
        );

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/products/{id}");
        request.Headers.TryAddWithoutValidation("If-Match", "*");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Delete_WithWeakIfMatch_Returns_412_PerStrongComparison()
    {
        // Arrange — RFC 9110: If-Match uses strong comparison, weak ETags should not match
        var id = Guid.NewGuid();
        _repository.Seed(
            new ProductEntity { Id = id, ProductName = "Product", UnitPrice = 10.00m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true }
        );

        var getResponse = await _client.GetAsync($"/api/products/{id}");
        var strongETag = getResponse.Headers.ETag!.Tag;
        var weakETag = $"W/{strongETag}";

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/products/{id}");
        request.Headers.TryAddWithoutValidation("If-Match", weakETag);

        // Act
        var response = await _client.SendAsync(request);

        // Assert — Strong comparison should fail for weak ETags
        response.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
    }

    [Fact]
    public async Task Delete_WithMultipleIfMatchETags_Returns_204_WhenOneMatches()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.Seed(
            new ProductEntity { Id = id, ProductName = "Product", UnitPrice = 10.00m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true }
        );

        var getResponse = await _client.GetAsync($"/api/products/{id}");
        var etag = getResponse.Headers.ETag!.Tag;

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/products/{id}");
        request.Headers.TryAddWithoutValidation("If-Match", $"\"wrong-1\", {etag}, \"wrong-2\"");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Delete_WithMultipleIfMatchETags_Returns_412_WhenNoneMatch()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.Seed(
            new ProductEntity { Id = id, ProductName = "Product", UnitPrice = 10.00m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true }
        );

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/products/{id}");
        request.Headers.TryAddWithoutValidation("If-Match", "\"wrong-etag-1\", \"wrong-etag-2\", \"wrong-etag-3\"");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
    }

    #endregion

    #region ETag Disabled

    [Fact]
    public async Task GetById_WithIfNoneMatch_WhenETagDisabled_IgnoresHeader()
    {
        // Arrange
        var (disabledHost, disabledClient) = await new TestHostBuilder<ProductEntity, Guid>(_repository, "/api/products")
            .WithOptions(options => options.EnableETagSupport = false)
            .WithEndpoint(config => config.AllowAnonymous())
            .BuildAsync();
        using var _ = disabledHost;

        var id = Guid.NewGuid();
        _repository.Seed(
            new ProductEntity { Id = id, ProductName = "Product", UnitPrice = 10.00m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true }
        );

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/products/{id}");
        request.Headers.TryAddWithoutValidation("If-None-Match", "*");

        // Act
        var response = await disabledClient.SendAsync(request);

        // Assert - Should return 200, not 304
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Delete_WithIfMatch_WhenETagDisabled_IgnoresHeader()
    {
        // Arrange
        var (disabledHost, disabledClient) = await new TestHostBuilder<ProductEntity, Guid>(_repository, "/api/products")
            .WithOptions(options => options.EnableETagSupport = false)
            .WithEndpoint(config => config.AllowAnonymous())
            .BuildAsync();
        using var _ = disabledHost;

        var id = Guid.NewGuid();
        _repository.Seed(
            new ProductEntity { Id = id, ProductName = "Product", UnitPrice = 10.00m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true }
        );

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/products/{id}");
        request.Headers.TryAddWithoutValidation("If-Match", "\"wrong-etag\"");

        // Act
        var response = await disabledClient.SendAsync(request);

        // Assert — Should return 204, not 412 (If-Match ignored when ETags disabled)
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    #endregion

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }
}

/// <summary>
/// Unit tests for ETagComparer helper class
/// </summary>
[Trait("Type", "Unit")]
[Trait("Feature", "ConditionalRequests")]
public class ETagComparerTests
{
    #region IfMatchSucceeds Tests

    [Fact]
    public void IfMatchSucceeds_WithEmptyHeader_ReturnsTrue()
    {
        // Arrange
        var headerValues = Microsoft.Extensions.Primitives.StringValues.Empty;

        // Act
        var result = ETagComparer.IfMatchSucceeds(headerValues, "\"abc123\"");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IfMatchSucceeds_WithWildcard_ReturnsTrue()
    {
        // Arrange
        var headerValues = new Microsoft.Extensions.Primitives.StringValues("*");

        // Act
        var result = ETagComparer.IfMatchSucceeds(headerValues, "\"abc123\"");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IfMatchSucceeds_WithMatchingETag_ReturnsTrue()
    {
        // Arrange
        var headerValues = new Microsoft.Extensions.Primitives.StringValues("\"abc123\"");

        // Act
        var result = ETagComparer.IfMatchSucceeds(headerValues, "\"abc123\"");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IfMatchSucceeds_WithNonMatchingETag_ReturnsFalse()
    {
        // Arrange
        var headerValues = new Microsoft.Extensions.Primitives.StringValues("\"different\"");

        // Act
        var result = ETagComparer.IfMatchSucceeds(headerValues, "\"abc123\"");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IfMatchSucceeds_WithMultipleETags_OneMatches_ReturnsTrue()
    {
        // Arrange
        var headerValues = new Microsoft.Extensions.Primitives.StringValues("\"etag1\", \"abc123\", \"etag3\"");

        // Act
        var result = ETagComparer.IfMatchSucceeds(headerValues, "\"abc123\"");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IfMatchSucceeds_WithMultipleETags_NoneMatch_ReturnsFalse()
    {
        // Arrange
        var headerValues = new Microsoft.Extensions.Primitives.StringValues("\"etag1\", \"etag2\", \"etag3\"");

        // Act
        var result = ETagComparer.IfMatchSucceeds(headerValues, "\"abc123\"");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IfMatchSucceeds_WithWeakETag_UsesStrongComparison_ReturnsFalse()
    {
        // Arrange - Strong comparison should fail for weak ETags
        var headerValues = new Microsoft.Extensions.Primitives.StringValues("W/\"abc123\"");

        // Act
        var result = ETagComparer.IfMatchSucceeds(headerValues, "\"abc123\"");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IfMatchSucceeds_BothWeakETags_UsesStrongComparison_ReturnsFalse()
    {
        // Arrange - Strong comparison fails for weak ETags even if they match
        var headerValues = new Microsoft.Extensions.Primitives.StringValues("W/\"abc123\"");

        // Act
        var result = ETagComparer.IfMatchSucceeds(headerValues, "W/\"abc123\"");

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region IfNoneMatchSucceeds Tests

    [Fact]
    public void IfNoneMatchSucceeds_WithEmptyHeader_ReturnsTrue()
    {
        // Arrange
        var headerValues = Microsoft.Extensions.Primitives.StringValues.Empty;

        // Act
        var result = ETagComparer.IfNoneMatchSucceeds(headerValues, "\"abc123\"");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IfNoneMatchSucceeds_WithWildcard_ReturnsFalse()
    {
        // Arrange - Wildcard means "if resource exists", so should return 304
        var headerValues = new Microsoft.Extensions.Primitives.StringValues("*");

        // Act
        var result = ETagComparer.IfNoneMatchSucceeds(headerValues, "\"abc123\"");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IfNoneMatchSucceeds_WithMatchingETag_ReturnsFalse()
    {
        // Arrange
        var headerValues = new Microsoft.Extensions.Primitives.StringValues("\"abc123\"");

        // Act
        var result = ETagComparer.IfNoneMatchSucceeds(headerValues, "\"abc123\"");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IfNoneMatchSucceeds_WithNonMatchingETag_ReturnsTrue()
    {
        // Arrange
        var headerValues = new Microsoft.Extensions.Primitives.StringValues("\"different\"");

        // Act
        var result = ETagComparer.IfNoneMatchSucceeds(headerValues, "\"abc123\"");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IfNoneMatchSucceeds_WithMultipleETags_OneMatches_ReturnsFalse()
    {
        // Arrange
        var headerValues = new Microsoft.Extensions.Primitives.StringValues("\"etag1\", \"abc123\", \"etag3\"");

        // Act
        var result = ETagComparer.IfNoneMatchSucceeds(headerValues, "\"abc123\"");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IfNoneMatchSucceeds_WithMultipleETags_NoneMatch_ReturnsTrue()
    {
        // Arrange
        var headerValues = new Microsoft.Extensions.Primitives.StringValues("\"etag1\", \"etag2\", \"etag3\"");

        // Act
        var result = ETagComparer.IfNoneMatchSucceeds(headerValues, "\"abc123\"");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IfNoneMatchSucceeds_WithWeakETag_UsesWeakComparison_ReturnsFalse()
    {
        // Arrange - Weak comparison should match strong and weak ETags with same opaque-tag
        var headerValues = new Microsoft.Extensions.Primitives.StringValues("W/\"abc123\"");

        // Act
        var result = ETagComparer.IfNoneMatchSucceeds(headerValues, "\"abc123\"");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IfNoneMatchSucceeds_StrongETagAgainstWeak_UsesWeakComparison_ReturnsFalse()
    {
        // Arrange - Weak comparison matches regardless of weak indicator
        var headerValues = new Microsoft.Extensions.Primitives.StringValues("\"abc123\"");

        // Act
        var result = ETagComparer.IfNoneMatchSucceeds(headerValues, "W/\"abc123\"");

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Multiple Header Values

    [Fact]
    public void IfMatchSucceeds_WithMultipleHeaderValues_ParsesCorrectly()
    {
        // Arrange - Multiple header values (not comma-separated within one value)
        var headerValues = new Microsoft.Extensions.Primitives.StringValues(new[] { "\"etag1\"", "\"abc123\"" });

        // Act
        var result = ETagComparer.IfMatchSucceeds(headerValues, "\"abc123\"");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IfNoneMatchSucceeds_WithMultipleHeaderValues_ParsesCorrectly()
    {
        // Arrange
        var headerValues = new Microsoft.Extensions.Primitives.StringValues(new[] { "\"etag1\"", "\"abc123\"" });

        // Act
        var result = ETagComparer.IfNoneMatchSucceeds(headerValues, "\"abc123\"");

        // Assert
        result.Should().BeFalse();
    }

    #endregion
}
