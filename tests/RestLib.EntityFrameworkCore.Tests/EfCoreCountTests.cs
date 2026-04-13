using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using RestLib.EntityFrameworkCore.Tests.Fakes;
using RestLib.Serialization;
using Xunit;

namespace RestLib.EntityFrameworkCore.Tests;

/// <summary>
/// Integration tests for countable collection responses backed by EF Core.
/// </summary>
[Trait("Category", "Story3.2.5")]
public class EfCoreCountTests : IAsyncLifetime
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

        _seededProducts = SeedData.CreateProducts(5);
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
    public async Task GetAll_WithCountableRepository_ReturnsTotalCount()
    {
        // Arrange

        // Act
        var response = await _client.GetAsync("/api/products");
        using var doc = await ParseResponseAsync(response);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        doc.RootElement.TryGetProperty("total_count", out var totalCount).Should().BeTrue();
        totalCount.GetInt64().Should().Be(5);
    }

    [Fact]
    public async Task GetAll_AfterCreateAndDelete_TotalCountReflectsChanges()
    {
        // Arrange
        var newProduct = new
        {
            product_name = "Count Product",
            unit_price = 12.34m,
            stock_quantity = 6,
            is_active = true,
            created_at = "2025-04-01T00:00:00Z"
        };

        // Act
        var initialResponse = await _client.GetAsync("/api/products");
        using var initialDoc = await ParseResponseAsync(initialResponse);

        var createResponse = await _client.PostAsJsonAsync("/api/products", newProduct);
        var created = JsonSerializer.Deserialize<ProductEntity>(await createResponse.Content.ReadAsStringAsync(), JsonOptions);

        var afterCreateResponse = await _client.GetAsync("/api/products");
        using var afterCreateDoc = await ParseResponseAsync(afterCreateResponse);

        var deleteResponse = await _client.DeleteAsync($"/api/products/{created!.Id}");

        var afterDeleteResponse = await _client.GetAsync("/api/products");
        using var afterDeleteDoc = await ParseResponseAsync(afterDeleteResponse);

        // Assert
        initialResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        afterCreateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        afterDeleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        initialDoc.RootElement.GetProperty("total_count").GetInt64().Should().Be(5);
        afterCreateDoc.RootElement.GetProperty("total_count").GetInt64().Should().Be(6);
        afterDeleteDoc.RootElement.GetProperty("total_count").GetInt64().Should().Be(5);
    }

    private static async Task<JsonDocument> ParseResponseAsync(HttpResponseMessage response)
    {
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
}
