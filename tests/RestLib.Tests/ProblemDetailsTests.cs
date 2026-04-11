using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using RestLib.Configuration;
using RestLib.FieldSelection;
using RestLib.Filtering;
using RestLib.Responses;
using RestLib.Sorting;
using RestLib.Tests.Fakes;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Tests for Story 3.3: Problem Details for Errors
/// Verifies that all error responses use RFC 9457 Problem Details format.
/// </summary>
[Trait("Type", "Integration")]
[Trait("Feature", "ProblemDetails")]
public class ProblemDetailsTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private ProductEntityRepository _repository = null!;

    public async Task InitializeAsync()
    {
        _repository = new ProductEntityRepository();

        (_host, _client) = await new TestHostBuilder<ProductEntity, Guid>(_repository, "/api/products")
            .WithEndpoint(config => config.AllowAnonymous())
            .BuildAsync();
    }

    #region Acceptance Criteria: All 4xx/5xx use Problem Details

    [Fact]
    public async Task GetById_NotFound_Returns_ProblemDetails()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/products/{nonExistentId}");

        // Assert
        await response.ShouldBeProblemDetails(
            HttpStatusCode.NotFound,
            ProblemTypes.NotFound,
            expectedTitle: "Resource Not Found");
    }

    [Fact]
    public async Task Update_NotFound_Returns_ProblemDetails()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();
        var entity = new { product_name = "Test", unit_price = 10.00, stock_quantity = 5, is_active = true };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/products/{nonExistentId}", entity);

        // Assert
        await response.ShouldBeProblemDetails(HttpStatusCode.NotFound, ProblemTypes.NotFound);
    }

    [Fact]
    public async Task Patch_NotFound_Returns_ProblemDetails()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();
        var patch = new { product_name = "Updated" };

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/products/{nonExistentId}", patch);

        // Assert
        await response.ShouldBeProblemDetails(HttpStatusCode.NotFound, ProblemTypes.NotFound);
    }

    [Fact]
    public async Task Delete_NotFound_Returns_ProblemDetails()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await _client.DeleteAsync($"/api/products/{nonExistentId}");

        // Assert
        await response.ShouldBeProblemDetails(HttpStatusCode.NotFound, ProblemTypes.NotFound);
    }

    [Fact]
    public async Task GetById_NotFound_ContentType_Is_ApplicationProblemJson()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/products/{nonExistentId}");

        // Assert
        response.Content.Headers.ContentType.Should().NotBeNull();
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    #endregion

    #region Acceptance Criteria: Types are relative URIs

    [Fact]
    public async Task NotFound_Type_Is_RelativeUri()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/products/{nonExistentId}");
        var problem = await response.Content.ReadFromJsonAsync<RestLibProblemDetails>();

        // Assert
        problem.Should().NotBeNull();
        problem!.Type.Should().StartWith("/");
        problem.Type.Should().Be("/problems/not-found");
    }

    [Fact]
    public async Task NotFound_Type_Does_Not_Contain_Scheme()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/products/{nonExistentId}");
        var problem = await response.Content.ReadFromJsonAsync<RestLibProblemDetails>();

        // Assert
        problem.Should().NotBeNull();
        problem!.Type.Should().NotContain("http://");
        problem.Type.Should().NotContain("https://");
    }

    #endregion

    #region Detail and Instance fields

    [Fact]
    public async Task NotFound_Detail_Contains_EntityName_And_Id()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/products/{nonExistentId}");

        // Assert
        var problem = await response.ShouldBeProblemDetails(HttpStatusCode.NotFound, ProblemTypes.NotFound);
        problem.Detail.Should().Contain("ProductEntity");
        problem.Detail.Should().Contain(nonExistentId.ToString());
    }

    [Fact]
    public async Task NotFound_Detail_Contains_CleanTypeName_Not_InternalEndpointName()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/products/{nonExistentId}");

        // Assert — detail should contain the clean type name, not the internal endpoint name with route suffix
        var problem = await response.ShouldBeProblemDetails(HttpStatusCode.NotFound, ProblemTypes.NotFound);
        problem.Detail.Should().Contain("ProductEntity");
        problem.Detail.Should().NotContain("ProductEntity_");
    }

    [Fact]
    public async Task NotFound_Instance_Contains_RequestPath()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/products/{nonExistentId}");

        // Assert
        var problem = await response.ShouldBeProblemDetails(HttpStatusCode.NotFound, ProblemTypes.NotFound);
        problem.Instance.Should().Contain("/api/products/");
        problem.Instance.Should().Contain(nonExistentId.ToString());
    }

    [Fact]
    public async Task Update_NotFound_Instance_Contains_RequestPath()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();
        var entity = new { product_name = "Test", unit_price = 10.00, stock_quantity = 5, is_active = true };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/products/{nonExistentId}", entity);

        // Assert
        var problem = await response.ShouldBeProblemDetails(HttpStatusCode.NotFound, ProblemTypes.NotFound);
        problem.Instance.Should().Contain("/api/products/");
    }

    #endregion

    #region Acceptance Criteria: Validation errors include field details

    [Fact]
    public void ProblemDetailsFactory_ValidationFailed_IncludesErrors()
    {
        // Arrange
        var errors = new Dictionary<string, string[]>
        {
            { "name", new[] { "The Name field is required." } },
            { "price", new[] { "Price must be greater than 0." } }
        };

        // Act
        var problem = ProblemDetailsFactory.ValidationFailed(errors, "/api/products");

        // Assert
        problem.Type.Should().Be(ProblemTypes.ValidationFailed);
        problem.Title.Should().Be("Validation Failed");
        problem.Status.Should().Be(400);
        problem.Errors.Should().NotBeNull();
        problem.Errors.Should().ContainKey("name");
        problem.Errors.Should().ContainKey("price");
    }

    [Fact]
    public void ProblemDetailsFactory_ValidationFailed_Errors_Contain_Messages()
    {
        // Arrange
        var errors = new Dictionary<string, string[]>
        {
            { "email", new[] { "Invalid email format.", "Email is required." } }
        };

        // Act
        var problem = ProblemDetailsFactory.ValidationFailed(errors, "/api/users");

        // Assert
        problem.Errors!["email"].Should().Contain("Invalid email format.");
        problem.Errors["email"].Should().Contain("Email is required.");
    }

    [Fact]
    public void ProblemDetailsFactory_ValidationFailed_Uses_SnakeCase_FieldNames()
    {
        // Arrange - simulating snake_case field names
        var errors = new Dictionary<string, string[]>
        {
            { "product_name", new[] { "Product name is required." } },
            { "unit_price", new[] { "Unit price must be positive." } }
        };

        // Act
        var problem = ProblemDetailsFactory.ValidationFailed(errors);

        // Assert
        problem.Errors.Should().ContainKey("product_name");
        problem.Errors.Should().ContainKey("unit_price");
    }

    #endregion

    #region Problem Details Factory Tests

    [Fact]
    public void ProblemDetailsFactory_NotFound_CreatesCorrectProblem()
    {
        // Act
        var problem = ProblemDetailsFactory.NotFound("Product", 123, "/api/products/123");

        // Assert
        problem.Type.Should().Be(ProblemTypes.NotFound);
        problem.Title.Should().Be("Resource Not Found");
        problem.Status.Should().Be(404);
        problem.Detail.Should().Contain("Product");
        problem.Detail.Should().Contain("123");
        problem.Instance.Should().Be("/api/products/123");
    }

    [Fact]
    public void ProblemDetailsFactory_BadRequest_CreatesCorrectProblem()
    {
        // Act
        var problem = ProblemDetailsFactory.BadRequest("Invalid cursor format", "/api/products");

        // Assert
        problem.Type.Should().Be(ProblemTypes.BadRequest);
        problem.Title.Should().Be("Bad Request");
        problem.Status.Should().Be(400);
        problem.Detail.Should().Be("Invalid cursor format");
    }

    [Fact]
    public void ProblemDetailsFactory_Conflict_CreatesCorrectProblem()
    {
        // Act
        var problem = ProblemDetailsFactory.Conflict("Resource already exists", "/api/products");

        // Assert
        problem.Type.Should().Be(ProblemTypes.Conflict);
        problem.Title.Should().Be("Conflict");
        problem.Status.Should().Be(409);
    }

    [Fact]
    public void ProblemDetailsFactory_PreconditionFailed_CreatesCorrectProblem()
    {
        // Act
        var problem = ProblemDetailsFactory.PreconditionFailed("ETag mismatch", "/api/products/1");

        // Assert
        problem.Type.Should().Be(ProblemTypes.PreconditionFailed);
        problem.Title.Should().Be("Precondition Failed");
        problem.Status.Should().Be(412);
    }

    [Fact]
    public void ProblemDetailsFactory_InternalError_CreatesCorrectProblem()
    {
        // Act
        var problem = ProblemDetailsFactory.InternalError(null, "/api/products");

        // Assert
        problem.Type.Should().Be(ProblemTypes.InternalError);
        problem.Title.Should().Be("Internal Server Error");
        problem.Status.Should().Be(500);
        problem.Detail.Should().Be("An unexpected error occurred.");
    }

    [Fact]
    public void ProblemDetailsFactory_InternalError_WithCustomDetail()
    {
        // Act
        var problem = ProblemDetailsFactory.InternalError("Database connection failed", "/api/products");

        // Assert
        problem.Detail.Should().Be("Database connection failed");
    }

    #endregion

    #region Problem Types Constants Tests

    [Fact]
    public void ProblemTypes_NotFound_IsRelativeUri()
    {
        // Act & Assert
        ProblemTypes.NotFound.Should().Be("/problems/not-found");
    }

    [Fact]
    public void ProblemTypes_ValidationFailed_IsRelativeUri()
    {
        // Act & Assert
        ProblemTypes.ValidationFailed.Should().Be("/problems/validation-failed");
    }

    [Fact]
    public void ProblemTypes_BadRequest_IsRelativeUri()
    {
        // Act & Assert
        ProblemTypes.BadRequest.Should().Be("/problems/bad-request");
    }

    [Fact]
    public void ProblemTypes_Conflict_IsRelativeUri()
    {
        // Act & Assert
        ProblemTypes.Conflict.Should().Be("/problems/conflict");
    }

    [Fact]
    public void ProblemTypes_PreconditionFailed_IsRelativeUri()
    {
        // Act & Assert
        ProblemTypes.PreconditionFailed.Should().Be("/problems/precondition-failed");
    }

    [Fact]
    public void ProblemTypes_Unauthorized_IsRelativeUri()
    {
        // Act & Assert
        ProblemTypes.Unauthorized.Should().Be("/problems/unauthorized");
    }

    [Fact]
    public void ProblemTypes_Forbidden_IsRelativeUri()
    {
        // Act & Assert
        ProblemTypes.Forbidden.Should().Be("/problems/forbidden");
    }

    [Fact]
    public void ProblemTypes_InternalError_IsRelativeUri()
    {
        // Act & Assert
        ProblemTypes.InternalError.Should().Be("/problems/internal-error");
    }

    #endregion

    #region JSON Serialization Tests

    [Fact]
    public async Task ProblemDetails_Uses_Lowercase_PropertyNames()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/products/{nonExistentId}");
        var rawJson = await response.Content.ReadAsStringAsync();

        // Assert - verify lowercase property names (RFC 9457 standard)
        rawJson.Should().Contain("\"type\":");
        rawJson.Should().Contain("\"title\":");
        rawJson.Should().Contain("\"status\":");
        rawJson.Should().Contain("\"detail\":");
        rawJson.Should().Contain("\"instance\":");
    }

    [Fact]
    public void ProblemDetails_OmitsNullProperties()
    {
        // Arrange
        var problem = new RestLibProblemDetails
        {
            Type = ProblemTypes.NotFound,
            Title = "Not Found",
            Status = 404,
            Detail = null,
            Instance = null,
            Errors = null
        };

        // Act
        var json = JsonSerializer.Serialize(problem);

        // Assert - null properties should not appear
        json.Should().NotContain("\"detail\"");
        json.Should().NotContain("\"instance\"");
        json.Should().NotContain("\"errors\"");
    }

    [Fact]
    public void ProblemDetails_IncludesNonNullProperties()
    {
        // Arrange
        var problem = new RestLibProblemDetails
        {
            Type = ProblemTypes.NotFound,
            Title = "Not Found",
            Status = 404,
            Detail = "Resource not found",
            Instance = "/api/products/123"
        };

        // Act
        var json = JsonSerializer.Serialize(problem);

        // Assert
        json.Should().Contain("\"detail\":\"Resource not found\"");
        json.Should().Contain("\"instance\":\"/api/products/123\"");
    }

    [Fact]
    public void ProblemDetails_Serializes_Errors_Dictionary()
    {
        // Arrange
        var errors = new Dictionary<string, string[]>
        {
            { "field1", new[] { "Error 1" } }
        };
        var problem = new RestLibProblemDetails
        {
            Type = ProblemTypes.ValidationFailed,
            Title = "Validation Failed",
            Status = 400,
            Errors = errors
        };

        // Act
        var json = JsonSerializer.Serialize(problem);

        // Assert
        json.Should().Contain("\"errors\":");
        json.Should().Contain("\"field1\":");
        json.Should().Contain("\"Error 1\"");
    }

    [Fact]
    public void ProblemDetails_Serializes_ExtensionData()
    {
        // Arrange
        var problem = new RestLibProblemDetails
        {
            Type = ProblemTypes.InternalError,
            Title = "Internal Server Error",
            Status = 500,
            Extensions = new Dictionary<string, JsonElement>
            {
                ["trace_id"] = JsonSerializer.SerializeToElement("abc-123"),
                ["retry_after"] = JsonSerializer.SerializeToElement(30),
            },
        };

        // Act
        var json = JsonSerializer.Serialize(problem);

        // Assert — extension members appear as top-level properties
        json.Should().Contain("\"trace_id\":\"abc-123\"");
        json.Should().Contain("\"retry_after\":30");
    }

    [Fact]
    public void ProblemDetails_OmitsExtensionData_WhenNull()
    {
        // Arrange
        var problem = new RestLibProblemDetails
        {
            Type = ProblemTypes.NotFound,
            Title = "Not Found",
            Status = 404,
            Extensions = null,
        };

        // Act
        var json = JsonSerializer.Serialize(problem);

        // Assert — no extra properties appear
        var doc = JsonDocument.Parse(json);
        var props = doc.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        props.Should().BeEquivalentTo(new[] { "type", "title", "status" });
    }

    [Fact]
    public void ProblemDetails_Deserializes_UnknownMembers_Into_ExtensionData()
    {
        // Arrange — JSON with a custom extension member
        var json = """
            {
                "type": "/problems/internal-error",
                "title": "Internal Server Error",
                "status": 500,
                "trace_id": "abc-123",
                "retry_after": 30
            }
            """;

        // Act
        var problem = JsonSerializer.Deserialize<RestLibProblemDetails>(json);

        // Assert
        problem.Should().NotBeNull();
        problem!.Extensions.Should().NotBeNull();
        problem.Extensions.Should().ContainKey("trace_id");
        problem.Extensions.Should().ContainKey("retry_after");
        problem.Extensions!["trace_id"].GetString().Should().Be("abc-123");
        problem.Extensions["retry_after"].GetInt32().Should().Be(30);
    }

    [Fact]
    public void ProblemDetails_RoundTrips_ExtensionData()
    {
        // Arrange
        var original = new RestLibProblemDetails
        {
            Type = ProblemTypes.BadRequest,
            Title = "Bad Request",
            Status = 400,
            Detail = "Something went wrong",
            Extensions = new Dictionary<string, JsonElement>
            {
                ["correlation_id"] = JsonSerializer.SerializeToElement("corr-456"),
                ["is_transient"] = JsonSerializer.SerializeToElement(true),
            },
        };

        // Act
        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<RestLibProblemDetails>(json);

        // Assert — known properties round-trip
        deserialized.Should().NotBeNull();
        deserialized!.Type.Should().Be(original.Type);
        deserialized.Title.Should().Be(original.Title);
        deserialized.Status.Should().Be(original.Status);
        deserialized.Detail.Should().Be(original.Detail);

        // Assert — extension data round-trips
        deserialized.Extensions.Should().NotBeNull();
        deserialized.Extensions.Should().HaveCount(2);
        deserialized.Extensions!["correlation_id"].GetString().Should().Be("corr-456");
        deserialized.Extensions["is_transient"].GetBoolean().Should().BeTrue();
    }

    #endregion

    #region Success Responses Do Not Use Problem Details

    [Fact]
    public async Task GetById_Success_DoesNot_Return_ProblemDetails()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.Seed(
            new ProductEntity { Id = id, ProductName = "Test", UnitPrice = 10.00m, StockQuantity = 5, CreatedAt = DateTime.UtcNow, IsActive = true }
        );

        // Act
        var response = await _client.GetAsync($"/api/products/{id}");
        var rawJson = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        rawJson.Should().NotContain("\"type\":\"/problems/");
    }

    [Fact]
    public async Task Create_Success_DoesNot_Return_ProblemDetails()
    {
        // Arrange
        var newProduct = new { product_name = "New Product", unit_price = 25.00, stock_quantity = 10, is_active = true };

        // Act
        var response = await _client.PostAsJsonAsync("/api/products", newProduct);
        var rawJson = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        rawJson.Should().NotContain("\"type\":\"/problems/");
    }

    #endregion

    #region Duplicate Key Safety Tests

    [Fact]
    public void InvalidFilters_DuplicateParameterNames_DoesNotThrow()
    {
        // Arrange — two errors for the same parameter
        var errors = new List<FilterValidationError>
    {
      new()
      {
        ParameterName = "price",
        ProvidedValue = "abc",
        ExpectedType = typeof(decimal),
        Message = "Value 'abc' is not a valid decimal."
      },
      new()
      {
        ParameterName = "price",
        ProvidedValue = "xyz",
        ExpectedType = typeof(decimal),
        Message = "Value 'xyz' is not a valid decimal."
      }
    };

        // Act
        var problem = ProblemDetailsFactory.InvalidFilters(errors);

        // Assert
        problem.Errors.Should().ContainKey("price");
        problem.Errors!["price"].Should().HaveCount(2);
        problem.Errors["price"].Should().Contain("Value 'abc' is not a valid decimal.");
        problem.Errors["price"].Should().Contain("Value 'xyz' is not a valid decimal.");
    }

    [Fact]
    public void InvalidFilters_MixedDuplicateAndUniqueKeys_GroupsCorrectly()
    {
        // Arrange
        var errors = new List<FilterValidationError>
    {
      new()
      {
        ParameterName = "price",
        ProvidedValue = "abc",
        ExpectedType = typeof(decimal),
        Message = "Value 'abc' is not a valid decimal."
      },
      new()
      {
        ParameterName = "quantity",
        ProvidedValue = "none",
        ExpectedType = typeof(int),
        Message = "Value 'none' is not a valid integer."
      },
      new()
      {
        ParameterName = "price",
        ProvidedValue = "---",
        ExpectedType = typeof(decimal),
        Message = "Value '---' is not a valid decimal."
      }
    };

        // Act
        var problem = ProblemDetailsFactory.InvalidFilters(errors);

        // Assert
        problem.Errors.Should().HaveCount(2);
        problem.Errors!["price"].Should().HaveCount(2);
        problem.Errors["quantity"].Should().HaveCount(1);
    }

    [Fact]
    public void InvalidSort_DuplicateFieldNames_DoesNotThrow()
    {
        // Arrange — two errors for the same sort field
        var errors = new List<SortValidationError>
    {
      new()
      {
        Field = "unknown_field",
        Message = "'unknown_field' is not a sortable field."
      },
      new()
      {
        Field = "unknown_field",
        Message = "'unknown_field' appears more than once."
      }
    };

        // Act
        var problem = ProblemDetailsFactory.InvalidSort(errors);

        // Assert
        problem.Errors.Should().ContainKey("unknown_field");
        problem.Errors!["unknown_field"].Should().HaveCount(2);
        problem.Errors["unknown_field"].Should().Contain("'unknown_field' is not a sortable field.");
        problem.Errors["unknown_field"].Should().Contain("'unknown_field' appears more than once.");
    }

    [Fact]
    public void InvalidFields_DuplicateFieldNames_DoesNotThrow()
    {
        // Arrange — two errors for the same field
        var errors = new List<FieldSelectionValidationError>
    {
      new()
      {
        Field = "secret",
        Message = "'secret' is not a selectable field."
      },
      new()
      {
        Field = "secret",
        Message = "'secret' has been deprecated."
      }
    };

        // Act
        var problem = ProblemDetailsFactory.InvalidFields(errors);

        // Assert
        problem.Errors.Should().ContainKey("secret");
        problem.Errors!["secret"].Should().HaveCount(2);
        problem.Errors["secret"].Should().Contain("'secret' is not a selectable field.");
        problem.Errors["secret"].Should().Contain("'secret' has been deprecated.");
    }

    #endregion

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }
}

