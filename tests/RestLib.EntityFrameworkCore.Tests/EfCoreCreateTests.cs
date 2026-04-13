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
/// Integration tests for the EF Core-backed Create endpoint.
/// </summary>
[Trait("Category", "Story3.2.1")]
public class EfCoreCreateTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = RestLibJsonOptions.CreateDefault();

    private IHost _host = null!;
    private HttpClient _client = null!;
    private TestDbContext _db = null!;

    /// <summary>
    /// Sets up the test host with an empty database.
    /// </summary>
    public async Task InitializeAsync()
    {
        (_host, _client, _db) = await new EfCoreTestHostBuilder<ProductEntity, Guid>("/api/products")
            .WithEndpoint(config => config.AllowAnonymous())
            .BuildAsync();
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
    public async Task Create_ValidEntity_Returns201AndPersistedEntity()
    {
        // Arrange
        var newProduct = new
        {
            product_name = "New Product",
            unit_price = 25.99m,
            stock_quantity = 10,
            is_active = true,
            created_at = "2024-01-15T00:00:00Z"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/products", newProduct);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().Contain("/api/products/");

        var created = await DeserializeProductAsync(response);

        created.Should().NotBeNull();
        created!.Id.Should().NotBeEmpty();
        created.ProductName.Should().Be("New Product");
        created.UnitPrice.Should().Be(25.99m);
        created.StockQuantity.Should().Be(10);
        created.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Create_EntityIsPersisted_RetrievableViaGetById()
    {
        // Arrange
        var newProduct = new
        {
            product_name = "Round Trip Product",
            unit_price = 99.50m,
            stock_quantity = 7,
            is_active = true,
            created_at = "2024-02-10T00:00:00Z"
        };

        var createResponse = await _client.PostAsJsonAsync("/api/products", newProduct);
        var created = await DeserializeProductAsync(createResponse);

        // Act
        var getResponse = await _client.GetAsync($"/api/products/{created!.Id}");

        // Assert
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var retrieved = await DeserializeProductAsync(getResponse);

        retrieved.Should().NotBeNull();
        retrieved!.Id.Should().Be(created.Id);
        retrieved.ProductName.Should().Be("Round Trip Product");
        retrieved.UnitPrice.Should().Be(99.50m);
        retrieved.StockQuantity.Should().Be(7);
        retrieved.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Create_DatabaseGeneratedKey_IsReflectedInResponse()
    {
        // Arrange
        var newProduct = new
        {
            id = Guid.Empty,
            product_name = "Generated Key Product",
            unit_price = 10.00m,
            stock_quantity = 1,
            is_active = true,
            created_at = "2024-03-01T00:00:00Z"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/products", newProduct);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await DeserializeProductAsync(response);

        created.Should().NotBeNull();
        created!.Id.Should().NotBe(Guid.Empty);
    }

    private static async Task<ProductEntity?> DeserializeProductAsync(HttpResponseMessage response)
    {
        return JsonSerializer.Deserialize<ProductEntity>(
            await response.Content.ReadAsStringAsync(),
            JsonOptions);
    }
}
