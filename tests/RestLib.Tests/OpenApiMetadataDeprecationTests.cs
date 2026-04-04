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
  #region AC3: Deprecation Marking

  [Fact]
  public async Task OpenApi_WithDeprecatedTrue_Should_MarkAllEndpointsAsDeprecated()
  {
    // Arrange
    using var host = await CreateHostWithOpenApi(config =>
    {
      config.AllowAnonymous();
      config.KeySelector = e => e.Id;
      config.OpenApi.Deprecated = true;
    });
    var client = host.GetTestClient();

    // Act
    var openApiDoc = await GetOpenApiDocument(client);

    // Assert - All operations should be marked as deprecated
    openApiDoc.Paths!["/api/items"]!.Operations[HttpMethod.Get]!.Deprecated.Should().BeTrue();
    openApiDoc.Paths!["/api/items"]!.Operations[HttpMethod.Post]!.Deprecated.Should().BeTrue();
    openApiDoc.Paths!["/api/items/{id}"]!.Operations[HttpMethod.Get]!.Deprecated.Should().BeTrue();
    openApiDoc.Paths!["/api/items/{id}"]!.Operations[HttpMethod.Put]!.Deprecated.Should().BeTrue();
    openApiDoc.Paths!["/api/items/{id}"]!.Operations[HttpMethod.Patch]!.Deprecated.Should().BeTrue();
    openApiDoc.Paths!["/api/items/{id}"]!.Operations[HttpMethod.Delete]!.Deprecated.Should().BeTrue();
  }

  [Fact]
  public async Task OpenApi_WithoutDeprecated_Should_NotMarkEndpointsAsDeprecated()
  {
    // Arrange
    using var host = await CreateHostWithOpenApi(config =>
    {
      config.AllowAnonymous();
      config.KeySelector = e => e.Id;
      // Deprecated defaults to false
    });
    var client = host.GetTestClient();

    // Act
    var openApiDoc = await GetOpenApiDocument(client);

    // Assert - No operations should be marked as deprecated
    openApiDoc.Paths!["/api/items"]!.Operations[HttpMethod.Get]!.Deprecated.Should().BeFalse();
    openApiDoc.Paths!["/api/items"]!.Operations[HttpMethod.Post]!.Deprecated.Should().BeFalse();
    openApiDoc.Paths!["/api/items/{id}"]!.Operations[HttpMethod.Get]!.Deprecated.Should().BeFalse();
    openApiDoc.Paths!["/api/items/{id}"]!.Operations[HttpMethod.Put]!.Deprecated.Should().BeFalse();
    openApiDoc.Paths!["/api/items/{id}"]!.Operations[HttpMethod.Patch]!.Deprecated.Should().BeFalse();
    openApiDoc.Paths!["/api/items/{id}"]!.Operations[HttpMethod.Delete]!.Deprecated.Should().BeFalse();
  }

  [Fact]
  public async Task OpenApi_WithDeprecationMessage_Should_IncludeMessageInDescription()
  {
    // Arrange
    const string deprecationMessage = "Use /api/v2/products instead. This endpoint will be removed on 2026-06-01.";
    using var host = await CreateHostWithOpenApi(config =>
    {
      config.AllowAnonymous();
      config.KeySelector = e => e.Id;
      config.OpenApi.Deprecated = true;
      config.OpenApi.DeprecationMessage = deprecationMessage;
    });
    var client = host.GetTestClient();

    // Act
    var openApiDoc = await GetOpenApiDocument(client);

    // Assert - Descriptions should contain the deprecation message
    openApiDoc.Paths!["/api/items"]!.Operations[HttpMethod.Get]!.Description
        .Should().Contain("DEPRECATED");
    openApiDoc.Paths!["/api/items"]!.Operations[HttpMethod.Get]!.Description
        .Should().Contain(deprecationMessage);
  }

  [Fact]
  public async Task OpenApi_WithDeprecatedWithoutMessage_Should_IncludeDefaultDeprecationText()
  {
    // Arrange
    using var host = await CreateHostWithOpenApi(config =>
    {
      config.AllowAnonymous();
      config.KeySelector = e => e.Id;
      config.OpenApi.Deprecated = true;
      // No DeprecationMessage set
    });
    var client = host.GetTestClient();

    // Act
    var openApiDoc = await GetOpenApiDocument(client);

    // Assert - Should have a default deprecation notice
    openApiDoc.Paths!["/api/items"]!.Operations[HttpMethod.Get]!.Description
        .Should().Contain("DEPRECATED");
    openApiDoc.Paths!["/api/items"]!.Operations[HttpMethod.Get]!.Description
        .Should().Contain("deprecated");
  }

  [Fact]
  public async Task OpenApi_WithDeprecationMessage_WithoutDeprecated_Should_NotShowMessage()
  {
    // Arrange
    using var host = await CreateHostWithOpenApi(config =>
    {
      config.AllowAnonymous();
      config.KeySelector = e => e.Id;
      config.OpenApi.Deprecated = false; // Not deprecated
      config.OpenApi.DeprecationMessage = "This should not appear";
    });
    var client = host.GetTestClient();

    // Act
    var openApiDoc = await GetOpenApiDocument(client);

    // Assert - Descriptions should NOT contain deprecation message
    openApiDoc.Paths!["/api/items"]!.Operations[HttpMethod.Get]!.Description
        .Should().NotContain("DEPRECATED");
    openApiDoc.Paths!["/api/items"]!.Operations[HttpMethod.Get]!.Description
        .Should().NotContain("This should not appear");
  }

  #endregion
}
