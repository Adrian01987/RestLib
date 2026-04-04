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
  #region Combined Configuration Tests

  [Fact]
  public async Task OpenApi_WithCombinedConfiguration_Should_ApplyAllSettings()
  {
    // Arrange
    using var host = await CreateHostWithOpenApi(config =>
    {
      config.AllowAnonymous();
      config.KeySelector = e => e.Id;
      config.OpenApi.Tag = "Legacy Products";
      config.OpenApi.Deprecated = true;
      config.OpenApi.DeprecationMessage = "Migrate to v2 API";
      config.OpenApi.Summaries.GetAll = "List legacy products";
      config.OpenApi.Descriptions.GetAll = "Returns all products from the legacy system.";
    });
    var client = host.GetTestClient();

    // Act
    var openApiDoc = await GetOpenApiDocument(client);

    // Assert - All configurations should be applied
    var getAllOp = openApiDoc.Paths!["/api/items"]!.Operations[HttpMethod.Get]!;

    getAllOp.Tags.Should().Contain(t => t.Name == "Legacy Products");
    getAllOp.Deprecated.Should().BeTrue();
    getAllOp.Summary.Should().Be("List legacy products");
    getAllOp.Description.Should().Contain("DEPRECATED");
    getAllOp.Description.Should().Contain("Migrate to v2 API");
    getAllOp.Description.Should().Contain("Returns all products from the legacy system.");
  }

  [Fact]
  public async Task OpenApi_Configuration_Should_AllowDifferentTagsForDifferentEntities()
  {
    // Arrange - Create two endpoint groups with different entity types and configurations
    var metadataRepository = new MetadataTestRepository();
    var itemRepository = new ItemTestRepository();

    using var host = await new HostBuilder()
        .ConfigureWebHost(webBuilder =>
        {
          webBuilder.UseTestServer();
          webBuilder.ConfigureServices(services =>
          {
            services.AddRouting();
            services.AddOpenApi();
            services.AddSingleton<IRepository<MetadataTestEntity, int>>(metadataRepository);
            services.AddSingleton<IRepository<ItemTestEntity, Guid>>(itemRepository);
          });
          webBuilder.Configure(app =>
          {
            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
              endpoints.MapOpenApi();

              // First entity with custom tag
              endpoints.MapRestLib<MetadataTestEntity, int>("/api/products", config =>
              {
                config.AllowAnonymous();
                config.KeySelector = e => e.Id;
                config.OpenApi.Tag = "Products";
              });

              // Second entity with different custom tag
              endpoints.MapRestLib<ItemTestEntity, Guid>("/api/items", config =>
              {
                config.AllowAnonymous();
                config.KeySelector = e => e.Id;
                config.OpenApi.Tag = "Inventory Items";
              });
            });
          });
        })
        .StartAsync();

    var client = host.GetTestClient();

    // Act
    var openApiDoc = await GetOpenApiDocument(client);

    // Assert - Each endpoint group should have its own tag
    openApiDoc.Paths!["/api/products"]!.Operations[HttpMethod.Get]!.Tags
        .Should().Contain(t => t.Name == "Products");
    openApiDoc.Paths!["/api/items"]!.Operations[HttpMethod.Get]!.Tags
        .Should().Contain(t => t.Name == "Inventory Items");
  }

  #endregion

  #region OpenApi Configuration Object Tests

  [Fact]
  public void OpenApiConfiguration_Should_HaveDefaultValues()
  {
    // Arrange & Act
    var config = new RestLibOpenApiConfiguration();

    // Assert
    config.Tag.Should().BeNull();
    config.TagDescription.Should().BeNull();
    config.Deprecated.Should().BeFalse();
    config.DeprecationMessage.Should().BeNull();
    config.Summaries.Should().NotBeNull();
    config.Descriptions.Should().NotBeNull();
  }

  [Fact]
  public void OpenApiSummaries_Should_ReturnNullForUnconfiguredOperations()
  {
    // Arrange
    var summaries = new RestLibOpenApiSummaries();

    // Assert
    summaries.GetAll.Should().BeNull();
    summaries.GetById.Should().BeNull();
    summaries.Create.Should().BeNull();
    summaries.Update.Should().BeNull();
    summaries.Patch.Should().BeNull();
    summaries.Delete.Should().BeNull();
  }

  [Fact]
  public void OpenApiSummaries_Should_AllowSettingAllProperties()
  {
    // Arrange
    var summaries = new RestLibOpenApiSummaries();

    // Act
    summaries.GetAll = "Test GetAll";
    summaries.GetById = "Test GetById";
    summaries.Create = "Test Create";
    summaries.Update = "Test Update";
    summaries.Patch = "Test Patch";
    summaries.Delete = "Test Delete";

    // Assert
    summaries.GetAll.Should().Be("Test GetAll");
    summaries.GetById.Should().Be("Test GetById");
    summaries.Create.Should().Be("Test Create");
    summaries.Update.Should().Be("Test Update");
    summaries.Patch.Should().Be("Test Patch");
    summaries.Delete.Should().Be("Test Delete");
  }

  [Fact]
  public void OpenApiDescriptions_Should_AllowSettingAllProperties()
  {
    // Arrange
    var descriptions = new RestLibOpenApiDescriptions();

    // Act
    descriptions.GetAll = "Test GetAll description";
    descriptions.GetById = "Test GetById description";
    descriptions.Create = "Test Create description";
    descriptions.Update = "Test Update description";
    descriptions.Patch = "Test Patch description";
    descriptions.Delete = "Test Delete description";

    // Assert
    descriptions.GetAll.Should().Be("Test GetAll description");
    descriptions.GetById.Should().Be("Test GetById description");
    descriptions.Create.Should().Be("Test Create description");
    descriptions.Update.Should().Be("Test Update description");
    descriptions.Patch.Should().Be("Test Patch description");
    descriptions.Delete.Should().Be("Test Delete description");
  }

  [Fact]
  public void RestLibEndpointConfiguration_Should_ExposeOpenApiConfiguration()
  {
    // Arrange & Act
    var config = new RestLibEndpointConfiguration<MetadataTestEntity, int>();

    // Assert
    config.OpenApi.Should().NotBeNull();
    config.OpenApi.Tag.Should().BeNull();
    config.OpenApi.Deprecated.Should().BeFalse();
  }

  [Fact]
  public void RestLibEndpointConfiguration_OpenApi_Should_BeConfigurable()
  {
    // Arrange
    var config = new RestLibEndpointConfiguration<MetadataTestEntity, int>();

    // Act
    config.OpenApi.Tag = "CustomTag";
    config.OpenApi.Deprecated = true;
    config.OpenApi.DeprecationMessage = "Use v2";
    config.OpenApi.Summaries.GetAll = "Custom summary";
    config.OpenApi.Descriptions.Create = "Custom description";

    // Assert
    config.OpenApi.Tag.Should().Be("CustomTag");
    config.OpenApi.Deprecated.Should().BeTrue();
    config.OpenApi.DeprecationMessage.Should().Be("Use v2");
    config.OpenApi.Summaries.GetAll.Should().Be("Custom summary");
    config.OpenApi.Descriptions.Create.Should().Be("Custom description");
  }

  #endregion
}
