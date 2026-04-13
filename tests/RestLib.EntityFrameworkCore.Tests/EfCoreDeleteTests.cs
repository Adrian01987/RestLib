using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using RestLib.EntityFrameworkCore.Tests.Fakes;
using RestLib.Responses;
using RestLib.Serialization;
using Xunit;

namespace RestLib.EntityFrameworkCore.Tests;

/// <summary>
/// Integration tests for the EF Core-backed Delete endpoint.
/// </summary>
[Trait("Category", "Story3.2.3")]
public class EfCoreDeleteTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = RestLibJsonOptions.CreateDefault();

    private IHost _host = null!;
    private HttpClient _client = null!;
    private TestDbContext _db = null!;
    private List<ProductEntity> _seededProducts = null!;

    /// <summary>
    /// Sets up the test host and seeds products.
    /// </summary>
    public async Task InitializeAsync()
    {
        (_host, _client, _db) = await new EfCoreTestHostBuilder<ProductEntity, Guid>("/api/products")
            .WithEndpoint(config => config.AllowAnonymous())
            .BuildAsync();

        _seededProducts = SeedData.CreateProducts(3);
        _db.Products.AddRange(_seededProducts);
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Cleans up the test host and HTTP client.
    /// </summary>
    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    [Fact]
    public async Task Delete_ExistingEntity_Returns204NoContent()
    {
        // Arrange
        var id = _seededProducts[0].Id;

        // Act
        var response = await _client.DeleteAsync($"/api/products/{id}");
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        body.Should().BeEmpty();
    }

    [Fact]
    public async Task Delete_ExistingEntity_IsNoLongerRetrievable()
    {
        // Arrange
        var id = _seededProducts[1].Id;
        await _client.DeleteAsync($"/api/products/{id}");

        // Act
        var getResponse = await _client.GetAsync($"/api/products/{id}");

        // Assert
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_NonExistentKey_Returns404ProblemDetails()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act
        var response = await _client.DeleteAsync($"/api/products/{id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problem = JsonSerializer.Deserialize<RestLibProblemDetails>(
            await response.Content.ReadAsStringAsync(),
            JsonOptions);

        problem.Should().NotBeNull();
        problem!.Type.Should().Be(ProblemTypes.NotFound);
    }
}
