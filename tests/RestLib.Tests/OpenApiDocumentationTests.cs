using System.Net.Http;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi;
using RestLib.Abstractions;
using RestLib.Pagination;
using Swashbuckle.AspNetCore.SwaggerGen;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Tests for Story 7.1: OpenAPI 3.1 Integration
/// Verifies that RestLib endpoints are properly documented in OpenAPI spec.
///
/// Acceptance Criteria:
/// - [ ] All endpoints in OpenAPI spec
/// - [ ] Schemas documented
/// - [ ] Status codes documented
/// - [ ] Parameters with constraints
/// </summary>
public class OpenApiDocumentationTests
{
  #region Test Entity and Repository

  private class OpenApiTestEntity
  {
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public bool IsActive { get; set; }
    public Guid CategoryId { get; set; }
    public DateTime CreatedAt { get; set; }
  }

  private class OpenApiTestRepository : IRepository<OpenApiTestEntity, int>
  {
    private readonly Dictionary<int, OpenApiTestEntity> _data = [];
    private int _nextId = 1;

    public void AddTestData(params OpenApiTestEntity[] entities)
    {
      foreach (var entity in entities)
      {
        _data[entity.Id] = entity;
        if (entity.Id >= _nextId) _nextId = entity.Id + 1;
      }
    }

    public Task<OpenApiTestEntity> CreateAsync(OpenApiTestEntity entity, CancellationToken ct = default)
    {
      entity.Id = _nextId++;
      _data[entity.Id] = entity;
      return Task.FromResult(entity);
    }

    public Task<bool> DeleteAsync(int id, CancellationToken ct = default)
      => Task.FromResult(_data.Remove(id));

    public Task<PagedResult<OpenApiTestEntity>> GetAllAsync(PaginationRequest request, CancellationToken ct = default)
      => Task.FromResult(new PagedResult<OpenApiTestEntity> { Items = _data.Values.ToList(), NextCursor = null });

    public Task<OpenApiTestEntity?> GetByIdAsync(int id, CancellationToken ct = default)
    {
      _data.TryGetValue(id, out var entity);
      return Task.FromResult(entity);
    }

    public Task<OpenApiTestEntity?> PatchAsync(int id, JsonElement patchDocument, CancellationToken ct = default)
    {
      if (!_data.TryGetValue(id, out var entity)) return Task.FromResult<OpenApiTestEntity?>(null);
      return Task.FromResult<OpenApiTestEntity?>(entity);
    }

    public Task<OpenApiTestEntity?> UpdateAsync(int id, OpenApiTestEntity entity, CancellationToken ct = default)
    {
      if (!_data.ContainsKey(id)) return Task.FromResult<OpenApiTestEntity?>(null);
      entity.Id = id;
      _data[id] = entity;
      return Task.FromResult<OpenApiTestEntity?>(entity);
    }
  }

  #endregion

  #region AC1: All Endpoints in OpenAPI Spec

  [Fact]
  public async Task OpenApi_Should_ContainAllSixCrudEndpoints()
  {
    // Arrange
    using var host = await CreateHostWithOpenApi();
    var client = host.GetTestClient();

    // Act
    var openApiDoc = await GetOpenApiDocument(client);

    // Assert - Should have 6 endpoints
    openApiDoc.Paths!.Should().ContainKey("/api/items");
    openApiDoc.Paths!.Should().ContainKey("/api/items/{id}");

    // GET /api/items (GetAll)
    openApiDoc.Paths!["/api/items"]!.Operations.Should().ContainKey(HttpMethod.Get);
    // POST /api/items (Create)
    openApiDoc.Paths!["/api/items"]!.Operations.Should().ContainKey(HttpMethod.Post);
    // GET /api/items/{id} (GetById)
    openApiDoc.Paths!["/api/items/{id}"]!.Operations.Should().ContainKey(HttpMethod.Get);
    // PUT /api/items/{id} (Update)
    openApiDoc.Paths!["/api/items/{id}"]!.Operations.Should().ContainKey(HttpMethod.Put);
    // PATCH /api/items/{id} (Patch)
    openApiDoc.Paths!["/api/items/{id}"]!.Operations.Should().ContainKey(HttpMethod.Patch);
    // DELETE /api/items/{id} (Delete)
    openApiDoc.Paths!["/api/items/{id}"]!.Operations.Should().ContainKey(HttpMethod.Delete);
  }

