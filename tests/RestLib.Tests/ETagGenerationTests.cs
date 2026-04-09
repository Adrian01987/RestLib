using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RestLib.Abstractions;
using RestLib.Caching;
using RestLib.Tests.Fakes;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Tests for Story 5.1: ETag Generation
/// Verifies ETag headers are generated correctly per RFC 9110.
/// </summary>
[Trait("Type", "Integration")]
[Trait("Feature", "ConditionalRequests")]
public class ETagGenerationTests : IDisposable
{
    private readonly IHost _host;
    private readonly HttpClient _client;
    private readonly ProductEntityRepository _repository;

    public ETagGenerationTests()
    {
        _repository = new ProductEntityRepository();

        (_host, _client) = new TestHostBuilder<ProductEntity, Guid>(_repository, "/api/products")
            .WithOptions(options => options.EnableETagSupport = true)
            .WithEndpoint(config => config.AllowAnonymous())
            .Build();
    }

    #region GET /collection/{id} - ETag in Response

    [Fact]
    public async Task GetById_WithETagEnabled_Returns_ETag_Header()
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
        response.Headers.ETag.Should().NotBeNull();
    }

    [Fact]
    public async Task GetById_ETag_Follows_RFC_9110_Format()
    {
        // Arrange - RFC 9110: ETags are quoted strings
        var id = Guid.NewGuid();
        _repository.Seed(
            new ProductEntity { Id = id, ProductName = "Product", UnitPrice = 10.00m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true }
        );

        // Act
        var response = await _client.GetAsync($"/api/products/{id}");

        // Assert
        response.Headers.ETag.Should().NotBeNull();
        var etag = response.Headers.ETag!.Tag;
        etag.Should().StartWith("\"").And.EndWith("\"");
    }

    [Fact]
    public async Task GetById_Same_Resource_Returns_Same_ETag()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.Seed(
            new ProductEntity { Id = id, ProductName = "Product", UnitPrice = 10.00m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true }
        );

        // Act
        var response1 = await _client.GetAsync($"/api/products/{id}");
        var response2 = await _client.GetAsync($"/api/products/{id}");

        // Assert
        response1.Headers.ETag!.Tag.Should().Be(response2.Headers.ETag!.Tag);
    }

    [Fact]
    public async Task GetById_Different_Resources_Return_Different_ETags()
    {
        // Arrange
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        _repository.Seed(
            new ProductEntity { Id = id1, ProductName = "Product 1", UnitPrice = 10.00m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = id2, ProductName = "Product 2", UnitPrice = 20.00m, StockQuantity = 10, CreatedAt = DateTime.UtcNow, IsActive = true }
        );

        // Act
        var response1 = await _client.GetAsync($"/api/products/{id1}");
        var response2 = await _client.GetAsync($"/api/products/{id2}");

        // Assert
        response1.Headers.ETag!.Tag.Should().NotBe(response2.Headers.ETag!.Tag);
    }

    [Fact]
    public async Task GetById_NotFound_Does_Not_Return_ETag()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/products/{nonExistentId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Headers.ETag.Should().BeNull();
    }

    #endregion

    #region POST /collection - ETag in Response

    [Fact]
    public async Task Create_WithETagEnabled_Returns_ETag_Header()
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
        response.Headers.ETag.Should().NotBeNull();
    }

    [Fact]
    public async Task Create_ETag_Matches_GetById_ETag()
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
        var createResponse = await _client.PostAsJsonAsync("/api/products", newProduct);
        var locationPath = createResponse.Headers.Location!.ToString();
        var getResponse = await _client.GetAsync(locationPath);

        // Assert
        createResponse.Headers.ETag!.Tag.Should().Be(getResponse.Headers.ETag!.Tag);
    }

    #endregion

    #region PUT /collection/{id} - ETag in Response

    [Fact]
    public async Task Update_WithETagEnabled_Returns_ETag_Header()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.Seed(
            new ProductEntity { Id = id, ProductName = "Product", UnitPrice = 10.00m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true }
        );
        var updatedProduct = new
        {
            product_name = "Updated Product",
            unit_price = 15.00,
            stock_quantity = 8,
            is_active = true
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/products/{id}", updatedProduct);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.ETag.Should().NotBeNull();
    }

    [Fact]
    public async Task Update_Changes_ETag()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.Seed(
            new ProductEntity { Id = id, ProductName = "Product", UnitPrice = 10.00m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true }
        );

        // Act
        var getResponse = await _client.GetAsync($"/api/products/{id}");
        var originalETag = getResponse.Headers.ETag!.Tag;

        var updatedProduct = new
        {
            product_name = "Updated Product",
            unit_price = 15.00,
            stock_quantity = 8,
            is_active = true
        };
        var updateResponse = await _client.PutAsJsonAsync($"/api/products/{id}", updatedProduct);

        // Assert
        updateResponse.Headers.ETag!.Tag.Should().NotBe(originalETag);
    }

    [Fact]
    public async Task Update_NotFound_Does_Not_Return_ETag()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();
        var updatedProduct = new
        {
            product_name = "Updated Product",
            unit_price = 15.00,
            stock_quantity = 8,
            is_active = true
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/products/{nonExistentId}", updatedProduct);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Headers.ETag.Should().BeNull();
    }

    #endregion

    #region PATCH /collection/{id} - ETag in Response

    [Fact]
    public async Task Patch_WithETagEnabled_Returns_ETag_Header()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.Seed(
            new ProductEntity { Id = id, ProductName = "Product", UnitPrice = 10.00m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true }
        );
        var patchContent = new StringContent(
            "{\"product_name\": \"Patched Product\"}",
            System.Text.Encoding.UTF8,
            "application/merge-patch+json");

        // Act
        var response = await _client.PatchAsync($"/api/products/{id}", patchContent);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.ETag.Should().NotBeNull();
    }

    [Fact]
    public async Task Patch_NotFound_Does_Not_Return_ETag()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();
        var patchContent = new StringContent(
            "{\"product_name\": \"Patched Product\"}",
            System.Text.Encoding.UTF8,
            "application/merge-patch+json");

        // Act
        var response = await _client.PatchAsync($"/api/products/{nonExistentId}", patchContent);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Headers.ETag.Should().BeNull();
    }

    #endregion

    #region ETag Disabled

    [Fact]
    public async Task GetById_WithETagDisabled_Does_Not_Return_ETag_Header()
    {
        // Arrange - Create a separate host with ETag disabled
        var (disabledHost, disabledClient) = new TestHostBuilder<ProductEntity, Guid>(_repository, "/api/products")
            .WithOptions(options => options.EnableETagSupport = false)
            .WithEndpoint(config => config.AllowAnonymous())
            .Build();
        using var _ = disabledHost;

        var id = Guid.NewGuid();
        _repository.Seed(
            new ProductEntity { Id = id, ProductName = "Product", UnitPrice = 10.00m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true }
        );

        // Act
        var response = await disabledClient.GetAsync($"/api/products/{id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.ETag.Should().BeNull();
    }

    #endregion

    public void Dispose()
    {
        _client.Dispose();
        _host.Dispose();
    }
}

