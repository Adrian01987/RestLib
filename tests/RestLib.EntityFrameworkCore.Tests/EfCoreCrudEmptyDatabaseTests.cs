using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using RestLib.EntityFrameworkCore.Tests.Fakes;
using RestLib.Serialization;
using Xunit;

namespace RestLib.EntityFrameworkCore.Tests;

/// <summary>
/// CRUD integration tests for empty database scenarios.
/// </summary>
[Trait("Category", "Story9.1")]
public class EfCoreCrudEmptyDatabaseTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = RestLibJsonOptions.CreateDefault();

    private IHost _host = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        (_host, _client, _) = await new EfCoreTestHostBuilder<ProductEntity, Guid>("/api/products")
            .WithEndpoint(config => config.AllowAnonymous())
            .BuildAsync();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    [Fact]
    public async Task GetAll_EmptyDatabase_ReturnsOkWithEmptyItems()
    {
        // Arrange

        // Act
        var response = await _client.GetAsync("/api/products");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var collection = JsonSerializer.Deserialize<CollectionResponse>(
            await response.Content.ReadAsStringAsync(),
            JsonOptions);

        collection.Should().NotBeNull();
        collection!.Items.Should().BeEmpty();
        collection.TotalCount.Should().Be(0);
        collection.Next.Should().BeNull();
    }

    /// <summary>
    /// Deserialization model for collection responses.
    /// </summary>
    private class CollectionResponse
    {
        /// <summary>
        /// Gets or sets the returned items.
        /// </summary>
        public List<ProductEntity> Items { get; set; } = [];

        /// <summary>
        /// Gets or sets the total count.
        /// </summary>
        public long? TotalCount { get; set; }

        /// <summary>
        /// Gets or sets the next page link.
        /// </summary>
        public string? Next { get; set; }
    }
}
