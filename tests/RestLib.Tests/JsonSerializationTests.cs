using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using RestLib.Configuration;
using RestLib.Serialization;
using RestLib.Tests.Fakes;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Tests for Story 3.1: JSON Serialization with snake_case
/// Verifies that all JSON responses use snake_case naming per Zalando Rule 118.
/// </summary>
public class JsonSerializationTests : IDisposable
{
    private readonly IHost _host;
    private readonly HttpClient _client;
    private readonly ProductEntityRepository _repository;

    public JsonSerializationTests()
    {
        _repository = new ProductEntityRepository();

        (_host, _client) = new TestHostBuilder<ProductEntity, Guid>(_repository, "/api/products")
            .WithEndpoint(config => config.AllowAnonymous())
            .Build();
    }

    #region Acceptance Criteria: Response properties are snake_case

    [Fact]
    public async Task GetById_Response_Uses_SnakeCase_PropertyNames()
    {
        // Arrange
        var id = Guid.NewGuid();
        var createdAt = new DateTime(2026, 1, 25, 10, 30, 0, DateTimeKind.Utc);
        _repository.Seed(new ProductEntity
        {
            Id = id,
            ProductName = "Test Product",
            UnitPrice = 99.99m,
            StockQuantity = 100,
            CreatedAt = createdAt,
            IsActive = true
        });

        // Act
        var response = await _client.GetAsync($"/api/products/{id}");
        var rawJson = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify snake_case property names in raw JSON
        rawJson.Should().Contain("\"id\":");
        rawJson.Should().Contain("\"product_name\":");
        rawJson.Should().Contain("\"unit_price\":");
        rawJson.Should().Contain("\"stock_quantity\":");
        rawJson.Should().Contain("\"created_at\":");
        rawJson.Should().Contain("\"is_active\":");

        // Verify PascalCase property names are NOT present
        rawJson.Should().NotContain("\"ProductName\":");
        rawJson.Should().NotContain("\"UnitPrice\":");
        rawJson.Should().NotContain("\"StockQuantity\":");
        rawJson.Should().NotContain("\"CreatedAt\":");
        rawJson.Should().NotContain("\"IsActive\":");
    }

    [Fact]
    public async Task GetAll_Response_Uses_SnakeCase_In_Items()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.Seed(new ProductEntity
        {
            Id = id,
            ProductName = "Item One",
            UnitPrice = 10.00m,
            StockQuantity = 50,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        });

        // Act
        var response = await _client.GetAsync("/api/products");
        var rawJson = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify wrapper uses snake_case
        rawJson.Should().Contain("\"items\":");

