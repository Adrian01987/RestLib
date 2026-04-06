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
using RestLib.InMemory;
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
    #region Test Entity

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

    #endregion

    #region Helper Methods

    private static async Task<IHost> CreateHostWithOpenApi()
    {
        var nextId = 1;
        var repository = new InMemoryRepository<OpenApiTestEntity, int>(e => e.Id, () => nextId++);

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
        var nextId = 1;
        var repository = new InMemoryRepository<OpenApiTestEntity, int>(e => e.Id, () => nextId++);

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