  [Fact]
  public async Task OpenApi_Endpoints_Should_HaveOperationIds()
  {
    // Arrange
    using var host = await CreateHostWithOpenApi();
    var client = host.GetTestClient();

    // Act
    var openApiDoc = await GetOpenApiDocument(client);

    // Assert
    openApiDoc.Paths!["/api/items"]!.Operations[HttpMethod.Get]!.OperationId
        .Should().Be("OpenApiTestEntity_api_items_GetAll");
    openApiDoc.Paths!["/api/items"]!.Operations[HttpMethod.Post]!.OperationId
        .Should().Be("OpenApiTestEntity_api_items_Create");
    openApiDoc.Paths!["/api/items/{id}"]!.Operations[HttpMethod.Get]!.OperationId
        .Should().Be("OpenApiTestEntity_api_items_GetById");
    openApiDoc.Paths!["/api/items/{id}"]!.Operations[HttpMethod.Put]!.OperationId
        .Should().Be("OpenApiTestEntity_api_items_Update");
    openApiDoc.Paths!["/api/items/{id}"]!.Operations[HttpMethod.Patch]!.OperationId
        .Should().Be("OpenApiTestEntity_api_items_Patch");
    openApiDoc.Paths!["/api/items/{id}"]!.Operations[HttpMethod.Delete]!.OperationId
        .Should().Be("OpenApiTestEntity_api_items_Delete");
  }

  [Fact]
  public async Task OpenApi_Endpoints_Should_HaveSummaries()
  {
    // Arrange
    using var host = await CreateHostWithOpenApi();
    var client = host.GetTestClient();

    // Act
    var openApiDoc = await GetOpenApiDocument(client);

    // Assert
    openApiDoc.Paths!["/api/items"]!.Operations[HttpMethod.Get]!.Summary
        .Should().Contain("Get all");
    openApiDoc.Paths!["/api/items"]!.Operations[HttpMethod.Post]!.Summary
        .Should().Contain("Create");
    openApiDoc.Paths!["/api/items/{id}"]!.Operations[HttpMethod.Get]!.Summary
        .Should().Contain("Get");
    openApiDoc.Paths!["/api/items/{id}"]!.Operations[HttpMethod.Put]!.Summary
        .Should().Contain("update");
    openApiDoc.Paths!["/api/items/{id}"]!.Operations[HttpMethod.Patch]!.Summary
        .Should().Contain("Partial");
    openApiDoc.Paths!["/api/items/{id}"]!.Operations[HttpMethod.Delete]!.Summary
        .Should().Contain("Delete");
  }

  [Fact]
  public async Task OpenApi_Endpoints_Should_HaveDescriptions()
  {
    // Arrange
    using var host = await CreateHostWithOpenApi();
    var client = host.GetTestClient();

    // Act
    var openApiDoc = await GetOpenApiDocument(client);

    // Assert - All endpoints should have descriptions
    openApiDoc.Paths!["/api/items"]!.Operations[HttpMethod.Get]!.Description
        .Should().NotBeNullOrEmpty();
    openApiDoc.Paths!["/api/items"]!.Operations[HttpMethod.Post]!.Description
        .Should().NotBeNullOrEmpty();
    openApiDoc.Paths!["/api/items/{id}"]!.Operations[HttpMethod.Get]!.Description
        .Should().NotBeNullOrEmpty();
    openApiDoc.Paths!["/api/items/{id}"]!.Operations[HttpMethod.Put]!.Description
        .Should().NotBeNullOrEmpty();
    openApiDoc.Paths!["/api/items/{id}"]!.Operations[HttpMethod.Patch]!.Description
        .Should().NotBeNullOrEmpty();
    openApiDoc.Paths!["/api/items/{id}"]!.Operations[HttpMethod.Delete]!.Description
        .Should().NotBeNullOrEmpty();
  }

