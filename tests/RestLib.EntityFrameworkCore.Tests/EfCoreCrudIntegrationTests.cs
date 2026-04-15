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
/// CRUD integration tests that verify all six REST operations work correctly
/// through the full HTTP pipeline with the EF Core adapter.
/// </summary>
[Trait("Category", "Story9.1")]
public class EfCoreCrudIntegrationTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = RestLibJsonOptions.CreateDefault();

    private IHost _host = null!;
    private HttpClient _client = null!;
    private TestDbContext _db = null!;
    private List<ProductEntity> _seededProducts = null!;

    public async Task InitializeAsync()
    {
        (_host, _client, _db) = await new EfCoreTestHostBuilder<ProductEntity, Guid>("/api/products")
            .WithEndpoint(config => config.AllowAnonymous())
            .BuildAsync();

        _seededProducts = SeedData.CreateProducts(3);
        _db.Products.AddRange(_seededProducts);
        await _db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    [Fact]
    public async Task GetAll_WithData_ReturnsOkWithAllItems()
    {
        // Arrange

        // Act
        var response = await _client.GetAsync("/api/products?limit=10");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var collection = await DeserializeCollectionAsync(response);
        collection.Items.Should().HaveCount(3);
        collection.TotalCount.Should().Be(3);
        collection.Next.Should().BeNull();
    }

    [Fact]
    public async Task GetAll_WithPagination_ReturnsFirstPageWithNextCursor()
    {
        // Arrange

        // Act
        var response = await _client.GetAsync("/api/products?limit=2");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var collection = await DeserializeCollectionAsync(response);
        collection.Items.Should().HaveCount(2);
        collection.TotalCount.Should().Be(3);
        collection.Next.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetById_ExistingEntity_ReturnsOkWithEntity()
    {
        // Arrange
        var expected = _seededProducts[0];

        // Act
        var response = await _client.GetAsync($"/api/products/{expected.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var entity = await DeserializeProductAsync(response);
        entity.Should().NotBeNull();
        entity!.Id.Should().Be(expected.Id);
        entity.ProductName.Should().Be(expected.ProductName);
        entity.UnitPrice.Should().Be(expected.UnitPrice);
        entity.StockQuantity.Should().Be(expected.StockQuantity);
        entity.IsActive.Should().Be(expected.IsActive);
    }

    [Fact]
    public async Task GetById_NonExistent_ReturnsNotFoundProblemDetails()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/products/{id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problem = await DeserializeProblemAsync(response);
        problem.Should().NotBeNull();
        problem!.Type.Should().Be(ProblemTypes.NotFound);
        problem.Status.Should().Be((int)HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Create_ValidEntity_ReturnsCreatedWithLocationHeader()
    {
        // Arrange
        var payload = new
        {
            product_name = "Integration Product",
            unit_price = 49.99m,
            stock_quantity = 20,
            is_active = true,
            created_at = "2025-01-01T00:00:00Z"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/products", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().Contain("/api/products/");

        var created = await DeserializeProductAsync(response);
        created.Should().NotBeNull();
        created!.Id.Should().NotBeEmpty();
        created.ProductName.Should().Be("Integration Product");
        created.UnitPrice.Should().Be(49.99m);
    }

    [Fact]
    public async Task Create_ValidationFailure_ReturnsBadRequestWithProblemDetails()
    {
        // Arrange
        var payload = new
        {
            unit_price = 10.00m,
            stock_quantity = 1,
            is_active = true,
            created_at = "2025-01-01T00:00:00Z"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/products", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problem = await DeserializeProblemAsync(response);
        problem.Should().NotBeNull();
        problem!.Type.Should().Be(ProblemTypes.ValidationFailed);
        problem.Status.Should().Be((int)HttpStatusCode.BadRequest);
        problem.Errors.Should().ContainKey("product_name");
    }

    [Fact]
    public async Task Update_ExistingEntity_ReturnsOkWithReplacedEntity()
    {
        // Arrange
        var id = _seededProducts[0].Id;
        var payload = new
        {
            product_name = "Updated Product",
            unit_price = 99.99m,
            stock_quantity = 50,
            is_active = false,
            created_at = "2025-06-01T00:00:00Z"
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/products/{id}", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await DeserializeProductAsync(response);
        updated.Should().NotBeNull();
        updated!.Id.Should().Be(id);
        updated.ProductName.Should().Be("Updated Product");
        updated.UnitPrice.Should().Be(99.99m);
        updated.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Update_NonExistent_ReturnsNotFoundProblemDetails()
    {
        // Arrange
        var id = Guid.NewGuid();
        var payload = new
        {
            product_name = "Missing Product",
            unit_price = 12.34m,
            stock_quantity = 1,
            is_active = false,
            created_at = "2025-01-01T00:00:00Z"
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/products/{id}", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problem = await DeserializeProblemAsync(response);
        problem.Should().NotBeNull();
        problem!.Type.Should().Be(ProblemTypes.NotFound);
    }

    [Fact]
    public async Task Patch_ExistingEntity_ReturnsOkWithOnlyPatchedFieldsChanged()
    {
        // Arrange
        var original = _seededProducts[1];
        var payload = new { product_name = "Patched Name" };

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/products/{original.Id}", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var patched = await DeserializeProductAsync(response);
        patched.Should().NotBeNull();
        patched!.ProductName.Should().Be("Patched Name");
        patched.UnitPrice.Should().Be(original.UnitPrice);
        patched.StockQuantity.Should().Be(original.StockQuantity);
        patched.IsActive.Should().Be(original.IsActive);
    }

    [Fact]
    public async Task Patch_NonExistent_ReturnsNotFoundProblemDetails()
    {
        // Arrange
        var id = Guid.NewGuid();
        var payload = new { product_name = "Missing" };

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/products/{id}", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problem = await DeserializeProblemAsync(response);
        problem.Should().NotBeNull();
        problem!.Type.Should().Be(ProblemTypes.NotFound);
    }

    [Fact]
    public async Task Patch_SnakeCaseProperties_MapCorrectlyToPascalCase()
    {
        // Arrange
        var original = _seededProducts[2];
        var payload = new
        {
            product_name = "Snake Mapped",
            unit_price = 55.55m,
            is_active = false
        };

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/products/{original.Id}", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var patched = await DeserializeProductAsync(response);
        patched.Should().NotBeNull();
        patched!.ProductName.Should().Be("Snake Mapped");
        patched.UnitPrice.Should().Be(55.55m);
        patched.IsActive.Should().BeFalse();
        patched.StockQuantity.Should().Be(original.StockQuantity);
    }

    [Fact]
    public async Task Delete_ExistingEntity_ReturnsNoContent()
    {
        // Arrange
        var id = _seededProducts[0].Id;

        // Act
        var response = await _client.DeleteAsync($"/api/products/{id}");
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        body.Should().BeEmpty();
    }

    [Fact]
    public async Task Delete_NonExistent_ReturnsNotFoundProblemDetails()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act
        var response = await _client.DeleteAsync($"/api/products/{id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problem = await DeserializeProblemAsync(response);
        problem.Should().NotBeNull();
        problem!.Type.Should().Be(ProblemTypes.NotFound);
    }

    /// <summary>
    /// Deserializes a product response.
    /// </summary>
    /// <param name="response">The HTTP response.</param>
    /// <returns>The deserialized product.</returns>
    private async Task<ProductEntity?> DeserializeProductAsync(HttpResponseMessage response)
    {
        return JsonSerializer.Deserialize<ProductEntity>(
            await response.Content.ReadAsStringAsync(),
            JsonOptions);
    }

    /// <summary>
    /// Deserializes a collection response.
    /// </summary>
    /// <param name="response">The HTTP response.</param>
    /// <returns>The deserialized collection response.</returns>
    private async Task<CollectionResponse> DeserializeCollectionAsync(HttpResponseMessage response)
    {
        return JsonSerializer.Deserialize<CollectionResponse>(
            await response.Content.ReadAsStringAsync(),
            JsonOptions)!;
    }

    /// <summary>
    /// Deserializes a problem details response.
    /// </summary>
    /// <param name="response">The HTTP response.</param>
    /// <returns>The deserialized problem details.</returns>
    private async Task<RestLibProblemDetails?> DeserializeProblemAsync(HttpResponseMessage response)
    {
        return JsonSerializer.Deserialize<RestLibProblemDetails>(
            await response.Content.ReadAsStringAsync(),
            JsonOptions);
    }

    /// <summary>
    /// Deserialization model for collection responses.
    /// </summary>
    private class CollectionResponse
    {
        /// <summary>
        /// Gets or sets the returned items.
        /// </summary>
        public List<ProductEntity> Items { get; set; } = [];

        /// <summary>
        /// Gets or sets the total count.
        /// </summary>
        public long? TotalCount { get; set; }

        /// <summary>
        /// Gets or sets the next page link.
        /// </summary>
        public string? Next { get; set; }
    }
}
