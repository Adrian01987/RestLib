using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using RestLib.EntityFrameworkCore.Tests.Fakes;
using RestLib.Serialization;
using Xunit;

namespace RestLib.EntityFrameworkCore.Tests;

/// <summary>
/// Integration tests for empty-table GetAll behavior.
/// </summary>
[Trait("Category", "Story3.1.2")]
public class EfCoreGetAllEmptyTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = RestLibJsonOptions.CreateDefault();

    private IHost _host = null!;
    private HttpClient _client = null!;

    /// <summary>
    /// Sets up the test host with an empty database.
    /// </summary>
    public async Task InitializeAsync()
    {
        (_host, _client, _) = await new EfCoreTestHostBuilder<ProductEntity, Guid>("/api/products")
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
    public async Task GetAll_EmptyTable_ReturnsEmptyItemsWithNoNextCursor()
    {
        // Arrange

        // Act
        var response = await _client.GetAsync("/api/products");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = JsonSerializer.Deserialize<CollectionResponse>(
            await response.Content.ReadAsStringAsync(),
            JsonOptions);

        content.Should().NotBeNull();
        content!.Items.Should().BeEmpty();
        content.TotalCount.Should().Be(0);
        content.Next.Should().BeNull();
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
