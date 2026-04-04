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
/// AC3: Status Codes Documented — verifies that all endpoints document their status codes.
/// </summary>
public partial class OpenApiDocumentationTests
{
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
}
