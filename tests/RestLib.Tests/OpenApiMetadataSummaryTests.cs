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
using RestLib.Configuration;
using RestLib.Pagination;
using Xunit;

namespace RestLib.Tests;

public partial class OpenApiMetadataConfigurationTests
{
    #region AC2: Custom Summaries

    [Fact]
    public async Task OpenApi_WithCustomSummary_GetAll_Should_UseConfiguredSummary()
    {
        // Arrange
        const string customSummary = "Retrieve all products from the catalog";
        using var host = await CreateHostWithOpenApi(config =>
        {
            config.AllowAnonymous();
            config.KeySelector = e => e.Id;
            config.OpenApi.Summaries.GetAll = customSummary;
        });
        var client = host.GetTestClient();

        // Act
        var openApiDoc = await GetOpenApiDocument(client);

        // Assert
        RequireOperation(openApiDoc, "/api/items", HttpMethod.Get).Summary
            .Should().Be(customSummary);
    }

    [Fact]
    public async Task OpenApi_WithCustomSummary_GetById_Should_UseConfiguredSummary()
    {
        // Arrange
        const string customSummary = "Get a specific product by its unique identifier";
        using var host = await CreateHostWithOpenApi(config =>
        {
            config.AllowAnonymous();
            config.KeySelector = e => e.Id;
            config.OpenApi.Summaries.GetById = customSummary;
        });
        var client = host.GetTestClient();

        // Act
        var openApiDoc = await GetOpenApiDocument(client);

        // Assert
        RequireOperation(openApiDoc, "/api/items/{id}", HttpMethod.Get).Summary
            .Should().Be(customSummary);
    }

    [Fact]
    public async Task OpenApi_WithCustomSummary_Create_Should_UseConfiguredSummary()
    {
        // Arrange
        const string customSummary = "Add a new product to the catalog";
        using var host = await CreateHostWithOpenApi(config =>
        {
            config.AllowAnonymous();
            config.KeySelector = e => e.Id;
            config.OpenApi.Summaries.Create = customSummary;
        });
        var client = host.GetTestClient();

        // Act
        var openApiDoc = await GetOpenApiDocument(client);

        // Assert
        RequireOperation(openApiDoc, "/api/items", HttpMethod.Post).Summary
            .Should().Be(customSummary);
    }

    [Fact]
    public async Task OpenApi_WithCustomSummary_Update_Should_UseConfiguredSummary()
    {
        // Arrange
        const string customSummary = "Replace an existing product";
        using var host = await CreateHostWithOpenApi(config =>
        {
            config.AllowAnonymous();
            config.KeySelector = e => e.Id;
            config.OpenApi.Summaries.Update = customSummary;
        });
        var client = host.GetTestClient();

        // Act
        var openApiDoc = await GetOpenApiDocument(client);

        // Assert
        RequireOperation(openApiDoc, "/api/items/{id}", HttpMethod.Put).Summary
            .Should().Be(customSummary);
    }

    [Fact]
    public async Task OpenApi_WithCustomSummary_Patch_Should_UseConfiguredSummary()
    {
        // Arrange
        const string customSummary = "Update specific fields of a product";
        using var host = await CreateHostWithOpenApi(config =>
        {
            config.AllowAnonymous();
            config.KeySelector = e => e.Id;
            config.OpenApi.Summaries.Patch = customSummary;
        });
        var client = host.GetTestClient();

        // Act
        var openApiDoc = await GetOpenApiDocument(client);

        // Assert
        RequireOperation(openApiDoc, "/api/items/{id}", HttpMethod.Patch).Summary
            .Should().Be(customSummary);
    }

    [Fact]
    public async Task OpenApi_WithCustomSummary_Delete_Should_UseConfiguredSummary()
    {
        // Arrange
        const string customSummary = "Remove a product from the catalog";
        using var host = await CreateHostWithOpenApi(config =>
        {
            config.AllowAnonymous();
            config.KeySelector = e => e.Id;
            config.OpenApi.Summaries.Delete = customSummary;
        });
        var client = host.GetTestClient();

        // Act
        var openApiDoc = await GetOpenApiDocument(client);

        // Assert
        RequireOperation(openApiDoc, "/api/items/{id}", HttpMethod.Delete).Summary
            .Should().Be(customSummary);
    }

    [Fact]
    public async Task OpenApi_WithAllCustomSummaries_Should_ApplyAllSummaries()
    {
        // Arrange
        using var host = await CreateHostWithOpenApi(config =>
        {
            config.AllowAnonymous();
            config.KeySelector = e => e.Id;
            config.OpenApi.Summaries.GetAll = "List products";
            config.OpenApi.Summaries.GetById = "Get product";
            config.OpenApi.Summaries.Create = "Add product";
            config.OpenApi.Summaries.Update = "Replace product";
            config.OpenApi.Summaries.Patch = "Update product fields";
            config.OpenApi.Summaries.Delete = "Remove product";
        });
        var client = host.GetTestClient();

        // Act
        var openApiDoc = await GetOpenApiDocument(client);

        // Assert
        RequireOperation(openApiDoc, "/api/items", HttpMethod.Get).Summary.Should().Be("List products");
        RequireOperation(openApiDoc, "/api/items", HttpMethod.Post).Summary.Should().Be("Add product");
        RequireOperation(openApiDoc, "/api/items/{id}", HttpMethod.Get).Summary.Should().Be("Get product");
        RequireOperation(openApiDoc, "/api/items/{id}", HttpMethod.Put).Summary.Should().Be("Replace product");
        RequireOperation(openApiDoc, "/api/items/{id}", HttpMethod.Patch).Summary.Should().Be("Update product fields");
        RequireOperation(openApiDoc, "/api/items/{id}", HttpMethod.Delete).Summary.Should().Be("Remove product");
    }

    [Fact]
    public async Task OpenApi_WithPartialCustomSummaries_Should_UseDefaultsForUnconfigured()
    {
        // Arrange
        using var host = await CreateHostWithOpenApi(config =>
        {
            config.AllowAnonymous();
            config.KeySelector = e => e.Id;
            config.OpenApi.Summaries.GetAll = "Custom list products"; // Only customize GetAll
        });
        var client = host.GetTestClient();

        // Act
        var openApiDoc = await GetOpenApiDocument(client);

        // Assert - GetAll should use custom summary
        RequireOperation(openApiDoc, "/api/items", HttpMethod.Get).Summary
            .Should().Be("Custom list products");

        // Other operations should use default summaries (containing entity name)
        RequireOperation(openApiDoc, "/api/items", HttpMethod.Post).Summary
            .Should().Contain("Create");
        RequireOperation(openApiDoc, "/api/items/{id}", HttpMethod.Get).Summary
            .Should().Contain("Get");
    }

    #endregion
}
