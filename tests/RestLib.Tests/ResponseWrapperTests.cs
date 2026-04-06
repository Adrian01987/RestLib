using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using RestLib.Configuration;
using RestLib.Tests.Fakes;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Tests for Story 3.2: Standardized Response Wrappers
/// Verifies that collections are wrapped in { "items": [...] } with pagination links,
/// and single resources are returned unwrapped.
/// </summary>
public class ResponseWrapperTests : IDisposable
{
    private readonly IHost _host;
    private readonly HttpClient _client;
    private readonly ProductEntityRepository _repository;

    public ResponseWrapperTests()
    {
        _repository = new ProductEntityRepository();

        (_host, _client) = new TestHostBuilder<ProductEntity, Guid>(_repository, "/api/products")
            .WithEndpoint(config => config.AllowAnonymous())
            .Build();
    }

    #region Acceptance Criteria: GET collection never returns raw array

    [Fact]
    public async Task GetAll_Returns_Wrapped_Collection_With_Items_Property()
    {
        // Arrange
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        _repository.Seed(
            new ProductEntity { Id = id1, ProductName = "Product 1", UnitPrice = 10.00m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true },
            new ProductEntity { Id = id2, ProductName = "Product 2", UnitPrice = 20.00m, StockQuantity = 10, CreatedAt = DateTime.UtcNow, IsActive = true }
        );

        // Act
        var response = await _client.GetAsync("/api/products");
        var rawJson = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Must contain "items" wrapper
        rawJson.Should().Contain("\"items\":");

        // Should NOT be a raw array (raw arrays start with '[')
        rawJson.Trim().Should().StartWith("{");
    }

    [Fact]
    public async Task GetAll_Empty_Collection_Returns_Empty_Items_Array()
    {
        // Act
        var response = await _client.GetAsync("/api/products");
        var rawJson = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Even empty collections should have items wrapper
        rawJson.Should().Contain("\"items\":[]");
    }

    [Fact]
    public async Task GetAll_Items_Array_Contains_Entities()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.Seed(
            new ProductEntity { Id = id, ProductName = "Test Product", UnitPrice = 99.99m, StockQuantity = 50, CreatedAt = DateTime.UtcNow, IsActive = true }
        );

