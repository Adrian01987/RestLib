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
using Swashbuckle.AspNetCore.SwaggerGen;
using Xunit;

namespace RestLib.Tests;

public partial class OpenApiMetadataConfigurationTests
{
  #region AC2.5: Custom Descriptions

  [Fact]
  public async Task OpenApi_WithCustomDescription_GetAll_Should_UseConfiguredDescription()
  {
    // Arrange
    const string customDescription = "Returns a paginated list of all products in the system, ordered by creation date.";
    using var host = await CreateHostWithOpenApi(config =>
    {
      config.AllowAnonymous();
      config.KeySelector = e => e.Id;
      config.OpenApi.Descriptions.GetAll = customDescription;
    });
    var client = host.GetTestClient();

    // Act
    var openApiDoc = await GetOpenApiDocument(client);

    // Assert
    openApiDoc.Paths!["/api/items"]!.Operations[HttpMethod.Get]!.Description
        .Should().Be(customDescription);
  }

  [Fact]
  public async Task OpenApi_WithCustomDescription_Create_Should_UseConfiguredDescription()
  {
    // Arrange
    const string customDescription = "Creates a new product with the provided details. Returns the created product with generated ID.";
    using var host = await CreateHostWithOpenApi(config =>
    {
      config.AllowAnonymous();
      config.KeySelector = e => e.Id;
      config.OpenApi.Descriptions.Create = customDescription;
    });
    var client = host.GetTestClient();

    // Act
    var openApiDoc = await GetOpenApiDocument(client);

    // Assert
    openApiDoc.Paths!["/api/items"]!.Operations[HttpMethod.Post]!.Description
        .Should().Be(customDescription);
  }

  [Fact]
  public async Task OpenApi_WithAllCustomDescriptions_Should_ApplyAllDescriptions()
  {
    // Arrange
    using var host = await CreateHostWithOpenApi(config =>
    {
      config.AllowAnonymous();
      config.KeySelector = e => e.Id;
      config.OpenApi.Descriptions.GetAll = "Custom GetAll description";
      config.OpenApi.Descriptions.GetById = "Custom GetById description";
      config.OpenApi.Descriptions.Create = "Custom Create description";
      config.OpenApi.Descriptions.Update = "Custom Update description";
      config.OpenApi.Descriptions.Patch = "Custom Patch description";
      config.OpenApi.Descriptions.Delete = "Custom Delete description";
    });
    var client = host.GetTestClient();

    // Act
    var openApiDoc = await GetOpenApiDocument(client);

    // Assert
    openApiDoc.Paths!["/api/items"]!.Operations[HttpMethod.Get]!.Description
        .Should().Be("Custom GetAll description");
    openApiDoc.Paths!["/api/items"]!.Operations[HttpMethod.Post]!.Description
        .Should().Be("Custom Create description");
    openApiDoc.Paths!["/api/items/{id}"]!.Operations[HttpMethod.Get]!.Description
        .Should().Be("Custom GetById description");
    openApiDoc.Paths!["/api/items/{id}"]!.Operations[HttpMethod.Put]!.Description
        .Should().Be("Custom Update description");
    openApiDoc.Paths!["/api/items/{id}"]!.Operations[HttpMethod.Patch]!.Description
        .Should().Be("Custom Patch description");
    openApiDoc.Paths!["/api/items/{id}"]!.Operations[HttpMethod.Delete]!.Description
        .Should().Be("Custom Delete description");
  }

  #endregion
}
