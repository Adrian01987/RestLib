using System.Net.Http;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi;
using RestLib.InMemory;
using RestLib.Tests.Fakes;
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
[Trait("Type", "Integration")]
[Trait("Feature", "OpenApi")]
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

        var (host, _) = await new TestHostBuilder<OpenApiTestEntity, int>(repository, "/api/items")
            .WithServices(services => services.AddOpenApi())
            .WithAdditionalEndpoints(endpoints => endpoints.MapOpenApi())
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.KeySelector = e => e.Id;
            })
            .BuildAsync();

        return host;
    }

    private static async Task<IHost> CreateHostWithFilters()
    {
        var nextId = 1;
        var repository = new InMemoryRepository<OpenApiTestEntity, int>(e => e.Id, () => nextId++);

        var (host, _) = await new TestHostBuilder<OpenApiTestEntity, int>(repository, "/api/items")
            .WithServices(services => services.AddOpenApi())
            .WithAdditionalEndpoints(endpoints => endpoints.MapOpenApi())
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.KeySelector = e => e.Id;
                config.AllowFiltering(e => e.IsActive, e => e.CategoryId);
            })
            .BuildAsync();

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