        // Act
        var response = await _client.GetAsync("/api/products");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<CollectionResponseDto>();
        content.Should().NotBeNull();
        content!.Items.Should().HaveCount(1);
        content.Items[0].ProductName.Should().Be("Test Product");
    }

    #endregion

    #region Acceptance Criteria: Pagination links included

    [Fact]
    public async Task GetAll_Includes_Self_Link()
    {
        // Arrange
        _repository.Seed(
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Product 1", UnitPrice = 10.00m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true }
        );

        // Act
        var response = await _client.GetAsync("/api/products");
        var rawJson = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        rawJson.Should().Contain("\"self\":");
    }

    [Fact]
    public async Task GetAll_Self_Link_Is_Absolute_Url()
    {
        // Arrange
        _repository.Seed(
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Product 1", UnitPrice = 10.00m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true }
        );

        // Act
        var response = await _client.GetAsync("/api/products");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<CollectionResponseDto>();
        content.Should().NotBeNull();
        content!.Self.Should().NotBeNull();
        content.Self.Should().StartWith("http");
        content.Self.Should().Contain("/api/products");
    }

    [Fact]
    public async Task GetAll_Self_Link_Contains_Limit_Parameter()
    {
        // Arrange
        _repository.Seed(
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Product 1", UnitPrice = 10.00m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true }
        );

        // Act
        var response = await _client.GetAsync("/api/products?limit=10");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<CollectionResponseDto>();
        content.Should().NotBeNull();
        content!.Self.Should().Contain("limit=10");
    }

    [Fact]
    public async Task GetAll_Self_Link_Contains_Cursor_When_Provided()
    {
        // Arrange
        _repository.Seed(
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Product 1", UnitPrice = 10.00m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true }
        );

        // Act - use valid base64url cursor (Story 4.1 requires valid cursor format)
        var validCursor = RestLib.Pagination.CursorEncoder.Encode(Guid.NewGuid());
        var response = await _client.GetAsync($"/api/products?cursor={Uri.EscapeDataString(validCursor)}&limit=10");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<CollectionResponseDto>();
        content.Should().NotBeNull();
        content!.Self.Should().Contain("cursor=");
        content.Self.Should().Contain("limit=10");
    }

    [Fact]
    public async Task GetAll_Next_Link_Is_Null_When_No_More_Items()
    {
        // Arrange - only one item, no more pages
        _repository.Seed(
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Product 1", UnitPrice = 10.00m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true }
        );

        // Act
        var response = await _client.GetAsync("/api/products");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<CollectionResponseDto>();
        content.Should().NotBeNull();
        content!.Next.Should().BeNull();
    }

    [Fact]
    public async Task GetAll_Prev_Link_Is_Null_On_First_Page()
    {
        // Arrange
        _repository.Seed(
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Product 1", UnitPrice = 10.00m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true }
        );

        // Act
        var response = await _client.GetAsync("/api/products");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<CollectionResponseDto>();
        content.Should().NotBeNull();
        content!.Prev.Should().BeNull();
    }

    #endregion

    #region Acceptance Criteria: Single resource returns unwrapped

    [Fact]
    public async Task GetById_Returns_Unwrapped_Entity()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.Seed(
            new ProductEntity { Id = id, ProductName = "Single Product", UnitPrice = 49.99m, StockQuantity = 25, CreatedAt = DateTime.UtcNow, IsActive = true }
        );

        // Act
        var response = await _client.GetAsync($"/api/products/{id}");
        var rawJson = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Should NOT contain "items" wrapper
        rawJson.Should().NotContain("\"items\":");

        // Should contain the entity properties directly
        rawJson.Should().Contain("\"product_name\":\"Single Product\"");
        rawJson.Should().Contain("\"unit_price\":49.99");
    }

    [Fact]
    public async Task Create_Returns_Unwrapped_Entity()
    {
        // Arrange
        var newProduct = new
        {
            product_name = "Created Product",
            unit_price = 75.00,
            stock_quantity = 100,
            is_active = true
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/products", newProduct);
        var rawJson = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        // Should NOT contain "items" wrapper
        rawJson.Should().NotContain("\"items\":");

        // Should contain the entity properties directly
        rawJson.Should().Contain("\"product_name\":");
    }

    [Fact]
    public async Task Update_Returns_Unwrapped_Entity()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.Seed(
            new ProductEntity { Id = id, ProductName = "Original", UnitPrice = 10.00m, StockQuantity = 10, CreatedAt = DateTime.UtcNow, IsActive = false }
        );

        var updatedProduct = new
        {
            product_name = "Updated Product",
            unit_price = 150.00,
            stock_quantity = 200,
            is_active = true
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/products/{id}", updatedProduct);
        var rawJson = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Should NOT contain "items" wrapper
        rawJson.Should().NotContain("\"items\":");

        // Should contain the entity properties directly
        rawJson.Should().Contain("\"product_name\":");
    }

    [Fact]
    public async Task Patch_Returns_Unwrapped_Entity()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.Seed(
            new ProductEntity { Id = id, ProductName = "Original", UnitPrice = 10.00m, StockQuantity = 10, CreatedAt = DateTime.UtcNow, IsActive = false }
        );

        var patch = new { product_name = "Patched Product" };

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/products/{id}", patch);
        var rawJson = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Should NOT contain "items" wrapper
        rawJson.Should().NotContain("\"items\":");

        // Should contain the entity properties directly
        rawJson.Should().Contain("\"product_name\":");
    }

    #endregion

    #region Pagination Parameters

    [Fact]
    public async Task GetAll_Accepts_Limit_Parameter()
    {
        // Arrange
        for (int i = 0; i < 10; i++)
        {
            _repository.Seed(
                new ProductEntity { Id = Guid.NewGuid(), ProductName = $"Product {i}", UnitPrice = 10.00m * i, StockQuantity = i * 5, CreatedAt = DateTime.UtcNow, IsActive = true }
            );
        }

        // Act
        var response = await _client.GetAsync("/api/products?limit=5");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<CollectionResponseDto>();
        content.Should().NotBeNull();
        content!.Items.Should().HaveCount(5);
    }

    [Fact]
    public async Task GetAll_Uses_Default_Limit_When_Not_Specified()
    {
        // Arrange - add more items than default page size (20)
        for (int i = 0; i < 25; i++)
        {
            _repository.Seed(
                new ProductEntity { Id = Guid.NewGuid(), ProductName = $"Product {i}", UnitPrice = 10.00m, StockQuantity = 1, CreatedAt = DateTime.UtcNow, IsActive = true }
            );
        }

        // Act
        var response = await _client.GetAsync("/api/products");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<CollectionResponseDto>();
        content.Should().NotBeNull();
        content!.Items.Should().HaveCount(20); // Default limit
    }

    [Fact]
    public async Task GetAll_Rejects_Limit_Above_MaxPageSize()
    {
        // Arrange - add many items
        for (int i = 0; i < 150; i++)
        {
            _repository.Seed(
                new ProductEntity { Id = Guid.NewGuid(), ProductName = $"Product {i}", UnitPrice = 10.00m, StockQuantity = 1, CreatedAt = DateTime.UtcNow, IsActive = true }
            );
        }

        // Act - request more than max (100)
        var response = await _client.GetAsync("/api/products?limit=200");

        // Assert - Story 4.1: invalid limit returns 400
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task GetAll_Rejects_Limit_Below_Minimum()
    {
        // Arrange
        _repository.Seed(
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Product 1", UnitPrice = 10.00m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true }
        );

        // Act - request invalid limit
        var response = await _client.GetAsync("/api/products?limit=0");

        // Assert - Story 4.1: invalid limit returns 400
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    #endregion

    public void Dispose()
    {
        _client.Dispose();
        _host.Dispose();
    }

    // DTO for deserializing collection responses
    private record CollectionResponseDto(
        List<ProductEntityDto> Items,
        string? Self,
        string? Next,
        string? Prev
    );

    private record ProductEntityDto(
        Guid Id,
        [property: JsonPropertyName("product_name")] string ProductName,
        [property: JsonPropertyName("unit_price")] decimal UnitPrice,
        [property: JsonPropertyName("stock_quantity")] int StockQuantity,
        [property: JsonPropertyName("created_at")] DateTime CreatedAt,
        [property: JsonPropertyName("is_active")] bool IsActive
    );
}

/// <summary>
/// Tests for disabling pagination links via configuration.
/// </summary>
public class ResponseWrapperConfigTests : IDisposable
{
    private IHost? _host;
    private HttpClient? _client;
    private ProductEntityRepository? _repository;

    private void SetupHost(Action<RestLibOptions>? configure = null)
    {
        _repository = new ProductEntityRepository();

        (_host, _client) = new TestHostBuilder<ProductEntity, Guid>(_repository, "/api/products")
            .WithOptions(configure ?? (_ => { }))
            .WithEndpoint(config => config.AllowAnonymous())
            .Build();
    }

    [Fact]
    public async Task GetAll_Omits_Pagination_Links_When_Disabled()
    {
        // Arrange
        SetupHost(options =>
        {
            options.IncludePaginationLinks = false;
        });

        _repository!.Seed(
            new ProductEntity { Id = Guid.NewGuid(), ProductName = "Product 1", UnitPrice = 10.00m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true }
        );

        // Act
        var response = await _client!.GetAsync("/api/products");
        var rawJson = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Should still have items wrapper
        rawJson.Should().Contain("\"items\":");

        // But self/next/prev should be null (omitted due to OmitNullValues)
        rawJson.Should().NotContain("\"self\":");
        rawJson.Should().NotContain("\"next\":");
        rawJson.Should().NotContain("\"prev\":");
    }

    [Fact]
    public async Task GetAll_Custom_DefaultPageSize_Is_Applied()
    {
        // Arrange
        SetupHost(options =>
        {
            options.DefaultPageSize = 5;
        });

        for (int i = 0; i < 10; i++)
        {
            _repository!.Seed(
                new ProductEntity { Id = Guid.NewGuid(), ProductName = $"Product {i}", UnitPrice = 10.00m, StockQuantity = 1, CreatedAt = DateTime.UtcNow, IsActive = true }
            );
        }

        // Act - no limit specified
        var response = await _client!.GetAsync("/api/products");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = doc.RootElement.GetProperty("items");
        items.GetArrayLength().Should().Be(5); // Custom default
    }

    [Fact]
    public async Task GetAll_Custom_MaxPageSize_Is_Enforced()
    {
        // Arrange
        SetupHost(options =>
        {
            options.DefaultPageSize = 10;
            options.MaxPageSize = 10;
        });

        for (int i = 0; i < 20; i++)
        {
            _repository!.Seed(
                new ProductEntity { Id = Guid.NewGuid(), ProductName = $"Product {i}", UnitPrice = 10.00m, StockQuantity = 1, CreatedAt = DateTime.UtcNow, IsActive = true }
            );
        }

        // Act - request more than custom max
        var response = await _client!.GetAsync("/api/products?limit=50");

        // Assert - Story 4.1: invalid limit returns 400
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    public void Dispose()
    {
        _client?.Dispose();
        _host?.Dispose();
    }
}