/// <summary>
/// Unit tests for HashBasedETagGenerator
/// </summary>
[Trait("Type", "Unit")]
[Trait("Feature", "ConditionalRequests")]
public class HashBasedETagGeneratorTests
{
    private readonly HashBasedETagGenerator _generator = new();

    [Fact]
    public void Generate_Returns_Quoted_String()
    {
        // Arrange
        var entity = new TestEntity { Id = Guid.NewGuid(), Name = "Test", Price = 10.00m };

        // Act
        var etag = _generator.Generate(entity);

        // Assert
        etag.Should().StartWith("\"").And.EndWith("\"");
    }

    [Fact]
    public void Generate_Same_Entity_Returns_Same_ETag()
    {
        // Arrange
        var id = Guid.NewGuid();
        var entity1 = new TestEntity { Id = id, Name = "Test", Price = 10.00m };
        var entity2 = new TestEntity { Id = id, Name = "Test", Price = 10.00m };

        // Act
        var etag1 = _generator.Generate(entity1);
        var etag2 = _generator.Generate(entity2);

        // Assert
        etag1.Should().Be(etag2);
    }

    [Fact]
    public void Generate_Different_Entities_Return_Different_ETags()
    {
        // Arrange
        var entity1 = new TestEntity { Id = Guid.NewGuid(), Name = "Test 1", Price = 10.00m };
        var entity2 = new TestEntity { Id = Guid.NewGuid(), Name = "Test 2", Price = 20.00m };

        // Act
        var etag1 = _generator.Generate(entity1);
        var etag2 = _generator.Generate(entity2);

        // Assert
        etag1.Should().NotBe(etag2);
    }

