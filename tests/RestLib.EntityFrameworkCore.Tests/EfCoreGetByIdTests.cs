using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RestLib.Abstractions;
using RestLib.EntityFrameworkCore.Tests.Fakes;
using RestLib.Responses;
using RestLib.Serialization;
using Xunit;

namespace RestLib.EntityFrameworkCore.Tests;

/// <summary>
/// Integration tests for the EF Core-backed GetById endpoint.
/// </summary>
[Trait("Category", "Story3.1.1")]
public class EfCoreGetByIdTests : IAsyncLifetime
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

        _seededProducts = SeedData.CreateProducts(5);
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
    public async Task GetById_ExistingEntity_ReturnsEntity()
    {
        // Arrange
        var expected = _seededProducts[0];

        // Act
        var response = await _client.GetAsync($"/api/products/{expected.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var entity = JsonSerializer.Deserialize<ProductEntity>(
            await response.Content.ReadAsStringAsync(),
            JsonOptions);

        entity.Should().NotBeNull();
        entity!.Id.Should().Be(expected.Id);
        entity.ProductName.Should().Be(expected.ProductName);
        entity.UnitPrice.Should().Be(expected.UnitPrice);
        entity.StockQuantity.Should().Be(expected.StockQuantity);
        entity.CreatedAt.Should().Be(expected.CreatedAt);
        entity.LastModifiedAt.Should().Be(expected.LastModifiedAt);
        entity.OptionalDescription.Should().Be(expected.OptionalDescription);
        entity.IsActive.Should().Be(expected.IsActive);
        entity.CategoryId.Should().Be(expected.CategoryId);
        entity.Status.Should().Be(expected.Status);
    }

    [Fact]
    public async Task GetById_NonExistentKey_Returns404ProblemDetails()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/products/{id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problem = JsonSerializer.Deserialize<RestLibProblemDetails>(
            await response.Content.ReadAsStringAsync(),
            JsonOptions);

        problem.Should().NotBeNull();
        problem!.Type.Should().Be(ProblemTypes.NotFound);
        problem.Status.Should().Be((int)HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetById_CancellationToken_IsForwarded()
    {
        // Arrange
        using var scope = _host.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<ProductEntity, Guid>>();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var act = () => repository.GetByIdAsync(_seededProducts[0].Id, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