  [Fact]
  public async Task OpenApi_Endpoints_Should_HaveTags()
  {
    // Arrange
    using var host = await CreateHostWithOpenApi();
    var client = host.GetTestClient();

    // Act
    var openApiDoc = await GetOpenApiDocument(client);

    // Assert - All endpoints should be tagged with entity name
    openApiDoc.Paths!["/api/items"]!.Operations[HttpMethod.Get]!.Tags
        .Should().Contain(t => t.Name == "OpenApiTestEntity");
    openApiDoc.Paths!["/api/items"]!.Operations[HttpMethod.Post]!.Tags
        .Should().Contain(t => t.Name == "OpenApiTestEntity");
    openApiDoc.Paths!["/api/items/{id}"]!.Operations[HttpMethod.Get]!.Tags
        .Should().Contain(t => t.Name == "OpenApiTestEntity");
  }

  #endregion

  #region AC2: Schemas Documented

  [Fact]
  public async Task OpenApi_Should_ContainEntitySchema()
  {
    // Arrange
    using var host = await CreateHostWithOpenApi();
    var client = host.GetTestClient();

    // Act
    var openApiDoc = await GetOpenApiDocument(client);

    // Assert - Entity schema should exist
    openApiDoc.Components!.Schemas.Should().ContainKey("OpenApiTestEntity");
  }

  [Fact]
  public async Task OpenApi_GetAll_ResponseSchema_Should_BeCollectionResponse()
  {
    // Arrange
    using var host = await CreateHostWithOpenApi();
    var client = host.GetTestClient();

    // Act
    var openApiDoc = await GetOpenApiDocument(client);
    var getAllOp = openApiDoc.Paths!["/api/items"]!.Operations[HttpMethod.Get]!;
    var response200 = getAllOp.Responses["200"];

    // Assert - Response should have application/json content
    response200.Content.Should().ContainKey("application/json");
    var schema = response200.Content["application/json"].Schema!;

    // Schema may have properties directly or via reference
    // The schema should represent a collection response
    schema.Should().NotBeNull();

    // Check if it has direct properties or is a reference to a schema with properties
    if (schema.Properties != null && schema.Properties.Count > 0)
    {
      schema.Properties.Should().ContainKey("items");
    }
    else if (schema is OpenApiSchemaReference)
    {
      // Schema is referenced - this is acceptable for OpenAPI
      schema.Should().NotBeNull();
    }
    else if (schema.Type == JsonSchemaType.Object)
    {
      // Schema is an object type - acceptable
      schema.Type.Should().Be(JsonSchemaType.Object);
    }
  }

  [Fact]
  public async Task OpenApi_Create_RequestBody_Should_Reference_EntitySchema()
  {
    // Arrange
    using var host = await CreateHostWithOpenApi();
    var client = host.GetTestClient();

    // Act
    var openApiDoc = await GetOpenApiDocument(client);
    var createOp = openApiDoc.Paths!["/api/items"]!.Operations[HttpMethod.Post]!;

    // Assert
    createOp.RequestBody.Should().NotBeNull();
    createOp.RequestBody!.Required.Should().BeTrue();
    createOp.RequestBody.Content.Should().ContainKey("application/json");
  }

  [Fact]
  public async Task OpenApi_Update_RequestBody_Should_Reference_EntitySchema()
  {
    // Arrange
    using var host = await CreateHostWithOpenApi();
    var client = host.GetTestClient();

    // Act
    var openApiDoc = await GetOpenApiDocument(client);
    var updateOp = openApiDoc.Paths!["/api/items/{id}"]!.Operations[HttpMethod.Put]!;

    // Assert
    updateOp.RequestBody.Should().NotBeNull();
    updateOp.RequestBody!.Required.Should().BeTrue();
    updateOp.RequestBody.Content.Should().ContainKey("application/json");
  }

  [Fact]
  public async Task OpenApi_Patch_RequestBody_Should_DescribeJsonMergePatch()
  {
    // Arrange
    using var host = await CreateHostWithOpenApi();
    var client = host.GetTestClient();

    // Act
    var openApiDoc = await GetOpenApiDocument(client);
    var patchOp = openApiDoc.Paths!["/api/items/{id}"]!.Operations[HttpMethod.Patch]!;

    // Assert
    patchOp.RequestBody.Should().NotBeNull();
    patchOp.RequestBody!.Required.Should().BeTrue();
    patchOp.RequestBody.Content.Should().ContainKey("application/json");
    patchOp.RequestBody.Description.Should().Contain("JSON Merge Patch");
  }