        // Verify entity properties in items use snake_case
        rawJson.Should().Contain("\"product_name\":");
        rawJson.Should().Contain("\"unit_price\":");
        rawJson.Should().Contain("\"stock_quantity\":");
    }

    [Fact]
    public async Task Create_Response_Uses_SnakeCase_PropertyNames()
    {
        // Arrange
        var product = new
        {
            product_name = "New Product",
            unit_price = 49.99,
            stock_quantity = 200,
            is_active = true
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/products", product);
        var rawJson = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        rawJson.Should().Contain("\"product_name\":");
        rawJson.Should().Contain("\"unit_price\":");
        rawJson.Should().Contain("\"stock_quantity\":");
        rawJson.Should().Contain("\"is_active\":");
    }

    [Fact]
    public async Task Update_Response_Uses_SnakeCase_PropertyNames()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.Seed(new ProductEntity
        {
            Id = id,
            ProductName = "Original",
            UnitPrice = 10.00m,
            StockQuantity = 10,
            CreatedAt = DateTime.UtcNow,
            IsActive = false
        });

        var updated = new
        {
            product_name = "Updated Product",
            unit_price = 199.99,
            stock_quantity = 500,
            is_active = true
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/products/{id}", updated);
        var rawJson = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        rawJson.Should().Contain("\"product_name\":");
        rawJson.Should().Contain("\"unit_price\":");
        rawJson.Should().Contain("\"stock_quantity\":");
        rawJson.Should().Contain("\"is_active\":");
    }

    [Fact]
    public async Task Patch_Response_Uses_SnakeCase_PropertyNames()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.Seed(new ProductEntity
        {
            Id = id,
            ProductName = "Original",
            UnitPrice = 10.00m,
            StockQuantity = 10,
            CreatedAt = DateTime.UtcNow,
            IsActive = false
        });

        var patch = new { product_name = "Patched Name" };

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/products/{id}", patch);
        var rawJson = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        rawJson.Should().Contain("\"product_name\":");
        rawJson.Should().Contain("\"unit_price\":");
    }

    #endregion

    #region Acceptance Criteria: Deserialization accepts snake_case

    [Fact]
    public async Task Create_Accepts_SnakeCase_Input()
    {
        // Arrange
        var jsonContent = new StringContent(
            """
        {
            "product_name": "Snake Case Product",
            "unit_price": 75.50,
            "stock_quantity": 150,
            "is_active": true
        }
        """,
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await _client.PostAsync("/api/products", jsonContent);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var rawJson = await response.Content.ReadAsStringAsync();
        rawJson.Should().Contain("\"product_name\":\"Snake Case Product\"");
    }

    [Fact]
    public async Task Update_Accepts_SnakeCase_Input()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.Seed(new ProductEntity
        {
            Id = id,
            ProductName = "Original",
            UnitPrice = 10.00m,
            StockQuantity = 10,
            CreatedAt = DateTime.UtcNow,
            IsActive = false
        });

        var jsonContent = new StringContent(
            """
        {
            "product_name": "Updated via snake_case",
            "unit_price": 999.99,
            "stock_quantity": 1000,
            "is_active": true
        }
        """,
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await _client.PutAsync($"/api/products/{id}", jsonContent);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var rawJson = await response.Content.ReadAsStringAsync();
        rawJson.Should().Contain("\"product_name\":\"Updated via snake_case\"");
    }

    [Fact]
    public async Task Patch_Accepts_SnakeCase_Input()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.Seed(new ProductEntity
        {
            Id = id,
            ProductName = "Original",
            UnitPrice = 10.00m,
            StockQuantity = 10,
            CreatedAt = DateTime.UtcNow,
            IsActive = false
        });

        var jsonContent = new StringContent(
            """
        {
            "product_name": "Patched via snake_case",
            "is_active": true
        }
        """,
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await _client.PatchAsync($"/api/products/{id}", jsonContent);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var rawJson = await response.Content.ReadAsStringAsync();
        rawJson.Should().Contain("\"product_name\":\"Patched via snake_case\"");
    }

    #endregion

    #region Acceptance Criteria: Nulls omitted from responses

    [Fact]
    public async Task GetById_Omits_Null_Properties()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.Seed(new ProductEntity
        {
            Id = id,
            ProductName = "Product Without Optional Fields",
            UnitPrice = 50.00m,
            StockQuantity = 25,
            CreatedAt = DateTime.UtcNow,
            LastModifiedAt = null, // Explicitly null
            OptionalDescription = null, // Explicitly null
            IsActive = true
        });

        // Act
        var response = await _client.GetAsync($"/api/products/{id}");
        var rawJson = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Null properties should NOT appear in JSON
        rawJson.Should().NotContain("\"last_modified_at\":");
        rawJson.Should().NotContain("\"optional_description\":");

        // Non-null properties should still be present
        rawJson.Should().Contain("\"product_name\":");
        rawJson.Should().Contain("\"unit_price\":");
    }

    [Fact]
    public async Task GetById_Includes_NonNull_Optional_Properties()
    {
        // Arrange
        var id = Guid.NewGuid();
        var lastModified = new DateTime(2026, 1, 20, 15, 0, 0, DateTimeKind.Utc);
        _repository.Seed(new ProductEntity
        {
            Id = id,
            ProductName = "Product With Optional Fields",
            UnitPrice = 75.00m,
            StockQuantity = 50,
            CreatedAt = DateTime.UtcNow,
            LastModifiedAt = lastModified, // Has value
            OptionalDescription = "This is a description", // Has value
            IsActive = true
        });

        // Act
        var response = await _client.GetAsync($"/api/products/{id}");
        var rawJson = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Non-null optional properties should be present
        rawJson.Should().Contain("\"last_modified_at\":");
        rawJson.Should().Contain("\"optional_description\":\"This is a description\"");
    }

    [Fact]
    public async Task GetAll_Omits_Null_Properties_In_Items()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.Seed(new ProductEntity
        {
            Id = id,
            ProductName = "Product In List",
            UnitPrice = 30.00m,
            StockQuantity = 15,
            CreatedAt = DateTime.UtcNow,
            LastModifiedAt = null,
            OptionalDescription = null,
            IsActive = true
        });

        // Act
        var response = await _client.GetAsync("/api/products");
        var rawJson = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Null properties in items should NOT appear
        rawJson.Should().NotContain("\"last_modified_at\":");
        rawJson.Should().NotContain("\"optional_description\":");
    }

    #endregion

    #region Acceptance Criteria: Configurable naming policy

    [Fact]
    public void RestLibOptions_Defaults_To_SnakeCaseLower()
    {
        // Arrange & Act
        var options = new RestLibOptions();

        // Assert
        options.JsonNamingPolicy.Should().Be(JsonNamingPolicy.SnakeCaseLower);
    }

    [Fact]
    public void RestLibOptions_Defaults_To_OmitNullValues_True()
    {
        // Arrange & Act
        var options = new RestLibOptions();

        // Assert
        options.OmitNullValues.Should().BeTrue();
    }

    [Fact]
    public void RestLibJsonOptions_Creates_Options_With_SnakeCase()
    {
        // Arrange
        var RestLibOptions = new RestLibOptions();

        // Act
        var jsonOptions = RestLibJsonOptions.Create(RestLibOptions);

        // Assert
        jsonOptions.PropertyNamingPolicy.Should().Be(JsonNamingPolicy.SnakeCaseLower);
    }

    [Fact]
    public void RestLibJsonOptions_Creates_Options_With_CaseInsensitive_Deserialization()
    {
        // Arrange
        var RestLibOptions = new RestLibOptions();

        // Act
        var jsonOptions = RestLibJsonOptions.Create(RestLibOptions);

        // Assert
        jsonOptions.PropertyNameCaseInsensitive.Should().BeTrue();
    }

    [Fact]
    public void RestLibJsonOptions_Respects_Custom_NamingPolicy()
    {
        // Arrange
        var RestLibOptions = new RestLibOptions
        {
            JsonNamingPolicy = JsonNamingPolicy.CamelCase
        };

        // Act
        var jsonOptions = RestLibJsonOptions.Create(RestLibOptions);

        // Assert
        jsonOptions.PropertyNamingPolicy.Should().Be(JsonNamingPolicy.CamelCase);
    }

    [Fact]
    public void RestLibJsonOptions_Respects_OmitNullValues_False()
    {
        // Arrange
        var RestLibOptions = new RestLibOptions
        {
            OmitNullValues = false
        };

        // Act
        var jsonOptions = RestLibJsonOptions.Create(RestLibOptions);

        // Assert
        jsonOptions.DefaultIgnoreCondition.Should().Be(System.Text.Json.Serialization.JsonIgnoreCondition.Never);
    }

    [Fact]
    public void RestLibJsonOptions_CreateDefault_Uses_SnakeCase_And_OmitsNulls()
    {
        // Act
        var jsonOptions = RestLibJsonOptions.CreateDefault();

        // Assert
        jsonOptions.PropertyNamingPolicy.Should().Be(JsonNamingPolicy.SnakeCaseLower);
        jsonOptions.DefaultIgnoreCondition.Should().Be(System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull);
    }

    #endregion

    public void Dispose()
    {
        _client.Dispose();
        _host.Dispose();
    }
}

