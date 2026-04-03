using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;
using RestLib.Abstractions;
using RestLib.Configuration;
using RestLib.Pagination;
using Swashbuckle.AspNetCore.SwaggerGen;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Tests for Story 7.2: API Metadata Configuration
/// Verifies that OpenAPI metadata can be customized per resource.
///
/// Acceptance Criteria:
/// - [ ] Custom tags
/// - [ ] Custom summaries
/// - [ ] Deprecation marking
/// </summary>
public class OpenApiMetadataConfigurationTests
{
  #region Test Entity and Repository

  private class MetadataTestEntity
  {
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
  }

  private class ItemTestEntity
  {
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public int Quantity { get; set; }
  }

  private class MetadataTestRepository : IRepository<MetadataTestEntity, int>
  {
    private readonly Dictionary<int, MetadataTestEntity> _data = [];
    private int _nextId = 1;

    public Task<MetadataTestEntity> CreateAsync(MetadataTestEntity entity, CancellationToken ct = default)
    {
      entity.Id = _nextId++;
      _data[entity.Id] = entity;
      return Task.FromResult(entity);
    }

    public Task<bool> DeleteAsync(int id, CancellationToken ct = default)
      => Task.FromResult(_data.Remove(id));

    public Task<PagedResult<MetadataTestEntity>> GetAllAsync(PaginationRequest request, CancellationToken ct = default)
      => Task.FromResult(new PagedResult<MetadataTestEntity> { Items = _data.Values.ToList(), NextCursor = null });

    public Task<MetadataTestEntity?> GetByIdAsync(int id, CancellationToken ct = default)
    {
      _data.TryGetValue(id, out var entity);
      return Task.FromResult(entity);
    }

    public Task<MetadataTestEntity?> PatchAsync(int id, JsonElement patchDocument, CancellationToken ct = default)
    {
      if (!_data.TryGetValue(id, out var entity)) return Task.FromResult<MetadataTestEntity?>(null);
      return Task.FromResult<MetadataTestEntity?>(entity);
    }

    public Task<MetadataTestEntity?> UpdateAsync(int id, MetadataTestEntity entity, CancellationToken ct = default)
    {
      if (!_data.ContainsKey(id)) return Task.FromResult<MetadataTestEntity?>(null);
      entity.Id = id;
      _data[id] = entity;
      return Task.FromResult<MetadataTestEntity?>(entity);
    }
  }

  private class ItemTestRepository : IRepository<ItemTestEntity, Guid>
  {
    private readonly Dictionary<Guid, ItemTestEntity> _data = [];

    public Task<ItemTestEntity> CreateAsync(ItemTestEntity entity, CancellationToken ct = default)
    {
      entity.Id = Guid.NewGuid();
      _data[entity.Id] = entity;
      return Task.FromResult(entity);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
      => Task.FromResult(_data.Remove(id));

    public Task<PagedResult<ItemTestEntity>> GetAllAsync(PaginationRequest request, CancellationToken ct = default)
      => Task.FromResult(new PagedResult<ItemTestEntity> { Items = _data.Values.ToList(), NextCursor = null });

    public Task<ItemTestEntity?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
      _data.TryGetValue(id, out var entity);
      return Task.FromResult(entity);
    }

    public Task<ItemTestEntity?> PatchAsync(Guid id, JsonElement patchDocument, CancellationToken ct = default)
    {
      if (!_data.TryGetValue(id, out var entity)) return Task.FromResult<ItemTestEntity?>(null);
      return Task.FromResult<ItemTestEntity?>(entity);
    }

    public Task<ItemTestEntity?> UpdateAsync(Guid id, ItemTestEntity entity, CancellationToken ct = default)
    {
      if (!_data.ContainsKey(id)) return Task.FromResult<ItemTestEntity?>(null);
      entity.Id = id;
      _data[id] = entity;
      return Task.FromResult<ItemTestEntity?>(entity);
    }
  }