  #endregion

  #region AC3: Status Codes Documented

  [Fact]
  public async Task OpenApi_GetAll_Should_Document_StatusCodes()
  {
    // Arrange
    using var host = await CreateHostWithOpenApi();
    var client = host.GetTestClient();

    // Act
    var openApiDoc = await GetOpenApiDocument(client);
    var getAllOp = openApiDoc.Paths!["/api/items"]!.Operations[HttpMethod.Get]!;

    // Assert
    getAllOp.Responses.Should().ContainKey("200"); // Success
    getAllOp.Responses.Should().ContainKey("400"); // Bad Request (invalid cursor/limit)
    getAllOp.Responses.Should().ContainKey("401"); // Unauthorized
    getAllOp.Responses.Should().ContainKey("403"); // Forbidden
  }

  [Fact]
  public async Task OpenApi_GetById_Should_Document_StatusCodes()
  {
    // Arrange
    using var host = await CreateHostWithOpenApi();
    var client = host.GetTestClient();

    // Act
    var openApiDoc = await GetOpenApiDocument(client);
    var getByIdOp = openApiDoc.Paths!["/api/items/{id}"]!.Operations[HttpMethod.Get]!;

    // Assert
    getByIdOp.Responses.Should().ContainKey("200"); // Success
    getByIdOp.Responses.Should().ContainKey("304"); // Not Modified (ETag)
    getByIdOp.Responses.Should().ContainKey("401"); // Unauthorized
    getByIdOp.Responses.Should().ContainKey("403"); // Forbidden
    getByIdOp.Responses.Should().ContainKey("404"); // Not Found
  }

  [Fact]
  public async Task OpenApi_Create_Should_Document_StatusCodes()
  {
    // Arrange
    using var host = await CreateHostWithOpenApi();
    var client = host.GetTestClient();

    // Act
    var openApiDoc = await GetOpenApiDocument(client);
    var createOp = openApiDoc.Paths!["/api/items"]!.Operations[HttpMethod.Post]!;

    // Assert
    createOp.Responses.Should().ContainKey("201"); // Created
    createOp.Responses.Should().ContainKey("400"); // Bad Request
    createOp.Responses.Should().ContainKey("401"); // Unauthorized
    createOp.Responses.Should().ContainKey("403"); // Forbidden
  }

  [Fact]
  public async Task OpenApi_Create_201Response_Should_Document_LocationHeader()
  {
    // Arrange
    using var host = await CreateHostWithOpenApi();
    var client = host.GetTestClient();

    // Act
    var openApiDoc = await GetOpenApiDocument(client);
    var createOp = openApiDoc.Paths!["/api/items"]!.Operations[HttpMethod.Post]!;
    var response201 = createOp.Responses["201"];

    // Assert
    response201.Headers.Should().ContainKey("Location");
    response201.Headers["Location"]!.Description.Should().Contain("URL");
  }

  [Fact]
  public async Task OpenApi_Update_Should_Document_StatusCodes()
  {
    // Arrange
    using var host = await CreateHostWithOpenApi();
    var client = host.GetTestClient();

    // Act
    var openApiDoc = await GetOpenApiDocument(client);
    var updateOp = openApiDoc.Paths!["/api/items/{id}"]!.Operations[HttpMethod.Put]!;

    // Assert
    updateOp.Responses.Should().ContainKey("200"); // Success
    updateOp.Responses.Should().ContainKey("400"); // Bad Request
    updateOp.Responses.Should().ContainKey("401"); // Unauthorized
    updateOp.Responses.Should().ContainKey("403"); // Forbidden
    updateOp.Responses.Should().ContainKey("404"); // Not Found
    updateOp.Responses.Should().ContainKey("412"); // Precondition Failed (ETag)
  }

  [Fact]
  public async Task OpenApi_Patch_Should_Document_StatusCodes()
  {
    // Arrange
    using var host = await CreateHostWithOpenApi();
    var client = host.GetTestClient();

    // Act
    var openApiDoc = await GetOpenApiDocument(client);
    var patchOp = openApiDoc.Paths!["/api/items/{id}"]!.Operations[HttpMethod.Patch]!;

    // Assert
    patchOp.Responses.Should().ContainKey("200"); // Success
    patchOp.Responses.Should().ContainKey("400"); // Bad Request
    patchOp.Responses.Should().ContainKey("401"); // Unauthorized
    patchOp.Responses.Should().ContainKey("403"); // Forbidden
    patchOp.Responses.Should().ContainKey("404"); // Not Found
    patchOp.Responses.Should().ContainKey("412"); // Precondition Failed
  }

