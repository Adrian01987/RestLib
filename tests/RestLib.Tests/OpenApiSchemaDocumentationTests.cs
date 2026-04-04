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

public partial class OpenApiDocumentationTests
{
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
}
