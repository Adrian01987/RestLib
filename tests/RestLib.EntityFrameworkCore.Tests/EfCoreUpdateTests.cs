using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using RestLib.EntityFrameworkCore.Tests.Fakes;
using RestLib.Responses;
using RestLib.Serialization;
using Xunit;

namespace RestLib.EntityFrameworkCore.Tests;

/// <summary>
/// Integration tests for the EF Core-backed Update endpoint.
/// </summary>
[Trait("Category", "Story3.2.2")]
public class EfCoreUpdateTests : IAsyncLifetime
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
    public async Task Update_ExistingEntity_Returns200AndFullyReplacedEntity()
    {
        // Arrange
        var id = _seededProducts[0].Id;
        var replacement = new
        {
            product_name = "Updated Product",
            unit_price = 99.99m,
            stock_quantity = 42,
            is_active = false,
            created_at = "2025-06-01T00:00:00Z",
            optional_description = "Updated desc",
            status = "Updated"
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/products/{id}", replacement);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await DeserializeProductAsync(response);

        updated.Should().NotBeNull();
        updated!.Id.Should().Be(id);
        updated.ProductName.Should().Be("Updated Product");
        updated.UnitPrice.Should().Be(99.99m);
        updated.StockQuantity.Should().Be(42);
        updated.IsActive.Should().BeFalse();
        updated.CreatedAt.Should().Be(DateTime.Parse("2025-06-01T00:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        updated.OptionalDescription.Should().Be("Updated desc");
        updated.Status.Should().Be("Updated");
    }

    [Fact]
    public async Task Update_NonExistentKey_Returns404ProblemDetails()
    {
        // Arrange
        var id = Guid.NewGuid();
        var replacement = new
        {
            product_name = "Missing Product",
            unit_price = 12.34m,
            stock_quantity = 1,
            is_active = false,
            created_at = "2025-01-01T00:00:00Z"
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/products/{id}", replacement);

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
    public async Task Update_AllPropertiesIncludingDefaults_AreReplaced()
    {
        // Arrange
        var id = _seededProducts[1].Id;
        var replacement = new
        {
            product_name = "Defaults Replaced",
            unit_price = 0m,
            stock_quantity = 0,
            is_active = false,
            created_at = "2025-02-15T00:00:00Z",
            optional_description = (string?)null,
            status = "Inactive"
        };

        // Act
        var updateResponse = await _client.PutAsJsonAsync($"/api/products/{id}", replacement);
        var getResponse = await _client.GetAsync($"/api/products/{id}");

        // Assert
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var retrieved = await DeserializeProductAsync(getResponse);

        retrieved.Should().NotBeNull();
        retrieved!.Id.Should().Be(id);
        retrieved.ProductName.Should().Be("Defaults Replaced");
        retrieved.UnitPrice.Should().Be(0m);
        retrieved.StockQuantity.Should().Be(0);
        retrieved.IsActive.Should().BeFalse();
        retrieved.OptionalDescription.Should().BeNull();
        retrieved.Status.Should().Be("Inactive");
    }

    [Fact]
    public async Task Update_PersistedChanges_RetrievableViaGetById()
    {
        // Arrange
        var id = _seededProducts[2].Id;
        var replacement = new
        {
            product_name = "Round Trip Update",
            unit_price = 15.25m,
            stock_quantity = 9,
            is_active = true,
            created_at = "2025-03-20T00:00:00Z",
            optional_description = "Round trip desc",
            status = "Updated"
        };

        await _client.PutAsJsonAsync($"/api/products/{id}", replacement);

        // Act
        var getResponse = await _client.GetAsync($"/api/products/{id}");

        // Assert
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var retrieved = await DeserializeProductAsync(getResponse);

        retrieved.Should().NotBeNull();
        retrieved!.Id.Should().Be(id);
        retrieved.ProductName.Should().Be("Round Trip Update");
        retrieved.UnitPrice.Should().Be(15.25m);
        retrieved.StockQuantity.Should().Be(9);
        retrieved.IsActive.Should().BeTrue();
        retrieved.OptionalDescription.Should().Be("Round trip desc");
        retrieved.Status.Should().Be("Updated");
    }

    private static async Task<ProductEntity?> DeserializeProductAsync(HttpResponseMessage response)
    {
        return JsonSerializer.Deserialize<ProductEntity>(
            await response.Content.ReadAsStringAsync(),
            JsonOptions);
    }
}
