using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using RestLib.EntityFrameworkCore.Tests.Fakes;
using RestLib.Responses;
using RestLib.Serialization;
using Xunit;

namespace RestLib.EntityFrameworkCore.Tests;

/// <summary>
/// Integration tests for the EF Core-backed Patch endpoint.
/// </summary>
[Trait("Category", "Story3.2.4")]
public class EfCorePatchTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = RestLibJsonOptions.CreateDefault();

    private IHost _host = null!;
    private HttpClient _client = null!;
    private TestDbContext _db = null!;
    private List<ProductEntity> _seededProducts = null!;

    /// <summary>
    /// Sets up the test host and seeds products.
    /// </summary>
    public async Task InitializeAsync()
    {
        (_host, _client, _db) = await new EfCoreTestHostBuilder<ProductEntity, Guid>("/api/products")
            .WithEndpoint(config => config.AllowAnonymous())
            .BuildAsync();

        _seededProducts = SeedData.CreateProducts(3);
        _db.Products.AddRange(_seededProducts);
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Cleans up the test host and HTTP client.
    /// </summary>
    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    [Fact]
    public async Task Patch_SubsetOfProperties_LeavesOthersUnchanged()
    {
        // Arrange
        var original = _seededProducts[0];
        var patch = new { product_name = "Patched Name" };

        // Act
        var patchResponse = await _client.PatchAsJsonAsync($"/api/products/{original.Id}", patch);
        var getResponse = await _client.GetAsync($"/api/products/{original.Id}");

        // Assert
        patchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var patched = await DeserializeProductAsync(getResponse);

        patched.Should().NotBeNull();
        patched!.ProductName.Should().Be("Patched Name");
        patched.UnitPrice.Should().Be(original.UnitPrice);
        patched.StockQuantity.Should().Be(original.StockQuantity);
        patched.IsActive.Should().Be(original.IsActive);
    }

    [Fact]
    public async Task Patch_SnakeCasePropertyNames_MapToPascalCaseProperties()
    {
        // Arrange
        var original = _seededProducts[1];
        var patch = new
        {
            product_name = "Snake Patched",
            unit_price = 77.77m,
            is_active = false
        };

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/products/{original.Id}", patch);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var patched = await DeserializeProductAsync(response);

        patched.Should().NotBeNull();
        patched!.ProductName.Should().Be("Snake Patched");
        patched.UnitPrice.Should().Be(77.77m);
        patched.IsActive.Should().BeFalse();
        patched.StockQuantity.Should().Be(original.StockQuantity);
        patched.Status.Should().Be(original.Status);
    }

    [Fact]
    public async Task Patch_NullablePropertySetToNull_SetsToNull()
    {
        // Arrange
        var original = _seededProducts[0];
        using var content = new StringContent(
            "{\"optional_description\":null}",
            Encoding.UTF8,
            "application/json");

        // Act
        var patchResponse = await _client.PatchAsync($"/api/products/{original.Id}", content);
        var getResponse = await _client.GetAsync($"/api/products/{original.Id}");

        // Assert
        patchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var patched = await DeserializeProductAsync(getResponse);

        patched.Should().NotBeNull();
        patched!.OptionalDescription.Should().BeNull();
    }

    [Fact]
    public async Task Patch_NonExistentKey_Returns404ProblemDetails()
    {
        // Arrange
        var id = Guid.NewGuid();
        var patch = new { product_name = "Missing" };

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/products/{id}", patch);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problem = JsonSerializer.Deserialize<RestLibProblemDetails>(
            await response.Content.ReadAsStringAsync(),
            JsonOptions);

        problem.Should().NotBeNull();
        problem!.Type.Should().Be(ProblemTypes.NotFound);
    }

    [Fact]
    public async Task Patch_EmptyPatchDocument_ReturnsEntityUnchanged()
    {
        // Arrange
        var original = _seededProducts[2];
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");

        // Act
        var patchResponse = await _client.PatchAsync($"/api/products/{original.Id}", content);

        // Assert
        patchResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var patched = await DeserializeProductAsync(patchResponse);

        patched.Should().NotBeNull();
        patched!.Id.Should().Be(original.Id);
        patched.ProductName.Should().Be(original.ProductName);
        patched.UnitPrice.Should().Be(original.UnitPrice);
        patched.StockQuantity.Should().Be(original.StockQuantity);
        patched.CreatedAt.Should().Be(original.CreatedAt);
        patched.LastModifiedAt.Should().Be(original.LastModifiedAt);
        patched.OptionalDescription.Should().Be(original.OptionalDescription);
        patched.IsActive.Should().Be(original.IsActive);
        patched.CategoryId.Should().Be(original.CategoryId);
        patched.Status.Should().Be(original.Status);
    }

    private static async Task<ProductEntity?> DeserializeProductAsync(HttpResponseMessage response)
    {
        return JsonSerializer.Deserialize<ProductEntity>(
            await response.Content.ReadAsStringAsync(),
            JsonOptions);
    }
}