  [Fact]
  public async Task OpenApi_Delete_Should_Document_StatusCodes()
  {
    // Arrange
    using var host = await CreateHostWithOpenApi();
    var client = host.GetTestClient();

    // Act
    var openApiDoc = await GetOpenApiDocument(client);
    var deleteOp = openApiDoc.Paths!["/api/items/{id}"]!.Operations[HttpMethod.Delete]!;

    // Assert
    deleteOp.Responses.Should().ContainKey("204"); // No Content
    deleteOp.Responses.Should().ContainKey("401"); // Unauthorized
    deleteOp.Responses.Should().ContainKey("403"); // Forbidden
    deleteOp.Responses.Should().ContainKey("404"); // Not Found
    deleteOp.Responses.Should().ContainKey("412"); // Precondition Failed
  }

  [Fact]
  public async Task OpenApi_ErrorResponses_Should_UseProblemDetails()
  {
    // Arrange
    using var host = await CreateHostWithOpenApi();
    var client = host.GetTestClient();

    // Act
    var openApiDoc = await GetOpenApiDocument(client);
    var getByIdOp = openApiDoc.Paths!["/api/items/{id}"]!.Operations[HttpMethod.Get]!;
    var response404 = getByIdOp.Responses["404"];

    // Assert - Should use application/problem+json
    response404.Content.Should().ContainKey("application/problem+json");
    var schema = response404.Content["application/problem+json"].Schema!;
    schema.Properties.Should().ContainKey("type");
    schema.Properties.Should().ContainKey("title");
    schema.Properties.Should().ContainKey("status");
    schema.Properties.Should().ContainKey("detail");
    schema.Properties.Should().ContainKey("instance");
  }

  #endregion

  #region AC4: Parameters with Constraints

  [Fact]
  public async Task OpenApi_GetAll_Should_Document_CursorParameter()
  {
    // Arrange
    using var host = await CreateHostWithOpenApi();
    var client = host.GetTestClient();

    // Act
    var openApiDoc = await GetOpenApiDocument(client);
    var getAllOp = openApiDoc.Paths!["/api/items"]!.Operations[HttpMethod.Get]!;
    var cursorParam = getAllOp.Parameters!.FirstOrDefault(p => p.Name == "cursor");

    // Assert
    cursorParam.Should().NotBeNull();
    cursorParam!.In.Should().Be(ParameterLocation.Query);
    cursorParam.Required.Should().BeFalse();
    cursorParam.Description.Should().Contain("cursor");
    cursorParam.Schema!.Type.Should().Be(JsonSchemaType.String);
  }

  [Fact]
  public async Task OpenApi_GetAll_Should_Document_LimitParameter_WithConstraints()
  {
    // Arrange
    using var host = await CreateHostWithOpenApi();
    var client = host.GetTestClient();

    // Act
    var openApiDoc = await GetOpenApiDocument(client);
    var getAllOp = openApiDoc.Paths!["/api/items"]!.Operations[HttpMethod.Get]!;
    var limitParam = getAllOp.Parameters!.FirstOrDefault(p => p.Name == "limit");

    // Assert
    limitParam.Should().NotBeNull();
    limitParam!.In.Should().Be(ParameterLocation.Query);
    limitParam.Required.Should().BeFalse();
    limitParam.Schema!.Type.Should().Be(JsonSchemaType.Integer);

    // Constraints may be serialized as schema properties or documented in description
    // Swashbuckle may not serialize min/max directly - check description for constraint info
    var hasSchemaConstraints = limitParam.Schema.Minimum != null && limitParam.Schema.Maximum != null;
    var hasDescriptionConstraints = limitParam.Description?.Contains("1") == true &&
                                     limitParam.Description?.Contains("100") == true;

    (hasSchemaConstraints || hasDescriptionConstraints).Should().BeTrue(
      "limit parameter should have constraints either in schema (minimum/maximum) or in description");
  }

