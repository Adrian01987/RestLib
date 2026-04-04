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

/// <summary>
/// Tests for Story 7.2: API Metadata Configuration
/// Verifies that OpenAPI metadata can be customized per resource.
///
/// Acceptance Criteria:
/// - [ ] Custom tags
/// - [ ] Custom summaries
/// - [ ] Deprecation marking
/// </summary>
public partial class OpenApiMetadataConfigurationTests
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
    var result = await OpenApiDocument.LoadAsync(content, "json");

    return result.Document!;
  }

  #endregion
}