/// <summary>
/// Tests for <see cref="ProblemTypes.Resolve"/> and the configurable
/// <see cref="RestLibOptions.ProblemTypeBaseUri"/> option.
/// </summary>
[Collection("ProblemTypeBaseUri")]
[Trait("Type", "Unit")]
[Trait("Feature", "ProblemDetails")]
public class ProblemTypeResolveTests : IAsyncLifetime
{
    /// <summary>
    /// Initializes the test. No setup required.
    /// </summary>
    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>
    /// Reset the static base URI after each test to prevent leaking state.
    /// </summary>
    public Task DisposeAsync()
    {
        ProblemTypes.Reset();
        return Task.CompletedTask;
    }

    [Fact]
    [Trait("Category", "Story3.3")]
    public void Resolve_WithoutBaseUri_ReturnsRelativePath()
    {
        // Arrange
        ProblemTypes.Reset();

        // Act & Assert
        ProblemTypes.Resolve(ProblemTypes.NotFound).Should().Be("/problems/not-found");
        ProblemTypes.Resolve(ProblemTypes.ValidationFailed).Should().Be("/problems/validation-failed");
        ProblemTypes.Resolve(ProblemTypes.InternalError).Should().Be("/problems/internal-error");
    }

    [Fact]
    [Trait("Category", "Story3.3")]
    public void Resolve_WithBaseUri_ReturnsAbsoluteUri()
    {
        // Arrange
        ProblemTypes.Configure(new Uri("https://api.example.com"));

        // Act & Assert
        ProblemTypes.Resolve(ProblemTypes.NotFound).Should().Be("https://api.example.com/problems/not-found");
        ProblemTypes.Resolve(ProblemTypes.ValidationFailed).Should().Be("https://api.example.com/problems/validation-failed");
        ProblemTypes.Resolve(ProblemTypes.InternalError).Should().Be("https://api.example.com/problems/internal-error");
    }