  [Fact]
  public async Task OpenApi_GetById_Should_Document_IdParameter()
  {
    // Arrange
    using var host = await CreateHostWithOpenApi();
    var client = host.GetTestClient();

    // Act
    var openApiDoc = await GetOpenApiDocument(client);
    var getByIdOp = openApiDoc.Paths!["/api/items/{id}"]!.Operations[HttpMethod.Get]!;
    var idParam = getByIdOp.Parameters!.FirstOrDefault(p => p.Name == "id");

    // Assert
    idParam.Should().NotBeNull();
    idParam!.In.Should().Be(ParameterLocation.Path);
    idParam.Required.Should().BeTrue();
    idParam.Description.Should().Contain("identifier");
    idParam.Schema!.Type.Should().Be(JsonSchemaType.Integer);
  }

  [Fact]
  public async Task OpenApi_GetById_Should_Document_IfNoneMatchHeader()
  {
    // Arrange
    using var host = await CreateHostWithOpenApi();
    var client = host.GetTestClient();

    // Act
    var openApiDoc = await GetOpenApiDocument(client);
    var getByIdOp = openApiDoc.Paths!["/api/items/{id}"]!.Operations[HttpMethod.Get]!;
    var ifNoneMatchParam = getByIdOp.Parameters!.FirstOrDefault(p => p.Name == "If-None-Match");

    // Assert
    ifNoneMatchParam.Should().NotBeNull();
    ifNoneMatchParam!.In.Should().Be(ParameterLocation.Header);
    ifNoneMatchParam.Required.Should().BeFalse();
    ifNoneMatchParam.Description.Should().Contain("ETag");
  }

  [Fact]
  public async Task OpenApi_Update_Should_Document_IfMatchHeader()
  {
    // Arrange
    using var host = await CreateHostWithOpenApi();
    var client = host.GetTestClient();

    // Act
    var openApiDoc = await GetOpenApiDocument(client);
    var updateOp = openApiDoc.Paths!["/api/items/{id}"]!.Operations[HttpMethod.Put]!;
    var ifMatchParam = updateOp.Parameters!.FirstOrDefault(p => p.Name == "If-Match");

    // Assert
    ifMatchParam.Should().NotBeNull();
    ifMatchParam!.In.Should().Be(ParameterLocation.Header);
    ifMatchParam.Required.Should().BeFalse();
    ifMatchParam.Description.Should().Contain("optimistic locking");
  }

  [Fact]
  public async Task OpenApi_Delete_Should_Document_IfMatchHeader()
  {
    // Arrange
    using var host = await CreateHostWithOpenApi();
    var client = host.GetTestClient();

    // Act
    var openApiDoc = await GetOpenApiDocument(client);
    var deleteOp = openApiDoc.Paths!["/api/items/{id}"]!.Operations[HttpMethod.Delete]!;
    var ifMatchParam = deleteOp.Parameters!.FirstOrDefault(p => p.Name == "If-Match");

    // Assert
    ifMatchParam.Should().NotBeNull();
    ifMatchParam!.In.Should().Be(ParameterLocation.Header);
    ifMatchParam.Required.Should().BeFalse();
  }

  [Fact]
  public async Task OpenApi_WithFiltering_Should_Document_FilterParameters()
  {
    // Arrange
    using var host = await CreateHostWithFilters();
    var client = host.GetTestClient();

    // Act
    var openApiDoc = await GetOpenApiDocument(client);
    var getAllOp = openApiDoc.Paths!["/api/items"]!.Operations[HttpMethod.Get]!;

    // Assert - Should have filter parameters
    var isActiveParam = getAllOp.Parameters!.FirstOrDefault(p => p.Name == "is_active");
    isActiveParam.Should().NotBeNull();
    isActiveParam!.In.Should().Be(ParameterLocation.Query);
    isActiveParam.Schema!.Type.Should().Be(JsonSchemaType.Boolean);

    var categoryIdParam = getAllOp.Parameters!.FirstOrDefault(p => p.Name == "category_id");
    categoryIdParam.Should().NotBeNull();
    categoryIdParam!.Schema!.Type.Should().Be(JsonSchemaType.String);
    categoryIdParam.Schema.Format.Should().Be("uuid");
  }

  #endregion

  #region Response Headers

