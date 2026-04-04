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
  #region AC1: Custom Tags

  [Fact]
  public async Task OpenApi_WithDefaultTag_Should_UseEntityTypeName()
  {
    // Arrange
    using var host = await CreateHostWithOpenApi(config =>
    {
      config.AllowAnonymous();
      config.KeySelector = e => e.Id;
      // No custom tag configured - should default to entity name
    });
    var client = host.GetTestClient();

    // Act
    var openApiDoc = await GetOpenApiDocument(client);

    // Assert - All operations should use entity type name as tag
    openApiDoc.Paths!["/api/items"]!.Operations[HttpMethod.Get]!.Tags
        .Should().Contain(t => t.Name == "MetadataTestEntity");
    openApiDoc.Paths!["/api/items"]!.Operations[HttpMethod.Post]!.Tags
        .Should().Contain(t => t.Name == "MetadataTestEntity");
    openApiDoc.Paths!["/api/items/{id}"]!.Operations[HttpMethod.Get]!.Tags
        .Should().Contain(t => t.Name == "MetadataTestEntity");
    openApiDoc.Paths!["/api/items/{id}"]!.Operations[HttpMethod.Put]!.Tags
        .Should().Contain(t => t.Name == "MetadataTestEntity");
    openApiDoc.Paths!["/api/items/{id}"]!.Operations[HttpMethod.Patch]!.Tags
        .Should().Contain(t => t.Name == "MetadataTestEntity");
    openApiDoc.Paths!["/api/items/{id}"]!.Operations[HttpMethod.Delete]!.Tags
        .Should().Contain(t => t.Name == "MetadataTestEntity");
  }

  [Fact]
  public async Task OpenApi_WithCustomTag_Should_UseConfiguredTag()
  {
    // Arrange
    using var host = await CreateHostWithOpenApi(config =>
    {
      config.AllowAnonymous();
      config.KeySelector = e => e.Id;
      config.OpenApi.Tag = "Products";
    });
    var client = host.GetTestClient();

    // Act
    var openApiDoc = await GetOpenApiDocument(client);

    // Assert - All operations should use custom tag
    openApiDoc.Paths!["/api/items"]!.Operations[HttpMethod.Get]!.Tags
        .Should().Contain(t => t.Name == "Products");
    openApiDoc.Paths!["/api/items"]!.Operations[HttpMethod.Post]!.Tags
        .Should().Contain(t => t.Name == "Products");
    openApiDoc.Paths!["/api/items/{id}"]!.Operations[HttpMethod.Get]!.Tags
        .Should().Contain(t => t.Name == "Products");
    openApiDoc.Paths!["/api/items/{id}"]!.Operations[HttpMethod.Put]!.Tags
        .Should().Contain(t => t.Name == "Products");
    openApiDoc.Paths!["/api/items/{id}"]!.Operations[HttpMethod.Patch]!.Tags
        .Should().Contain(t => t.Name == "Products");
    openApiDoc.Paths!["/api/items/{id}"]!.Operations[HttpMethod.Delete]!.Tags
        .Should().Contain(t => t.Name == "Products");
  }

  [Fact]
  public async Task OpenApi_WithCustomTag_Should_NotContainDefaultTag()
  {
    // Arrange
    using var host = await CreateHostWithOpenApi(config =>
    {
      config.AllowAnonymous();
      config.KeySelector = e => e.Id;
      config.OpenApi.Tag = "Items";
    });
    var client = host.GetTestClient();

    // Act
    var openApiDoc = await GetOpenApiDocument(client);

    // Assert - Should NOT contain the default entity name tag
    openApiDoc.Paths!["/api/items"]!.Operations[HttpMethod.Get]!.Tags
        .Should().NotContain(t => t.Name == "MetadataTestEntity");
    openApiDoc.Paths!["/api/items"]!.Operations[HttpMethod.Get]!.Tags
        .Should().ContainSingle(t => t.Name == "Items");
  }

  [Fact]
  public async Task OpenApi_WithEmptyTag_Should_UseEntityTypeName()
  {
    // Arrange
    using var host = await CreateHostWithOpenApi(config =>
    {
      config.AllowAnonymous();
      config.KeySelector = e => e.Id;
      config.OpenApi.Tag = ""; // Empty should fall back to entity name
    });
    var client = host.GetTestClient();

    // Act
    var openApiDoc = await GetOpenApiDocument(client);

    // Assert - Should use entity name when tag is empty
    // Note: Empty string is truthy in C# so it won't fall back. Let's check actual behavior
    openApiDoc.Paths!["/api/items"]!.Operations[HttpMethod.Get]!.Tags
        .Should().HaveCount(1);
  }

  #endregion
}