    [Fact]
    [Trait("Category", "Story3.3")]
    public void Resolve_WithTrailingSlashBaseUri_DoesNotDoubleSlash()
    {
        // Arrange
        ProblemTypes.Configure(new Uri("https://api.example.com/"));

        // Act
        var resolved = ProblemTypes.Resolve(ProblemTypes.NotFound);

        // Assert — no double slash between base and relative path
        resolved.Should().Be("https://api.example.com/problems/not-found");
    }

    [Fact]
    [Trait("Category", "Story3.3")]
    public void Factory_NotFound_WithBaseUri_ProducesAbsoluteType()
    {
        // Arrange
        ProblemTypes.Configure(new Uri("https://api.example.com"));

        // Act
        var problem = ProblemDetailsFactory.NotFound("Product", Guid.Empty);

        // Assert
        problem.Type.Should().Be("https://api.example.com/problems/not-found");
    }

    [Fact]
    [Trait("Category", "Story3.3")]
    public void Factory_AllMethods_WithBaseUri_ProduceAbsoluteTypes()
    {
        // Arrange
        ProblemTypes.Configure(new Uri("https://docs.example.com"));
        var errors = new Dictionary<string, string[]> { ["f"] = ["err"] };
        var filterErrors = new List<FilterValidationError>
        {
            new() { ParameterName = "p", ProvidedValue = "v", ExpectedType = typeof(int), Message = "bad" },
        };
        var sortErrors = new List<SortValidationError>
        {
            new() { Field = "f", Message = "bad" },
        };
        var fieldErrors = new List<FieldSelectionValidationError>
        {
            new() { Field = "f", Message = "bad" },
        };

        // Act & Assert — every factory method should produce an absolute type URI
        ProblemDetailsFactory.NotFound("E", 1).Type
            .Should().StartWith("https://docs.example.com/problems/");
        ProblemDetailsFactory.ValidationFailed(errors).Type
            .Should().StartWith("https://docs.example.com/problems/");
        ProblemDetailsFactory.BadRequest("x").Type
            .Should().StartWith("https://docs.example.com/problems/");
        ProblemDetailsFactory.InvalidCursor("x").Type
            .Should().StartWith("https://docs.example.com/problems/");
        ProblemDetailsFactory.InvalidLimit(0, 1, 100).Type
            .Should().StartWith("https://docs.example.com/problems/");
        ProblemDetailsFactory.InvalidFilters(filterErrors).Type
            .Should().StartWith("https://docs.example.com/problems/");
        ProblemDetailsFactory.InvalidSort(sortErrors).Type
            .Should().StartWith("https://docs.example.com/problems/");
        ProblemDetailsFactory.InvalidFields(fieldErrors).Type
            .Should().StartWith("https://docs.example.com/problems/");
        ProblemDetailsFactory.InvalidBatchRequest("x").Type
            .Should().StartWith("https://docs.example.com/problems/");
        ProblemDetailsFactory.BatchSizeExceeded(10, 5).Type
            .Should().StartWith("https://docs.example.com/problems/");
        ProblemDetailsFactory.BatchActionNotEnabled("x", ["y"]).Type
            .Should().StartWith("https://docs.example.com/problems/");
        ProblemDetailsFactory.Conflict("x").Type
            .Should().StartWith("https://docs.example.com/problems/");
        ProblemDetailsFactory.PreconditionFailed("x").Type
            .Should().StartWith("https://docs.example.com/problems/");
        ProblemDetailsFactory.InternalError().Type
            .Should().StartWith("https://docs.example.com/problems/");
        ProblemDetailsFactory.HookShortCircuit(500).Type
            .Should().StartWith("https://docs.example.com/problems/");
    }

