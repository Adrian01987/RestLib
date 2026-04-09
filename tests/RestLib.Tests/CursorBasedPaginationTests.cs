using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using RestLib.Abstractions;
using RestLib.InMemory;
using RestLib.Pagination;
using RestLib.Responses;
using RestLib.Tests.Fakes;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Tests for Story 4.1: Cursor-Based Pagination.
/// Verifies cursor encoding/decoding, limit validation, and proper error handling.
/// </summary>
[Trait("Type", "Integration")]
[Trait("Feature", "Pagination")]
public class CursorBasedPaginationTests : IDisposable
{
    private readonly IHost _host;
    private readonly HttpClient _client;
    private readonly InMemoryRepository<ProductEntity, Guid> _repository;

    public CursorBasedPaginationTests()
    {
        _repository = new InMemoryRepository<ProductEntity, Guid>(e => e.Id, Guid.NewGuid);

        (_host, _client) = new TestHostBuilder<ProductEntity, Guid>(_repository, "/api/products")
            .WithOptions(options =>
            {
                options.DefaultPageSize = 20;
                options.MaxPageSize = 100;
            })
            .WithEndpoint(config => config.AllowAnonymous())
            .Build();
    }

    public void Dispose()
    {
        _client.Dispose();
        _host.Dispose();
    }

    #region Cursor Validation Tests

    [Fact]
    [Trait("Category", "Story4.1")]
    public async Task GetAll_WithNullCursor_ReturnsFirstPage()
    {
        // Arrange
        _repository.SeedProducts(25);

        // Act
        var response = await _client.GetAsync("/api/products");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("items").GetArrayLength().Should().Be(20); // Default page size
    }

    [Fact]
    [Trait("Category", "Story4.1")]
    public async Task GetAll_WithEmptyStringCursor_ReturnsFirstPage()
    {
        // Arrange
        _repository.SeedProducts(25);

        // Act
        var response = await _client.GetAsync("/api/products?cursor=");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Category", "Story4.1")]
    public async Task GetAll_WithValidBase64UrlCursor_ReturnsSuccess()
    {
        // Arrange
        _repository.SeedProducts(50);
        var cursor = CursorEncoder.Encode(Guid.NewGuid());

        // Act
        var response = await _client.GetAsync($"/api/products?cursor={Uri.EscapeDataString(cursor)}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Category", "Story4.1")]
    public async Task GetAll_WithInvalidCursor_Returns400WithProblemDetails()
    {
        // Arrange
        var invalidCursor = "not-a-valid-base64url-cursor";

        // Act
        var response = await _client.GetAsync($"/api/products?cursor={invalidCursor}");

        // Assert
        await response.ShouldBeProblemDetails(
            HttpStatusCode.BadRequest,
            "/problems/invalid-cursor",
            expectedTitle: "Invalid Cursor");
    }

    [Fact]
    [Trait("Category", "Story4.1")]
    public async Task GetAll_WithMalformedBase64Cursor_Returns400()
    {
        // Arrange - base64 with invalid characters
        var malformedCursor = "abc!!!xyz";

        // Act
        var response = await _client.GetAsync($"/api/products?cursor={malformedCursor}");

        // Assert
        await response.ShouldBeProblemDetails(HttpStatusCode.BadRequest, "/problems/invalid-cursor");
    }

    [Fact]
    [Trait("Category", "Story4.1")]
    public async Task GetAll_WithValidBase64ButInvalidStructure_Returns400()
    {
        // Arrange - valid base64 but not a valid cursor structure
        var invalidStructure = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("{\"foo\":\"bar\"}"))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        // Act
        var response = await _client.GetAsync($"/api/products?cursor={invalidStructure}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Story4.1")]
    public async Task GetAll_WithCursorExceedingMaxLength_Returns400WithProblemDetails()
    {
        // Arrange — default MaxCursorLength is 4096; create a cursor string that exceeds it
        var oversizedCursor = new string('A', 4097);

        // Act
        var response = await _client.GetAsync($"/api/products?cursor={oversizedCursor}");

        // Assert
        var problem = await response.ShouldBeProblemDetails(
            HttpStatusCode.BadRequest,
            "/problems/invalid-cursor",
            expectedTitle: "Invalid Cursor");
        problem.Detail.Should().Contain("4096");
    }