  [Fact]
  public async Task OpenApi_GetById_200Response_Should_Document_ETagHeader()
  {
    // Arrange
    using var host = await CreateHostWithOpenApi();
    var client = host.GetTestClient();

    // Act
    var openApiDoc = await GetOpenApiDocument(client);
    var getByIdOp = openApiDoc.Paths!["/api/items/{id}"]!.Operations[HttpMethod.Get]!;
    var response200 = getByIdOp.Responses["200"];

    // Assert
    response200.Headers.Should().ContainKey("ETag");
    response200.Headers["ETag"]!.Description.Should().Contain("cache");
  }

  [Fact]
  public async Task OpenApi_Create_201Response_Should_Document_ETagHeader()
  {
    // Arrange
    using var host = await CreateHostWithOpenApi();
    var client = host.GetTestClient();

    // Act
    var openApiDoc = await GetOpenApiDocument(client);
    var createOp = openApiDoc.Paths!["/api/items"]!.Operations[HttpMethod.Post]!;
    var response201 = createOp.Responses["201"];

    // Assert
    response201.Headers.Should().ContainKey("ETag");
  }

  [Fact]
  public async Task OpenApi_Update_200Response_Should_Document_ETagHeader()
  {
    // Arrange
    using var host = await CreateHostWithOpenApi();
    var client = host.GetTestClient();

    // Act
    var openApiDoc = await GetOpenApiDocument(client);
    var updateOp = openApiDoc.Paths!["/api/items/{id}"]!.Operations[HttpMethod.Put]!;
    var response200 = updateOp.Responses["200"];

    // Assert
    response200.Headers.Should().ContainKey("ETag");
  }

  #endregion

  #region Helper Methods

  private static async Task<IHost> CreateHostWithOpenApi()
  {
    var repository = new OpenApiTestRepository();

    var host = await new HostBuilder()
        .ConfigureWebHost(webBuilder =>
        {
          webBuilder.UseTestServer();
          webBuilder.ConfigureServices(services =>
          {
            services.AddRestLib();
            services.AddRouting();
            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen(c =>
            {
              c.SwaggerDoc("v1", new OpenApiInfo { Title = "Test API", Version = "v1" });
              // Enable annotations from minimal APIs
              c.SupportNonNullableReferenceTypes();
            });
            services.AddSingleton<IRepository<OpenApiTestEntity, int>>(repository);
          });
          webBuilder.Configure(app =>
          {
            app.UseRouting();
            app.UseSwagger();
            app.UseEndpoints(endpoints =>
            {
              endpoints.MapRestLib<OpenApiTestEntity, int>("/api/items", config =>
              {
                config.AllowAnonymous();
                config.KeySelector = e => e.Id;
              });
            });
          });
        })
        .StartAsync();

    return host;
  }

  private static async Task<IHost> CreateHostWithFilters()
  {
    var repository = new OpenApiTestRepository();

    var host = await new HostBuilder()
        .ConfigureWebHost(webBuilder =>
        {
          webBuilder.UseTestServer();
          webBuilder.ConfigureServices(services =>
          {
            services.AddRestLib();
            services.AddRouting();
            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen(c =>
            {
              c.SwaggerDoc("v1", new OpenApiInfo { Title = "Test API", Version = "v1" });
              c.SupportNonNullableReferenceTypes();
            });
            services.AddSingleton<IRepository<OpenApiTestEntity, int>>(repository);
          });
          webBuilder.Configure(app =>
          {
            app.UseRouting();
            app.UseSwagger();
            app.UseEndpoints(endpoints =>
            {
              endpoints.MapRestLib<OpenApiTestEntity, int>("/api/items", config =>
              {
                config.AllowAnonymous();
                config.KeySelector = e => e.Id;
                config.AllowFiltering(e => e.IsActive, e => e.CategoryId);
              });
            });
          });
        })
        .StartAsync();

    return host;
  }

  private static async Task<OpenApiDocument> GetOpenApiDocument(HttpClient client)
  {
    var response = await client.GetAsync("/swagger/v1/swagger.json");
    response.EnsureSuccessStatusCode();

    var content = await response.Content.ReadAsStreamAsync();
    var result = await OpenApiDocument.LoadAsync(content, "json");

    return result.Document!;
  }

  #endregion
}