    [Fact]
    [Trait("Category", "Story3.3")]
    public void Factory_NotFound_Serialized_WithBaseUri_ContainsAbsoluteType()
    {
        // Arrange
        ProblemTypes.Configure(new Uri("https://api.example.com"));
        var problem = ProblemDetailsFactory.NotFound("Product", Guid.Empty);

        // Act
        var json = JsonSerializer.Serialize(problem);

        // Assert — the serialized JSON contains the absolute type URI
        json.Should().Contain("\"type\":\"https://api.example.com/problems/not-found\"");
    }
}

/// <summary>
/// Tests for <see cref="RestLibServiceExtensions.AddRestLib"/> with valid
/// <see cref="RestLibOptions.ProblemTypeBaseUri"/> values. These live in the
/// "ProblemTypeBaseUri" collection so they do not run in parallel with
/// <see cref="ProblemTypeResolveTests"/>, avoiding static-state races on
/// <see cref="ProblemTypes"/>.
/// </summary>
[Collection("ProblemTypeBaseUri")]
[Trait("Type", "Unit")]
[Trait("Feature", "Configuration")]
public class ProblemTypeBaseUriRegistrationTests : IAsyncLifetime
{
    /// <summary>
    /// Initializes the test. No setup required.
    /// </summary>
    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>
    /// Reset the static base URI after each test to prevent leaking state.
    /// </summary>
    public Task DisposeAsync()
    {
        ProblemTypes.Reset();
        return Task.CompletedTask;
    }

    [Fact]
    [Trait("Category", "Story1.3")]
    public void AddRestLib_ValidHttpsProblemTypeBaseUri_DoesNotThrow()
    {
        // Arrange
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();

        // Act
        var act = () => services.AddRestLib(o =>
            o.ProblemTypeBaseUri = new Uri("https://api.example.com"));

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    [Trait("Category", "Story1.3")]
    public void AddRestLib_ValidHttpProblemTypeBaseUri_DoesNotThrow()
    {
        // Arrange
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();

        // Act
        var act = () => services.AddRestLib(o =>
            o.ProblemTypeBaseUri = new Uri("http://localhost:5000"));

        // Assert
        act.Should().NotThrow();
    }
}
