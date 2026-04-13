using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using RestLib.EntityFrameworkCore.Tests.Fakes;
using Xunit;

namespace RestLib.EntityFrameworkCore.Tests;

/// <summary>
/// Integration tests for countable collection responses with an empty table.
/// </summary>
[Trait("Category", "Story3.2.5")]
public class EfCoreCountEmptyTests : IAsyncLifetime
{
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
    public async Task GetAll_EmptyTable_ReturnsTotalCountZero()
    {
        // Arrange

        // Act
        var response = await _client.GetAsync("/api/products");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        doc.RootElement.TryGetProperty("total_count", out var totalCount).Should().BeTrue();
        totalCount.GetInt64().Should().Be(0);
    }
}
