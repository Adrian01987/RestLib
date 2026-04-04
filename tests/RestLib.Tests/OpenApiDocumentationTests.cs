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
/// Tests for Story 7.1: OpenAPI 3.1 Integration
/// Verifies that RestLib endpoints are properly documented in OpenAPI spec.
///
/// Acceptance Criteria:
/// - [ ] All endpoints in OpenAPI spec
/// - [ ] Schemas documented
/// - [ ] Status codes documented
/// - [ ] Parameters with constraints
/// </summary>
public partial class OpenApiDocumentationTests
{
  #region Test Entity and Repository

  private class OpenApiTestEntity
  {
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public bool IsActive { get; set; }
    public Guid CategoryId { get; set; }
    public DateTime CreatedAt { get; set; }
  }

  private class OpenApiTestRepository : IRepository<OpenApiTestEntity, int>
  {
    private readonly Dictionary<int, OpenApiTestEntity> _data = [];
    private int _nextId = 1;

    public void AddTestData(params OpenApiTestEntity[] entities)
    {
      foreach (var entity in entities)
      {
        _data[entity.Id] = entity;
        if (entity.Id >= _nextId) _nextId = entity.Id + 1;
      }
    }

    public Task<OpenApiTestEntity> CreateAsync(OpenApiTestEntity entity, CancellationToken ct = default)
    {
      entity.Id = _nextId++;
      _data[entity.Id] = entity;
      return Task.FromResult(entity);
    }

    public Task<bool> DeleteAsync(int id, CancellationToken ct = default)
      => Task.FromResult(_data.Remove(id));

    public Task<PagedResult<OpenApiTestEntity>> GetAllAsync(PaginationRequest request, CancellationToken ct = default)
      => Task.FromResult(new PagedResult<OpenApiTestEntity> { Items = _data.Values.ToList(), NextCursor = null });

    public Task<OpenApiTestEntity?> GetByIdAsync(int id, CancellationToken ct = default)
    {
      _data.TryGetValue(id, out var entity);
      return Task.FromResult(entity);
    }

    public Task<OpenApiTestEntity?> PatchAsync(int id, JsonElement patchDocument, CancellationToken ct = default)
    {
      if (!_data.TryGetValue(id, out var entity)) return Task.FromResult<OpenApiTestEntity?>(null);
      return Task.FromResult<OpenApiTestEntity?>(entity);
    }

    public Task<OpenApiTestEntity?> UpdateAsync(int id, OpenApiTestEntity entity, CancellationToken ct = default)
    {
      if (!_data.ContainsKey(id)) return Task.FromResult<OpenApiTestEntity?>(null);
      entity.Id = id;
      _data[id] = entity;
      return Task.FromResult<OpenApiTestEntity?>(entity);
    }
  }

  #endregion

  #region Helper Methods

  private static async Task<IHost> CreateHostWithOpenApi()
  {
    var repository = new OpenApiTestRepository();

    var host = await new HostBuilder()
        .ConfigureWebHost(webBuilder =>
        {
          webBuilder.UseTestServer();
          webBuilder.ConfigureServices(services =>
          {
            services.AddRestLib();
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

    return host;
  }

  private static async Task<IHost> CreateHostWithFilters()
  {
    var repository = new OpenApiTestRepository();

    var host = await new HostBuilder()
        .ConfigureWebHost(webBuilder =>
        {
          webBuilder.UseTestServer();
          webBuilder.ConfigureServices(services =>
          {
            services.AddRestLib();
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
                config.AllowFiltering(e => e.IsActive, e => e.CategoryId);
              });
            });
          });
        })
        .StartAsync();

    return host;
  }

  private static async Task<OpenApiDocument> GetOpenApiDocument(HttpClient client)
  {
    var response = await client.GetAsync("/openapi/v1.json");
    response.EnsureSuccessStatusCode();

    var content = await response.Content.ReadAsStreamAsync();
    var result = await OpenApiDocument.LoadAsync(content, "json");

    return result.Document!;
  }

  #endregion
}