/// <summary>
/// Tests for custom JSON configuration via AddRestLib options.
/// </summary>
public class JsonSerializationCustomConfigTests : IDisposable
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
    public async Task Custom_CamelCase_NamingPolicy_Is_Applied()
    {
        // Arrange
        SetupHost(options =>
        {
            options.JsonNamingPolicy = JsonNamingPolicy.CamelCase;
        });

        var id = Guid.NewGuid();
        _repository!.Seed(new ProductEntity
        {
            Id = id,
            ProductName = "CamelCase Test",
            UnitPrice = 100.00m,
            StockQuantity = 50,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        });

        // Act
        var response = await _client!.GetAsync($"/api/products/{id}");
        var rawJson = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Should use camelCase
        rawJson.Should().Contain("\"productName\":");
        rawJson.Should().Contain("\"unitPrice\":");
        rawJson.Should().Contain("\"stockQuantity\":");
        rawJson.Should().Contain("\"createdAt\":");
        rawJson.Should().Contain("\"isActive\":");

        // Should NOT use snake_case
        rawJson.Should().NotContain("\"product_name\":");
        rawJson.Should().NotContain("\"unit_price\":");
    }

    [Fact]
    public async Task Custom_OmitNullValues_False_Includes_Nulls()
    {
        // Arrange
        SetupHost(options =>
        {
            options.OmitNullValues = false;
        });

        var id = Guid.NewGuid();
        _repository!.Seed(new ProductEntity
        {
            Id = id,
            ProductName = "Include Nulls Test",
            UnitPrice = 100.00m,
            StockQuantity = 50,
            CreatedAt = DateTime.UtcNow,
            LastModifiedAt = null,
            OptionalDescription = null,
            IsActive = true
        });

        // Act
        var response = await _client!.GetAsync($"/api/products/{id}");
        var rawJson = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Null properties SHOULD be present (as null)
        rawJson.Should().Contain("\"last_modified_at\":null");
        rawJson.Should().Contain("\"optional_description\":null");
    }

    public void Dispose()
    {
        _client?.Dispose();
        _host?.Dispose();
    }
}