    [Fact]
    public void Generate_Property_Change_Changes_ETag()
    {
        // Arrange
        var id = Guid.NewGuid();
        var original = new TestEntity { Id = id, Name = "Original", Price = 10.00m };
        var modified = new TestEntity { Id = id, Name = "Modified", Price = 10.00m };

        // Act
        var originalETag = _generator.Generate(original);
        var modifiedETag = _generator.Generate(modified);

        // Assert
        originalETag.Should().NotBe(modifiedETag);
    }

    [Fact]
    public void Generate_ThrowsArgumentNullException_ForNullEntity()
    {
        // Act
        var act = () => _generator.Generate<TestEntity>(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Validate_Returns_True_For_Matching_ETag()
    {
        // Arrange
        var entity = new TestEntity { Id = Guid.NewGuid(), Name = "Test", Price = 10.00m };
        var etag = _generator.Generate(entity);

        // Act
        var result = _generator.Validate(entity, etag);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Validate_Returns_False_For_NonMatching_ETag()
    {
        // Arrange
        var entity = new TestEntity { Id = Guid.NewGuid(), Name = "Test", Price = 10.00m };

        // Act
        var result = _generator.Validate(entity, "\"different-etag\"");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Validate_Returns_True_For_Wildcard()
    {
        // Arrange
        var entity = new TestEntity { Id = Guid.NewGuid(), Name = "Test", Price = 10.00m };

        // Act
        var result = _generator.Validate(entity, "*");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Validate_Returns_False_For_Empty_ETag()
    {
        // Arrange
        var entity = new TestEntity { Id = Guid.NewGuid(), Name = "Test", Price = 10.00m };

        // Act
        var result = _generator.Validate(entity, "");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Validate_Returns_False_For_Null_ETag()
    {
        // Arrange
        var entity = new TestEntity { Id = Guid.NewGuid(), Name = "Test", Price = 10.00m };

        // Act
        var result = _generator.Validate(entity, null!);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Validate_Handles_WeakETag()
    {
        // Arrange - Weak ETags (W/"...") should match by stripping the W/ prefix
        var entity = new TestEntity { Id = Guid.NewGuid(), Name = "Test", Price = 10.00m };
        var strongETag = _generator.Generate(entity);
        var weakETag = $"W/{strongETag}";

        // Act
        var result = _generator.Validate(entity, weakETag);

        // Assert
        result.Should().BeTrue();
    }
}

/// <summary>
/// Tests for custom ETag generator injection
/// </summary>
[Trait("Type", "Integration")]
[Trait("Feature", "ConditionalRequests")]
public class CustomETagGeneratorTests : IDisposable
{
    private readonly IHost _host;
    private readonly HttpClient _client;
    private readonly ProductEntityRepository _repository;

    public CustomETagGeneratorTests()
    {
        _repository = new ProductEntityRepository();

        (_host, _client) = new TestHostBuilder<ProductEntity, Guid>(_repository, "/api/products")
            .WithOptions(options => options.EnableETagSupport = true)
            .WithEndpoint(config => config.AllowAnonymous())
            .WithServices(services => services.AddSingleton<IETagGenerator>(new FixedETagGenerator("custom-etag-value")))
            .Build();
    }

    [Fact]
    public async Task GetById_Uses_Custom_ETag_Generator()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.Seed(
            new ProductEntity { Id = id, ProductName = "Product", UnitPrice = 10.00m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true }
        );

        // Act
        var response = await _client.GetAsync($"/api/products/{id}");

        // Assert
        response.Headers.ETag.Should().NotBeNull();
        response.Headers.ETag!.Tag.Should().Be("\"custom-etag-value\"");
    }

    public void Dispose()
    {
        _client.Dispose();
        _host.Dispose();
    }

    /// <summary>
    /// A simple test ETag generator that always returns a fixed value.
    /// </summary>
    private class FixedETagGenerator : IETagGenerator
    {
        private readonly string _fixedValue;

        public FixedETagGenerator(string fixedValue)
        {
            _fixedValue = fixedValue;
        }

        public string Generate<TEntity>(TEntity entity) where TEntity : class => $"\"{_fixedValue}\"";
        public bool Validate<TEntity>(TEntity entity, string etag) where TEntity : class => etag == $"\"{_fixedValue}\"" || etag == "*";
    }
}
