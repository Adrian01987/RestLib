using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using RestLib.Responses;
using RestLib.Tests.Fakes;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Tests for Story 3.4: Correct HTTP Status Codes
/// Verifies that all operations return appropriate status codes per Zalando guidelines.
/// </summary>
public class HttpStatusCodeTests : IDisposable
{
    private readonly IHost _host;
    private readonly HttpClient _client;
    private readonly ProductEntityRepository _repository;

    public HttpStatusCodeTests()
    {
        _repository = new ProductEntityRepository();

        (_host, _client) = new TestHostBuilder<ProductEntity, Guid>(_repository, "/api/products")
            .WithEndpoint(config => config.AllowAnonymous())
            .Build();
    }

    #region GET /collection - List

    [Fact]
    public async Task GetAll_Returns_200_OK()
    {
        // Arrange
        _repository.Seed(
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Product 1", UnitPrice = 10.00m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true }
        );

        // Act
        var response = await _client.GetAsync("/api/products");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetAll_Empty_Returns_200_OK()
    {
        // Act
        var response = await _client.GetAsync("/api/products");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion

    #region GET /collection/{id} - Read

    [Fact]
    public async Task GetById_ExistingResource_Returns_200_OK()
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
    public async Task GetById_NonExistingResource_Returns_404_NotFound()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/products/{nonExistentId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region POST /collection - Create

    [Fact]
    public async Task Create_Returns_201_Created()
    {
        // Arrange
        var newProduct = new
        {
            product_name = "New Product",
            unit_price = 25.00,
            stock_quantity = 10,
            is_active = true
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/products", newProduct);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Create_Returns_Location_Header()
    {
        // Arrange
        var newProduct = new
        {
            product_name = "New Product",
            unit_price = 25.00,
            stock_quantity = 10,
            is_active = true
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/products", newProduct);

        // Assert
        response.Headers.Location.Should().NotBeNull();
    }

    [Fact]
    public async Task Create_Location_Header_Contains_Resource_Path()
    {
        // Arrange
        var newProduct = new
        {
            product_name = "New Product",
            unit_price = 25.00,
            stock_quantity = 10,
            is_active = true
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/products", newProduct);

        // Assert
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().Contain("/api/products/");
    }

    [Fact]
    public async Task Create_Location_Header_Contains_Created_Resource_Id()
    {
        // Arrange
        var newProduct = new
        {
            product_name = "New Product",
            unit_price = 25.00,
            stock_quantity = 10,
            is_active = true
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/products", newProduct);

        // Assert
        response.Headers.Location.Should().NotBeNull();
        var locationPath = response.Headers.Location!.ToString();

        // Extract the ID from the location and verify it's a valid GUID
        var parts = locationPath.Split('/');
        var idPart = parts[^1]; // Last segment should be the ID
        Guid.TryParse(idPart, out var createdId).Should().BeTrue("Location header should contain a valid GUID");
        createdId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task Create_Location_Header_Points_To_GetById_Endpoint()
    {
        // Arrange
        var newProduct = new
        {
            product_name = "Verifiable Product",
            unit_price = 99.99,
            stock_quantity = 50,
            is_active = true
        };

        // Act
        var createResponse = await _client.PostAsJsonAsync("/api/products", newProduct);

        // Assert - Location should point to a valid resource
        createResponse.Headers.Location.Should().NotBeNull();
        var locationPath = createResponse.Headers.Location!.ToString();

        // Follow the Location header to verify it returns the created resource
        var getResponse = await _client.GetAsync(locationPath);
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var retrievedProduct = await getResponse.Content.ReadAsStringAsync();
        retrievedProduct.Should().Contain("Verifiable Product");
    }

    #endregion

    #region PUT /collection/{id} - Full Update

    [Fact]
    public async Task Update_ExistingResource_Returns_200_OK()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.Seed(
            new ProductEntity { Id = id, ProductName = "Original", UnitPrice = 10.00m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = false }
        );

        var updatedProduct = new
        {
            product_name = "Updated Product",
            unit_price = 50.00,
            stock_quantity = 100,
            is_active = true
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/products/{id}", updatedProduct);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Update_NonExistingResource_Returns_404_NotFound()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();
        var updatedProduct = new
        {
            product_name = "Updated Product",
            unit_price = 50.00,
            stock_quantity = 100,
            is_active = true
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/products/{nonExistentId}", updatedProduct);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region PATCH /collection/{id} - Partial Update

    [Fact]
    public async Task Patch_ExistingResource_Returns_200_OK()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.Seed(
            new ProductEntity { Id = id, ProductName = "Original", UnitPrice = 10.00m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = false }
        );

        var patch = new { product_name = "Patched Name" };

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/products/{id}", patch);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Patch_NonExistingResource_Returns_404_NotFound()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();
        var patch = new { product_name = "Patched Name" };

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/products/{nonExistentId}", patch);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region DELETE /collection/{id} - Delete

    [Fact]
    public async Task Delete_ExistingResource_Returns_204_NoContent()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.Seed(
            new ProductEntity { Id = id, ProductName = "To Delete", UnitPrice = 10.00m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true }
        );

        // Act
        var response = await _client.DeleteAsync($"/api/products/{id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Delete_NonExistingResource_Returns_404_NotFound()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await _client.DeleteAsync($"/api/products/{nonExistentId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_Success_Has_No_Response_Body()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.Seed(
            new ProductEntity { Id = id, ProductName = "To Delete", UnitPrice = 10.00m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true }
        );

        // Act
        var response = await _client.DeleteAsync($"/api/products/{id}");
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        body.Should().BeEmpty();
    }

    #endregion

    public void Dispose()
    {
        _client.Dispose();
        _host.Dispose();
    }
}

/// <summary>
/// Tests for 412 Precondition Failed when ETag support is enabled.
/// </summary>
public class ETagPreconditionTests : IDisposable
{
    private IHost? _host;
    private HttpClient? _client;
    private ProductEntityRepository? _repository;

    private void SetupHost(bool enableETagSupport)
    {
        _repository = new ProductEntityRepository();

        (_host, _client) = new TestHostBuilder<ProductEntity, Guid>(_repository, "/api/products")
            .WithOptions(options => options.EnableETagSupport = enableETagSupport)
            .WithEndpoint(config => config.AllowAnonymous())
            .Build();
    }

    [Fact]
    public async Task Update_With_IfMatch_Mismatch_Returns_412_WhenETagEnabled()
    {
        // Arrange
        SetupHost(enableETagSupport: true);

        var id = Guid.NewGuid();
        _repository!.Seed(
            new ProductEntity { Id = id, ProductName = "Original", UnitPrice = 10.00m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true }
        );

        var updatedProduct = new
        {
            product_name = "Updated",
            unit_price = 50.00,
            stock_quantity = 100,
            is_active = true
        };

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/products/{id}")
        {
            Content = JsonContent.Create(updatedProduct)
        };
        request.Headers.TryAddWithoutValidation("If-Match", "\"wrong-etag\"");

        // Act
        var response = await _client!.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
    }

    [Fact]
    public async Task Update_With_IfMatch_Mismatch_Returns_ProblemDetails()
    {
        // Arrange
        SetupHost(enableETagSupport: true);

        var id = Guid.NewGuid();
        _repository!.Seed(
            new ProductEntity { Id = id, ProductName = "Original", UnitPrice = 10.00m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true }
        );

        var updatedProduct = new { product_name = "Updated" };

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/products/{id}")
        {
            Content = JsonContent.Create(updatedProduct)
        };
        request.Headers.TryAddWithoutValidation("If-Match", "\"wrong-etag\"");

        // Act
        var response = await _client!.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<RestLibProblemDetails>();
        problem.Should().NotBeNull();
        problem!.Type.Should().Be(ProblemTypes.PreconditionFailed);
        problem.Status.Should().Be(412);
    }

    [Fact]
    public async Task Patch_With_IfMatch_Mismatch_Returns_412_WhenETagEnabled()
    {
        // Arrange
        SetupHost(enableETagSupport: true);

        var id = Guid.NewGuid();
        _repository!.Seed(
            new ProductEntity { Id = id, ProductName = "Original", UnitPrice = 10.00m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true }
        );

        var patch = new { product_name = "Patched" };

        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/products/{id}")
        {
            Content = JsonContent.Create(patch)
        };
        request.Headers.TryAddWithoutValidation("If-Match", "\"wrong-etag\"");

        // Act
        var response = await _client!.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
    }

    [Fact]
    public async Task Update_With_IfMatch_Wildcard_Succeeds()
    {
        // Arrange
        SetupHost(enableETagSupport: true);

        var id = Guid.NewGuid();
        _repository!.Seed(
            new ProductEntity { Id = id, ProductName = "Original", UnitPrice = 10.00m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true }
        );

        var updatedProduct = new
        {
            product_name = "Updated",
            unit_price = 50.00,
            stock_quantity = 100,
            is_active = true
        };

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/products/{id}")
        {
            Content = JsonContent.Create(updatedProduct)
        };
        request.Headers.TryAddWithoutValidation("If-Match", "*");

        // Act
        var response = await _client!.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Update_Without_IfMatch_Succeeds_WhenETagEnabled()
    {
        // Arrange
        SetupHost(enableETagSupport: true);

        var id = Guid.NewGuid();
        _repository!.Seed(
            new ProductEntity { Id = id, ProductName = "Original", UnitPrice = 10.00m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true }
        );

        var updatedProduct = new
        {
            product_name = "Updated",
            unit_price = 50.00,
            stock_quantity = 100,
            is_active = true
        };

        // Act
        var response = await _client!.PutAsJsonAsync($"/api/products/{id}", updatedProduct);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Update_With_IfMatch_NoETagSupport_IgnoresHeader()
    {
        // Arrange
        SetupHost(enableETagSupport: false);

        var id = Guid.NewGuid();
        _repository!.Seed(
            new ProductEntity { Id = id, ProductName = "Original", UnitPrice = 10.00m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true }
        );

        var updatedProduct = new
        {
            product_name = "Updated",
            unit_price = 50.00,
            stock_quantity = 100,
            is_active = true
        };

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/products/{id}")
        {
            Content = JsonContent.Create(updatedProduct)
        };
        request.Headers.TryAddWithoutValidation("If-Match", "\"wrong-etag\"");

        // Act
        var response = await _client!.SendAsync(request);

        // Assert - should succeed because ETag support is disabled
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Update_With_IfMatch_NonExistingResource_Returns_404()
    {
        // Arrange
        SetupHost(enableETagSupport: true);

        var nonExistentId = Guid.NewGuid();
        var updatedProduct = new { product_name = "Updated" };

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/products/{nonExistentId}")
        {
            Content = JsonContent.Create(updatedProduct)
        };
        request.Headers.TryAddWithoutValidation("If-Match", "\"some-etag\"");

        // Act
        var response = await _client!.SendAsync(request);

        // Assert - 404 takes precedence over 412
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    public void Dispose()
    {
        _client?.Dispose();
        _host?.Dispose();
    }
}

/// <summary>
/// Tests verifying status codes match Zalando guidelines table.
/// </summary>
public class ZalandoStatusCodeComplianceTests : IDisposable
{
    private readonly IHost _host;
    private readonly HttpClient _client;
    private readonly ProductEntityRepository _repository;

    public ZalandoStatusCodeComplianceTests()
    {
        _repository = new ProductEntityRepository();

        (_host, _client) = new TestHostBuilder<ProductEntity, Guid>(_repository, "/api/products")
            .WithEndpoint(config => config.AllowAnonymous())
            .Build();
    }

    /// <summary>
    /// Zalando: List (GET /collection) → 200 OK
    /// </summary>
    [Fact]
    public async Task Zalando_List_Returns_200()
    {
        var response = await _client.GetAsync("/api/products");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Zalando: Read (GET /collection/{id}) → 200 OK on success
    /// </summary>
    [Fact]
    public async Task Zalando_Read_Success_Returns_200()
    {
        var id = Guid.NewGuid();
        _repository.Seed(new ProductEntity { Id = id, ProductName = "Test", UnitPrice = 10, StockQuantity = 1, CreatedAt = DateTime.UtcNow, IsActive = true });

        var response = await _client.GetAsync($"/api/products/{id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Zalando: Read (GET /collection/{id}) → 404 Not Found when resource doesn't exist
    /// </summary>
    [Fact]
    public async Task Zalando_Read_NotFound_Returns_404()
    {
        var response = await _client.GetAsync($"/api/products/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Zalando: Create (POST /collection) → 201 Created + Location header
    /// </summary>
    [Fact]
    public async Task Zalando_Create_Returns_201_WithLocation()
    {
        var product = new { product_name = "Test", unit_price = 10.00, stock_quantity = 1, is_active = true };

        var response = await _client.PostAsJsonAsync("/api/products", product);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
    }

    /// <summary>
    /// Zalando: Full Update (PUT /collection/{id}) → 200 OK on success
    /// </summary>
    [Fact]
    public async Task Zalando_FullUpdate_Success_Returns_200()
    {
        var id = Guid.NewGuid();
        _repository.Seed(new ProductEntity { Id = id, ProductName = "Test", UnitPrice = 10, StockQuantity = 1, CreatedAt = DateTime.UtcNow, IsActive = true });

        var updated = new { product_name = "Updated", unit_price = 20.00, stock_quantity = 2, is_active = false };
        var response = await _client.PutAsJsonAsync($"/api/products/{id}", updated);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Zalando: Full Update (PUT /collection/{id}) → 404 Not Found when resource doesn't exist
    /// </summary>
    [Fact]
    public async Task Zalando_FullUpdate_NotFound_Returns_404()
    {
        var updated = new { product_name = "Updated", unit_price = 20.00, stock_quantity = 2, is_active = false };
        var response = await _client.PutAsJsonAsync($"/api/products/{Guid.NewGuid()}", updated);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Zalando: Partial Update (PATCH /collection/{id}) → 200 OK on success
    /// </summary>
    [Fact]
    public async Task Zalando_PartialUpdate_Success_Returns_200()
    {
        var id = Guid.NewGuid();
        _repository.Seed(new ProductEntity { Id = id, ProductName = "Test", UnitPrice = 10, StockQuantity = 1, CreatedAt = DateTime.UtcNow, IsActive = true });

        var patch = new { product_name = "Patched" };
        var response = await _client.PatchAsJsonAsync($"/api/products/{id}", patch);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Zalando: Partial Update (PATCH /collection/{id}) → 404 Not Found when resource doesn't exist
    /// </summary>
    [Fact]
    public async Task Zalando_PartialUpdate_NotFound_Returns_404()
    {
        var patch = new { product_name = "Patched" };
        var response = await _client.PatchAsJsonAsync($"/api/products/{Guid.NewGuid()}", patch);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Zalando: Delete (DELETE /collection/{id}) → 204 No Content on success
    /// </summary>
    [Fact]
    public async Task Zalando_Delete_Success_Returns_204()
    {
        var id = Guid.NewGuid();
        _repository.Seed(new ProductEntity { Id = id, ProductName = "Test", UnitPrice = 10, StockQuantity = 1, CreatedAt = DateTime.UtcNow, IsActive = true });

        var response = await _client.DeleteAsync($"/api/products/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// Zalando: Delete (DELETE /collection/{id}) → 404 Not Found when resource doesn't exist
    /// </summary>
    [Fact]
    public async Task Zalando_Delete_NotFound_Returns_404()
    {
        var response = await _client.DeleteAsync($"/api/products/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    public void Dispose()
    {
        _client.Dispose();
        _host.Dispose();
    }
}
