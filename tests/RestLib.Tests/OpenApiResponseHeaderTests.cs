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
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Response Headers — verifies that ETag headers are documented in responses.
/// </summary>
public partial class OpenApiDocumentationTests
{
    #region Response Headers

    [Fact]
    public async Task OpenApi_GetById_200Response_Should_Document_ETagHeader()
    {
        // Arrange
        using var host = await CreateHostWithOpenApi();
        var client = host.GetTestClient();

        // Act
        var openApiDoc = await GetOpenApiDocument(client);
        var getByIdOp = RequireOperation(openApiDoc, "/api/items/{id}", HttpMethod.Get);
        var response200 = RequireResponse(getByIdOp, "200");

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
        var createOp = RequireOperation(openApiDoc, "/api/items", HttpMethod.Post);
        var response201 = RequireResponse(createOp, "201");

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
        var updateOp = RequireOperation(openApiDoc, "/api/items/{id}", HttpMethod.Put);
        var response200 = RequireResponse(updateOp, "200");

        // Assert
        response200.Headers.Should().ContainKey("ETag");
    }

    #endregion
}
