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

public partial class OpenApiDocumentationTests
{
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
        RequireOperation(openApiDoc, "/api/items", HttpMethod.Get).OperationId
            .Should().Be("OpenApiTestEntity_api_items_GetAll");
        RequireOperation(openApiDoc, "/api/items", HttpMethod.Post).OperationId
            .Should().Be("OpenApiTestEntity_api_items_Create");
        RequireOperation(openApiDoc, "/api/items/{id}", HttpMethod.Get).OperationId
            .Should().Be("OpenApiTestEntity_api_items_GetById");
        RequireOperation(openApiDoc, "/api/items/{id}", HttpMethod.Put).OperationId
            .Should().Be("OpenApiTestEntity_api_items_Update");
        RequireOperation(openApiDoc, "/api/items/{id}", HttpMethod.Patch).OperationId
            .Should().Be("OpenApiTestEntity_api_items_Patch");
        RequireOperation(openApiDoc, "/api/items/{id}", HttpMethod.Delete).OperationId
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
        RequireOperation(openApiDoc, "/api/items", HttpMethod.Get).Summary
            .Should().Contain("Get all");
        RequireOperation(openApiDoc, "/api/items", HttpMethod.Post).Summary
            .Should().Contain("Create");
        RequireOperation(openApiDoc, "/api/items/{id}", HttpMethod.Get).Summary
            .Should().Contain("Get");
        RequireOperation(openApiDoc, "/api/items/{id}", HttpMethod.Put).Summary
            .Should().Contain("update");
        RequireOperation(openApiDoc, "/api/items/{id}", HttpMethod.Patch).Summary
            .Should().Contain("Partial");
        RequireOperation(openApiDoc, "/api/items/{id}", HttpMethod.Delete).Summary
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
        RequireOperation(openApiDoc, "/api/items", HttpMethod.Get).Description
            .Should().NotBeNullOrEmpty();
        RequireOperation(openApiDoc, "/api/items", HttpMethod.Post).Description
            .Should().NotBeNullOrEmpty();
        RequireOperation(openApiDoc, "/api/items/{id}", HttpMethod.Get).Description
            .Should().NotBeNullOrEmpty();
        RequireOperation(openApiDoc, "/api/items/{id}", HttpMethod.Put).Description
            .Should().NotBeNullOrEmpty();
        RequireOperation(openApiDoc, "/api/items/{id}", HttpMethod.Patch).Description
            .Should().NotBeNullOrEmpty();
        RequireOperation(openApiDoc, "/api/items/{id}", HttpMethod.Delete).Description
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
        RequireOperation(openApiDoc, "/api/items", HttpMethod.Get).Tags
            .Should().Contain(t => t.Name == "OpenApiTestEntity");
        RequireOperation(openApiDoc, "/api/items", HttpMethod.Post).Tags
            .Should().Contain(t => t.Name == "OpenApiTestEntity");
        RequireOperation(openApiDoc, "/api/items/{id}", HttpMethod.Get).Tags
            .Should().Contain(t => t.Name == "OpenApiTestEntity");
    }

    #endregion
}