    [Fact]
    [Trait("Category", "Story4.1")]
    public async Task GetAll_WithCursorAtCustomMaxLength_IsAccepted()
    {
        // Arrange — a valid cursor well within the default 4096 limit should be accepted
        _repository.SeedProducts(5);
        var validCursor = CursorEncoder.Encode(0);

        // Sanity check: the encoded cursor is well within the default max length
        validCursor.Length.Should().BeLessThan(4096);

        // Act — use the valid cursor
        var response = await _client.GetAsync($"/api/products?cursor={validCursor}&limit=2");

        // Assert — should succeed
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Category", "Story4.1")]
    public async Task GetAll_WithCursorExceedingCustomMaxLength_Returns400()
    {
        // Arrange — build a host with a very small MaxCursorLength
        var repository = new InMemoryRepository<ProductEntity, Guid>(e => e.Id, Guid.NewGuid);
        var (host, client) = new TestHostBuilder<ProductEntity, Guid>(repository, "/api/products")
            .WithOptions(options =>
            {
                options.MaxCursorLength = 10;
            })
            .WithEndpoint(config => config.AllowAnonymous())
            .Build();

        using (host)
        using (client)
        {
            // A cursor longer than 10 characters
            var longCursor = new string('A', 11);

            // Act
            var response = await client.GetAsync($"/api/products?cursor={longCursor}");

            // Assert
            var problem = await response.ShouldBeProblemDetails(
                HttpStatusCode.BadRequest,
                "/problems/invalid-cursor");
            problem.Detail.Should().Contain("10");
        }
    }

    #endregion

    #region Limit Validation Tests

    [Fact]
    [Trait("Category", "Story4.1")]
    public async Task GetAll_WithDefaultLimit_Returns20Items()
    {
        // Arrange
        _repository.SeedProducts(50);

        // Act
        var response = await _client.GetAsync("/api/products");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("items").GetArrayLength().Should().Be(20);
    }

    [Fact]
    [Trait("Category", "Story4.1")]
    public async Task GetAll_WithLimit10_Returns10Items()
    {
        // Arrange
        _repository.SeedProducts(50);

        // Act
        var response = await _client.GetAsync("/api/products?limit=10");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("items").GetArrayLength().Should().Be(10);
    }

    [Fact]
    [Trait("Category", "Story4.1")]
    public async Task GetAll_WithLimit1_Returns1Item()
    {
        // Arrange
        _repository.SeedProducts(50);

        // Act
        var response = await _client.GetAsync("/api/products?limit=1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("items").GetArrayLength().Should().Be(1);
    }

    [Fact]
    [Trait("Category", "Story4.1")]
    public async Task GetAll_WithLimit100_Returns100Items()
    {
        // Arrange
        _repository.SeedProducts(150);

        // Act
        var response = await _client.GetAsync("/api/products?limit=100");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("items").GetArrayLength().Should().Be(100);
    }

    [Fact]
    [Trait("Category", "Story4.1")]
    public async Task GetAll_WithLimitZero_Returns400WithProblemDetails()
    {
        // Act
        var response = await _client.GetAsync("/api/products?limit=0");

        // Assert
        var problem = await response.ShouldBeProblemDetails(
            HttpStatusCode.BadRequest,
            "/problems/invalid-limit",
            expectedTitle: "Invalid Limit");
        problem.Detail.Should().Contain("0").And.Contain("1").And.Contain("100");
    }

    [Fact]
    [Trait("Category", "Story4.1")]
    public async Task GetAll_WithNegativeLimit_Returns400WithProblemDetails()
    {
        // Act
        var response = await _client.GetAsync("/api/products?limit=-5");

        // Assert
        var problem = await response.ShouldBeProblemDetails(
            HttpStatusCode.BadRequest,
            "/problems/invalid-limit");
        problem.Detail.Should().Contain("-5");
    }

    [Fact]
    [Trait("Category", "Story4.1")]
    public async Task GetAll_WithLimit101_Returns400WithProblemDetails()
    {
        // Act
        var response = await _client.GetAsync("/api/products?limit=101");

        // Assert
        var problem = await response.ShouldBeProblemDetails(
            HttpStatusCode.BadRequest,
            "/problems/invalid-limit");
        problem.Detail.Should().Contain("101");
    }

    [Fact]
    [Trait("Category", "Story4.1")]
    public async Task GetAll_WithLimit1000_Returns400WithProblemDetails()
    {
        // Act
        var response = await _client.GetAsync("/api/products?limit=1000");

        // Assert
        await response.ShouldBeProblemDetails(HttpStatusCode.BadRequest, "/problems/invalid-limit");
    }

    #endregion

    #region Cursor Encoding Tests

    [Fact]
    [Trait("Category", "Story4.1")]
    public void CursorEncoder_EncodesGuidToBase64Url()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act
        var cursor = CursorEncoder.Encode(id);

        // Assert
        cursor.Should().NotBeNullOrEmpty();
        // Base64url should not contain + / or =
        cursor.Should().NotContain("+");
        cursor.Should().NotContain("/");
        cursor.Should().NotContain("=");
    }

    [Fact]
    [Trait("Category", "Story4.1")]
    public void CursorEncoder_EncodesIntToBase64Url()
    {
        // Arrange
        var id = 12345;

        // Act
        var cursor = CursorEncoder.Encode(id);

        // Assert
        cursor.Should().NotBeNullOrEmpty();
        cursor.Should().NotContain("+");
        cursor.Should().NotContain("/");
        cursor.Should().NotContain("=");
    }

    [Fact]
    [Trait("Category", "Story4.1")]
    public void CursorEncoder_EncodesStringToBase64Url()
    {
        // Arrange
        var id = "product-123";

        // Act
        var cursor = CursorEncoder.Encode(id);

        // Assert
        cursor.Should().NotBeNullOrEmpty();
        cursor.Should().NotContain("+");
        cursor.Should().NotContain("/");
        cursor.Should().NotContain("=");
    }

    [Fact]
    [Trait("Category", "Story4.1")]
    public void CursorEncoder_DecodesGuidFromBase64Url()
    {
        // Arrange
        var originalId = Guid.NewGuid();
        var cursor = CursorEncoder.Encode(originalId);

        // Act
        var success = CursorEncoder.TryDecode<Guid>(cursor, out var decodedId);

        // Assert
        success.Should().BeTrue();
        decodedId.Should().Be(originalId);
    }

    [Fact]
    [Trait("Category", "Story4.1")]
    public void CursorEncoder_DecodesIntFromBase64Url()
    {
        // Arrange
        var originalId = 12345;
        var cursor = CursorEncoder.Encode(originalId);

        // Act
        var success = CursorEncoder.TryDecode<int>(cursor, out var decodedId);

        // Assert
        success.Should().BeTrue();
        decodedId.Should().Be(originalId);
    }

    [Fact]
    [Trait("Category", "Story4.1")]
    public void CursorEncoder_DecodesStringFromBase64Url()
    {
        // Arrange
        var originalId = "product-123";
        var cursor = CursorEncoder.Encode(originalId);

        // Act
        var success = CursorEncoder.TryDecode<string>(cursor, out var decodedId);

        // Assert
        success.Should().BeTrue();
        decodedId.Should().Be(originalId);
    }

    [Fact]
    [Trait("Category", "Story4.1")]
    public void CursorEncoder_TryDecode_ReturnsFalseForInvalidCursor()
    {
        // Arrange
        var invalidCursor = "not-valid-base64";

        // Act
        var success = CursorEncoder.TryDecode<Guid>(invalidCursor, out var decodedId);

        // Assert
        success.Should().BeFalse();
        decodedId.Should().Be(Guid.Empty);
    }

    [Fact]
    [Trait("Category", "Story4.1")]
    public void CursorEncoder_TryDecode_ReturnsFalseForEmptyString()
    {
        // Act
        var success = CursorEncoder.TryDecode<Guid>("", out var decodedId);

        // Assert
        success.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Story4.1")]
    public void CursorEncoder_TryDecode_ReturnsFalseForNull()
    {
        // Act
        var success = CursorEncoder.TryDecode<Guid>(null!, out var decodedId);

        // Assert
        success.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Story4.1")]
    public void CursorEncoder_IsValid_ReturnsTrueForValidCursor()
    {
        // Arrange
        var cursor = CursorEncoder.Encode(Guid.NewGuid());

        // Act
        var isValid = CursorEncoder.IsValid(cursor);

        // Assert
        isValid.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Story4.1")]
    public void CursorEncoder_IsValid_ReturnsTrueForNullOrEmpty()
    {
        // Act & Assert
        CursorEncoder.IsValid(null).Should().BeTrue();
        CursorEncoder.IsValid("").Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Story4.1")]
    public void CursorEncoder_IsValid_ReturnsFalseForInvalidBase64()
    {
        // Act
        var isValid = CursorEncoder.IsValid("not-valid!!!base64");

        // Assert
        isValid.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Story4.1")]
    public void CursorEncoder_IsValid_ReturnsFalseForValidBase64ButInvalidStructure()
    {
        // Arrange - valid base64 but doesn't have the "v" property
        var invalidStructure = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("{\"foo\":\"bar\"}"))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        // Act
        var isValid = CursorEncoder.IsValid(invalidStructure);

        // Assert
        isValid.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Story4.1")]
    public void CursorEncoder_CursorsAreUrlSafe()
    {
        // Arrange - test with values that would produce + and / in standard base64
        var testValues = new object[]
        {
      Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
      "test/string+with=special",
      999999999999L,
      -1
        };

        foreach (var value in testValues)
        {
            // Act
            var cursor = CursorEncoder.Encode(value);

            // Assert - base64url must not contain these characters
            cursor.Should().NotContain("+", $"Cursor for {value} contains '+'");
            cursor.Should().NotContain("/", $"Cursor for {value} contains '/'");
            cursor.Should().NotContain("=", $"Cursor for {value} contains '='");
        }
    }

    #endregion

    #region Combined Cursor and Limit Tests

    [Fact]
    [Trait("Category", "Story4.1")]
    public async Task GetAll_WithValidCursorAndLimit_ReturnsSuccess()
    {
        // Arrange
        _repository.SeedProducts(50);
        var cursor = CursorEncoder.Encode(Guid.NewGuid());

        // Act
        var response = await _client.GetAsync($"/api/products?cursor={Uri.EscapeDataString(cursor)}&limit=10");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Category", "Story4.1")]
    public async Task GetAll_WithInvalidCursorAndValidLimit_Returns400ForCursor()
    {
        // Arrange - cursor is validated first
        var invalidCursor = "invalid";

        // Act
        var response = await _client.GetAsync($"/api/products?cursor={invalidCursor}&limit=10");

        // Assert
        await response.ShouldBeProblemDetails(HttpStatusCode.BadRequest, "/problems/invalid-cursor");
    }

    [Fact]
    [Trait("Category", "Story4.1")]
    public async Task GetAll_WithValidCursorAndInvalidLimit_Returns400ForLimit()
    {
        // Arrange
        var validCursor = CursorEncoder.Encode(Guid.NewGuid());

        // Act
        var response = await _client.GetAsync($"/api/products?cursor={Uri.EscapeDataString(validCursor)}&limit=0");

        // Assert
        await response.ShouldBeProblemDetails(HttpStatusCode.BadRequest, "/problems/invalid-limit");
    }

    #endregion

    #region Response Structure Tests

    [Fact]
    [Trait("Category", "Story4.1")]
    public async Task GetAll_ResponseContainsPaginationLinks()
    {
        // Arrange
        _repository.SeedProducts(50);

        // Act
        var response = await _client.GetAsync("/api/products?limit=10");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Assert
        json.TryGetProperty("self", out _).Should().BeTrue();
        json.GetProperty("self").GetString().Should().Contain("/api/products");
        json.GetProperty("self").GetString().Should().Contain("limit=10");
    }

    [Fact]
    [Trait("Category", "Story4.1")]
    public async Task GetAll_ResponseHasNextLinkWhenMoreItemsExist()
    {
        // Arrange - seed items that will produce a next cursor
        _repository.SeedProducts(10);

        // Act
        var response = await _client.GetAsync("/api/products?limit=5");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Assert
        json.TryGetProperty("next", out var nextLink).Should().BeTrue();
        nextLink.GetString().Should().Contain("cursor=");
        nextLink.GetString().Should().Contain("limit=5");
    }

    [Fact]
    [Trait("Category", "Story4.1")]
    public async Task GetAll_ResponseOmitsNextLinkWhenNoMoreItems()
    {
        // Arrange - seed fewer items than limit
        _repository.SeedProducts(5);

        // Act
        var response = await _client.GetAsync("/api/products?limit=10");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Assert - when there are no more items, "next" is omitted (null values not serialized)
        json.TryGetProperty("next", out _).Should().BeFalse();
    }

    #endregion

    #region Edge Cases

    [Fact]
    [Trait("Category", "Story4.1")]
    public async Task GetAll_WithEmptyRepository_ReturnsEmptyItems()
    {
        // Arrange - repository is empty

        // Act
        var response = await _client.GetAsync("/api/products");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        json.GetProperty("items").GetArrayLength().Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Story4.1")]
    public async Task GetAll_WithFewerItemsThanLimit_ReturnsAllItems()
    {
        // Arrange
        _repository.SeedProducts(3);

        // Act
        var response = await _client.GetAsync("/api/products?limit=10");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        json.GetProperty("items").GetArrayLength().Should().Be(3);
    }

    [Fact]
    [Trait("Category", "Story4.1")]
    public async Task GetAll_WithExactlyLimitItems_ReturnsAllItems()
    {
        // Arrange
        _repository.SeedProducts(10);

        // Act
        var response = await _client.GetAsync("/api/products?limit=10");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        json.GetProperty("items").GetArrayLength().Should().Be(10);
    }

    [Fact]
    [Trait("Category", "Story4.1")]
    public async Task GetAll_LimitIsEnforcedByMaxPageSize()
    {
        // Arrange - limit exceeds max (should fail validation)
        _repository.SeedProducts(200);

        // Act
        var response = await _client.GetAsync("/api/products?limit=150");

        // Assert
        await response.ShouldBeProblemDetails(HttpStatusCode.BadRequest, "/problems/invalid-limit");
    }

    [Fact]
    [Trait("Category", "Story4.1")]
    public async Task GetAll_CursorWithSpecialCharacters_IsHandledCorrectly()
    {
        // Arrange - cursor that encodes to base64 with special chars that need URL encoding
        var cursor = CursorEncoder.Encode("test/value+special=chars");

        // Act
        var response = await _client.GetAsync($"/api/products?cursor={Uri.EscapeDataString(cursor)}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion
}

/// <summary>
/// Tests for cursor encoding with custom page size configuration.
/// </summary>
[Trait("Type", "Integration")]
[Trait("Feature", "Pagination")]
public class CursorPaginationCustomConfigTests : IDisposable
{
    private readonly IHost _host;
    private readonly HttpClient _client;
    private readonly InMemoryRepository<ProductEntity, Guid> _repository;

    public CursorPaginationCustomConfigTests()
    {
        _repository = new InMemoryRepository<ProductEntity, Guid>(e => e.Id, Guid.NewGuid);

        (_host, _client) = new TestHostBuilder<ProductEntity, Guid>(_repository, "/api/products")
            .WithOptions(options =>
            {
                options.DefaultPageSize = 5;   // Custom default
                options.MaxPageSize = 50;      // Custom max
            })
            .WithEndpoint(config => config.AllowAnonymous())
            .Build();
    }

    public void Dispose()
    {
        _client.Dispose();
        _host.Dispose();
    }

    [Fact]
    [Trait("Category", "Story4.1")]
    public async Task GetAll_UsesCustomDefaultPageSize()
    {
        // Arrange
        _repository.SeedProducts(20);

        // Act
        var response = await _client.GetAsync("/api/products");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        json.GetProperty("items").GetArrayLength().Should().Be(5); // Custom default
    }

    [Fact]
    [Trait("Category", "Story4.1")]
    public async Task GetAll_EnforcesCustomMaxPageSize()
    {
        // Arrange
        _repository.SeedProducts(100);

        // Act - limit exceeds custom max of 50
        var response = await _client.GetAsync("/api/products?limit=51");

        // Assert
        var problem = await response.ShouldBeProblemDetails(
            HttpStatusCode.BadRequest,
            "/problems/invalid-limit");
        problem.Detail.Should().Contain("50"); // Custom max
    }

    [Fact]
    [Trait("Category", "Story4.1")]
    public async Task GetAll_AllowsUpToCustomMaxPageSize()
    {
        // Arrange
        _repository.SeedProducts(100);

        // Act - limit at custom max of 50
        var response = await _client.GetAsync("/api/products?limit=50");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("items").GetArrayLength().Should().Be(50);
    }
}

/// <summary>
/// Integration tests verifying Zalando Rule 160 compliance for cursor pagination.
/// </summary>
[Trait("Type", "Integration")]
[Trait("Feature", "Pagination")]
public class ZalandoPaginationComplianceTests : IDisposable
{
    private readonly IHost _host;
    private readonly HttpClient _client;
    private readonly InMemoryRepository<ProductEntity, Guid> _repository;

    public ZalandoPaginationComplianceTests()
    {
        _repository = new InMemoryRepository<ProductEntity, Guid>(e => e.Id, Guid.NewGuid);

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
    [Trait("Category", "Story4.1")]
    [Trait("Compliance", "Zalando-Rule-160")]
    public void CursorsAreOpaque_NotExposingInternalDetails()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act
        var cursor = CursorEncoder.Encode(id);

        // Assert
        // Cursor should not directly contain the GUID string
        cursor.Should().NotContain(id.ToString());
        // Should be base64url encoded (opaque)
        cursor.Should().MatchRegex(@"^[A-Za-z0-9_-]+$");
    }

    [Fact]
    [Trait("Category", "Story4.1")]
    [Trait("Compliance", "Zalando-Rule-160")]
    public async Task GetAll_SupportsLimitQueryParameter()
    {
        // Arrange
        _repository.SeedProducts(50);

        // Act
        var response = await _client.GetAsync("/api/products?limit=15");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        json.GetProperty("items").GetArrayLength().Should().Be(15);
    }

    [Fact]
    [Trait("Category", "Story4.1")]
    [Trait("Compliance", "Zalando-Rule-160")]
    public async Task GetAll_SupportsCursorQueryParameter()
    {
        // Arrange
        _repository.SeedProducts(50);
        var cursor = CursorEncoder.Encode(Guid.NewGuid());

        // Act
        var response = await _client.GetAsync($"/api/products?cursor={Uri.EscapeDataString(cursor)}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Category", "Story4.1")]
    [Trait("Compliance", "Zalando-Rule-160")]
    public async Task GetAll_InvalidCursor_ReturnsProperProblemDetails()
    {
        // Act
        var response = await _client.GetAsync("/api/products?cursor=invalid");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<RestLibProblemDetails>();
        problem.Should().NotBeNull();
        problem!.Type.Should().StartWith("/problems/");
        problem.Status.Should().Be(400);
    }
}

/// <summary>
/// Helper for seeding product entities into an InMemoryRepository for pagination tests.
/// </summary>
internal static class PaginationTestHelper
{
    /// <summary>
    /// Seeds the repository with <paramref name="count"/> product entities.
    /// </summary>
    /// <param name="repository">The repository to seed.</param>
    /// <param name="count">Number of entities to seed.</param>
    internal static void SeedProducts(this InMemoryRepository<ProductEntity, Guid> repository, int count)
    {
        var entities = Enumerable.Range(0, count).Select(i => new ProductEntity
        {
            Id = Guid.NewGuid(),
            ProductName = $"Product {i + 1}",
            UnitPrice = 10.00m + i,
            StockQuantity = 100 + i,
            IsActive = true,
        });
        repository.Seed(entities);
    }
}
