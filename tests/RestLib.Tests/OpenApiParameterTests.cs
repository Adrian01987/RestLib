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
/// AC4: Parameters with Constraints — verifies that endpoint parameters are documented with constraints.
/// </summary>
public partial class OpenApiDocumentationTests
{
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

        // Verify schema constraints reflect default RestLibOptions (MaxPageSize=100, DefaultPageSize=20)
        limitParam.Schema.Minimum.Should().Be("1");
        limitParam.Schema.Maximum.Should().Be("100");
        limitParam.Schema.Default.Should().NotBeNull();
        limitParam.Schema.Default!.GetValue<int>().Should().Be(20);

        // Description should reflect the same values
        limitParam.Description.Should().Contain("1-100");
        limitParam.Description.Should().Contain("Default: 20");
    }

    [Fact]
    public async Task OpenApi_GetAll_Should_Document_LimitParameter_WithCustomPaginationLimits()
    {
        // Arrange — configure custom pagination limits
        var repository = new OpenApiTestRepository();

        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
            {
                services.AddRestLib(options =>
            {
                options.DefaultPageSize = 50;
                options.MaxPageSize = 500;
            });
                services.AddRouting();
                services.AddOpenApi();
                services.AddSingleton<IRepository<OpenApiTestEntity, int>>(repository);
            });
                webBuilder.Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
            {
                endpoints.MapOpenApi();
                endpoints.MapRestLib<OpenApiTestEntity, int>("/api/items", config =>
              {
                  config.AllowAnonymous();
                  config.KeySelector = e => e.Id;
              });
            });
            });
            })
            .StartAsync();

        var client = host.GetTestClient();

        // Act
        var openApiDoc = await GetOpenApiDocument(client);
        var getAllOp = openApiDoc.Paths!["/api/items"]!.Operations[HttpMethod.Get]!;
        var limitParam = getAllOp.Parameters!.FirstOrDefault(p => p.Name == "limit");

        // Assert
        limitParam.Should().NotBeNull();
        limitParam!.Schema!.Minimum.Should().Be("1");
        limitParam.Schema.Maximum.Should().Be("500");
        limitParam.Schema.Default.Should().NotBeNull();
        limitParam.Schema.Default!.GetValue<int>().Should().Be(50);

        // Description should reflect custom values
        limitParam.Description.Should().Contain("1-500");
        limitParam.Description.Should().Contain("Default: 50");
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
}
