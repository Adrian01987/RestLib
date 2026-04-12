using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RestLib.Abstractions;
using RestLib.EntityFrameworkCore.Tests.Fakes;
using Xunit;

namespace RestLib.EntityFrameworkCore.Tests;

/// <summary>
/// Smoke tests verifying that <see cref="EfCoreTestHostBuilder{TEntity, TKey}"/>
/// creates a working test host with EF Core services.
/// </summary>
[Trait("Category", "Story2.1.2")]
public class EfCoreTestHostBuilderSmokeTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private TestDbContext _db = null!;

    /// <summary>
    /// Sets up the test host using the EF Core test host builder.
    /// </summary>
    public async Task InitializeAsync()
    {
        (_host, _client, _db) = await new EfCoreTestHostBuilder<ProductEntity, Guid>("/api/products")
            .WithEndpoint(config => config.AllowAnonymous())
            .BuildAsync();
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
    public void BuildAsync_HostStarts_Successfully()
    {
        // Arrange

        // Act

        // Assert
        _host.Should().NotBeNull();
        _client.Should().NotBeNull();
    }

    [Fact]
    public void BuildAsync_TestDbContext_IsAccessible()
    {
        // Arrange

        // Act
        var dbContext = _db;

        // Assert
        dbContext.Should().NotBeNull();
    }

    [Fact]
    public async Task BuildAsync_CanSeedData_ViaTestDbContext()
    {
        // Arrange
        var products = SeedData.CreateProducts(3);

        // Act
        _db.Products.AddRange(products);
        await _db.SaveChangesAsync();
        var count = await _db.Products.CountAsync();

        // Assert
        count.Should().Be(3);
    }

    [Fact]
    public async Task BuildAsync_CanSeedAndQuery_Categories()
    {
        // Arrange
        var categories = SeedData.CreateCategories(2);

        // Act
        _db.Categories.AddRange(categories);
        await _db.SaveChangesAsync();
        var count = await _db.Categories.CountAsync();

        // Assert
        count.Should().Be(2);
    }

    [Fact]
    public async Task BuildAsync_DatabaseIsIsolated_PerTest()
    {
        // Arrange

        // Act
        var count = await _db.Products.CountAsync();

        // Assert
        count.Should().Be(0);
    }

    [Fact]
    public void BuildAsync_IRepository_IsResolvable()
    {
        // Arrange
        using var scope = _host.Services.CreateScope();

        // Act
        var repository = scope.ServiceProvider.GetService<IRepository<ProductEntity, Guid>>();

        // Assert
        repository.Should().NotBeNull();
    }
}