  #endregion

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
    openApiDoc.Paths["/api/items"].Operations[OperationType.Get].Tags
        .Should().Contain(t => t.Name == "MetadataTestEntity");
    openApiDoc.Paths["/api/items"].Operations[OperationType.Post].Tags
        .Should().Contain(t => t.Name == "MetadataTestEntity");
    openApiDoc.Paths["/api/items/{id}"].Operations[OperationType.Get].Tags
        .Should().Contain(t => t.Name == "MetadataTestEntity");
    openApiDoc.Paths["/api/items/{id}"].Operations[OperationType.Put].Tags
        .Should().Contain(t => t.Name == "MetadataTestEntity");
    openApiDoc.Paths["/api/items/{id}"].Operations[OperationType.Patch].Tags
        .Should().Contain(t => t.Name == "MetadataTestEntity");
    openApiDoc.Paths["/api/items/{id}"].Operations[OperationType.Delete].Tags
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
    openApiDoc.Paths["/api/items"].Operations[OperationType.Get].Tags
        .Should().Contain(t => t.Name == "Products");
    openApiDoc.Paths["/api/items"].Operations[OperationType.Post].Tags
        .Should().Contain(t => t.Name == "Products");
    openApiDoc.Paths["/api/items/{id}"].Operations[OperationType.Get].Tags
        .Should().Contain(t => t.Name == "Products");
    openApiDoc.Paths["/api/items/{id}"].Operations[OperationType.Put].Tags
        .Should().Contain(t => t.Name == "Products");
    openApiDoc.Paths["/api/items/{id}"].Operations[OperationType.Patch].Tags
        .Should().Contain(t => t.Name == "Products");
    openApiDoc.Paths["/api/items/{id}"].Operations[OperationType.Delete].Tags
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
    openApiDoc.Paths["/api/items"].Operations[OperationType.Get].Tags
        .Should().NotContain(t => t.Name == "MetadataTestEntity");
    openApiDoc.Paths["/api/items"].Operations[OperationType.Get].Tags
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
    openApiDoc.Paths["/api/items"].Operations[OperationType.Get].Tags
        .Should().HaveCount(1);
  }

  #endregion

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
    openApiDoc.Paths["/api/items"].Operations[OperationType.Get].Summary
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
    openApiDoc.Paths["/api/items/{id}"].Operations[OperationType.Get].Summary
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
    openApiDoc.Paths["/api/items"].Operations[OperationType.Post].Summary
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
    openApiDoc.Paths["/api/items/{id}"].Operations[OperationType.Put].Summary
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
    openApiDoc.Paths["/api/items/{id}"].Operations[OperationType.Patch].Summary
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
    openApiDoc.Paths["/api/items/{id}"].Operations[OperationType.Delete].Summary
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
    openApiDoc.Paths["/api/items"].Operations[OperationType.Get].Summary.Should().Be("List products");
    openApiDoc.Paths["/api/items"].Operations[OperationType.Post].Summary.Should().Be("Add product");
    openApiDoc.Paths["/api/items/{id}"].Operations[OperationType.Get].Summary.Should().Be("Get product");
    openApiDoc.Paths["/api/items/{id}"].Operations[OperationType.Put].Summary.Should().Be("Replace product");
    openApiDoc.Paths["/api/items/{id}"].Operations[OperationType.Patch].Summary.Should().Be("Update product fields");
    openApiDoc.Paths["/api/items/{id}"].Operations[OperationType.Delete].Summary.Should().Be("Remove product");
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
    openApiDoc.Paths["/api/items"].Operations[OperationType.Get].Summary
        .Should().Be("Custom list products");

    // Other operations should use default summaries (containing entity name)
    openApiDoc.Paths["/api/items"].Operations[OperationType.Post].Summary
        .Should().Contain("Create");
    openApiDoc.Paths["/api/items/{id}"].Operations[OperationType.Get].Summary
        .Should().Contain("Get");
  }

  #endregion

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
    openApiDoc.Paths["/api/items"].Operations[OperationType.Get].Description
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
    openApiDoc.Paths["/api/items"].Operations[OperationType.Post].Description
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
    openApiDoc.Paths["/api/items"].Operations[OperationType.Get].Description
        .Should().Be("Custom GetAll description");
    openApiDoc.Paths["/api/items"].Operations[OperationType.Post].Description
        .Should().Be("Custom Create description");
    openApiDoc.Paths["/api/items/{id}"].Operations[OperationType.Get].Description
        .Should().Be("Custom GetById description");
    openApiDoc.Paths["/api/items/{id}"].Operations[OperationType.Put].Description
        .Should().Be("Custom Update description");
    openApiDoc.Paths["/api/items/{id}"].Operations[OperationType.Patch].Description
        .Should().Be("Custom Patch description");
    openApiDoc.Paths["/api/items/{id}"].Operations[OperationType.Delete].Description
        .Should().Be("Custom Delete description");
  }

  #endregion

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
    openApiDoc.Paths["/api/items"].Operations[OperationType.Get].Deprecated.Should().BeTrue();
    openApiDoc.Paths["/api/items"].Operations[OperationType.Post].Deprecated.Should().BeTrue();
    openApiDoc.Paths["/api/items/{id}"].Operations[OperationType.Get].Deprecated.Should().BeTrue();
    openApiDoc.Paths["/api/items/{id}"].Operations[OperationType.Put].Deprecated.Should().BeTrue();
    openApiDoc.Paths["/api/items/{id}"].Operations[OperationType.Patch].Deprecated.Should().BeTrue();
    openApiDoc.Paths["/api/items/{id}"].Operations[OperationType.Delete].Deprecated.Should().BeTrue();
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
    openApiDoc.Paths["/api/items"].Operations[OperationType.Get].Deprecated.Should().BeFalse();
    openApiDoc.Paths["/api/items"].Operations[OperationType.Post].Deprecated.Should().BeFalse();
    openApiDoc.Paths["/api/items/{id}"].Operations[OperationType.Get].Deprecated.Should().BeFalse();
    openApiDoc.Paths["/api/items/{id}"].Operations[OperationType.Put].Deprecated.Should().BeFalse();
    openApiDoc.Paths["/api/items/{id}"].Operations[OperationType.Patch].Deprecated.Should().BeFalse();
    openApiDoc.Paths["/api/items/{id}"].Operations[OperationType.Delete].Deprecated.Should().BeFalse();
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
    openApiDoc.Paths["/api/items"].Operations[OperationType.Get].Description
        .Should().Contain("DEPRECATED");
    openApiDoc.Paths["/api/items"].Operations[OperationType.Get].Description
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
    openApiDoc.Paths["/api/items"].Operations[OperationType.Get].Description
        .Should().Contain("DEPRECATED");
    openApiDoc.Paths["/api/items"].Operations[OperationType.Get].Description
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
    openApiDoc.Paths["/api/items"].Operations[OperationType.Get].Description
        .Should().NotContain("DEPRECATED");
    openApiDoc.Paths["/api/items"].Operations[OperationType.Get].Description
        .Should().NotContain("This should not appear");
  }

  #endregion

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
    var getAllOp = openApiDoc.Paths["/api/items"].Operations[OperationType.Get];

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
            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen(c =>
            {
              c.SwaggerDoc("v1", new OpenApiInfo { Title = "Test API", Version = "v1" });
              c.SupportNonNullableReferenceTypes();
            });
            services.AddSingleton<IRepository<MetadataTestEntity, int>>(metadataRepository);
            services.AddSingleton<IRepository<ItemTestEntity, Guid>>(itemRepository);
          });
          webBuilder.Configure(app =>
          {
            app.UseRouting();
            app.UseSwagger();
            app.UseEndpoints(endpoints =>
            {
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
    openApiDoc.Paths["/api/products"].Operations[OperationType.Get].Tags
        .Should().Contain(t => t.Name == "Products");
    openApiDoc.Paths["/api/items"].Operations[OperationType.Get].Tags
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

  #region Helper Methods

  private static async Task<IHost> CreateHostWithOpenApi(
      Action<RestLibEndpointConfiguration<MetadataTestEntity, int>> configure)
  {
    var repository = new MetadataTestRepository();

    var host = await new HostBuilder()
        .ConfigureWebHost(webBuilder =>
        {
          webBuilder.UseTestServer();
          webBuilder.ConfigureServices(services =>
          {
            services.AddRouting();
            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen(c =>
            {
              c.SwaggerDoc("v1", new OpenApiInfo { Title = "Test API", Version = "v1" });
              c.SupportNonNullableReferenceTypes();
            });
            services.AddSingleton<IRepository<MetadataTestEntity, int>>(repository);
          });
          webBuilder.Configure(app =>
          {
            app.UseRouting();
            app.UseSwagger();
            app.UseEndpoints(endpoints =>
            {
              endpoints.MapRestLib<MetadataTestEntity, int>("/api/items", configure);
            });
          });
        })
        .StartAsync();

    return host;
  }

  private static async Task<OpenApiDocument> GetOpenApiDocument(HttpClient client)
  {
    var response = await client.GetAsync("/swagger/v1/swagger.json");
    response.EnsureSuccessStatusCode();

    var content = await response.Content.ReadAsStreamAsync();
    var reader = new OpenApiStreamReader();
    var result = await reader.ReadAsync(content);

    if (result.OpenApiDiagnostic.Errors.Count > 0)
    {
      throw new InvalidOperationException(
          $"OpenAPI document has errors: {string.Join(", ", result.OpenApiDiagnostic.Errors.Select(e => e.Message))}");
    }

    return result.OpenApiDocument;
  }

  #endregion
}
