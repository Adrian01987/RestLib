using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using RestLib.EntityFrameworkCore.Tests.Fakes;
using Xunit;

namespace RestLib.EntityFrameworkCore.Tests;

/// <summary>
/// Integration tests for nested scalar property paths in the EF Core adapter.
/// </summary>
[Trait("Feature", "NestedPaths")]
[Trait("Type", "Integration")]
public class EfCoreNestedPropertyPathTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private TestDbContext _dbContext = null!;

    public async Task InitializeAsync()
    {
        (_host, _client, _dbContext) = await new EfCoreTestHostBuilder<OrderEntity, Guid>("/api/orders")
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.AllowFiltering(order => order.Customer!.Email, RestLib.Filtering.FilterOperators.String);
                config.AllowSorting(order => order.Customer!.Name, order => order.OrderNumber);
                config.AllowFieldSelection(order => order.Id, order => order.OrderNumber, order => order.Customer!.Email);
            })
            .WithRepositoryOptions(options => options.EnableProjectionPushdown = true)
            .BuildAsync();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    [Fact]
    public async Task NestedFiltering_ByCustomerEmail_ReturnsMatchingOrders()
    {
        // Arrange
        await SeedOrdersAsync();

        // Act
        var response = await _client.GetAsync("/api/orders?customer.email[contains]=example.com");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(3);
    }

    [Fact]
    public async Task NestedSorting_ByCustomerName_OrdersResults()
    {
        // Arrange
        await SeedOrdersAsync();

        // Act
        var response = await _client.GetAsync("/api/orders?sort=customer.name:asc");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        items[0].GetProperty("order_number").GetString().Should().Be("ORD-002");
        items[1].GetProperty("order_number").GetString().Should().Be("ORD-003");
        items[2].GetProperty("order_number").GetString().Should().Be("ORD-001");
    }

    [Fact]
    public async Task NestedFieldSelection_ByCustomerEmail_ReturnsSelectedNestedField()
    {
        // Arrange
        await SeedOrdersAsync();

        // Act
        var response = await _client.GetAsync("/api/orders?fields=order_number,customer.email&sort=order_number:asc");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var firstItem = json.GetProperty("items")[0];
        firstItem.TryGetProperty("order_number", out _).Should().BeTrue();
        firstItem.TryGetProperty("customer.email", out _).Should().BeTrue();
        firstItem.TryGetProperty("customer", out _).Should().BeFalse();
        firstItem.GetProperty("customer.email").GetString().Should().Be("zoe@example.com");
    }

    private async Task SeedOrdersAsync()
    {
        _dbContext.Orders.RemoveRange(_dbContext.Orders);
        _dbContext.Customers.RemoveRange(_dbContext.Customers);
        await _dbContext.SaveChangesAsync();

        var zoeCustomerId = Guid.Parse("00000000-0000-0000-0000-000000000101");
        var adamCustomerId = Guid.Parse("00000000-0000-0000-0000-000000000102");
        var adamOrderId = Guid.Parse("00000000-0000-0000-0000-000000000201");
        var zoeFirstOrderId = Guid.Parse("00000000-0000-0000-0000-000000000202");
        var zoeSecondOrderId = Guid.Parse("00000000-0000-0000-0000-000000000203");

        var customerA = new OrderCustomerEntity
        {
            Id = zoeCustomerId,
            Name = "Zoe",
            Email = "zoe@example.com"
        };
        var customerB = new OrderCustomerEntity
        {
            Id = adamCustomerId,
            Name = "Adam",
            Email = "adam@example.com"
        };

        _dbContext.Customers.AddRange(customerA, customerB);
        _dbContext.Orders.AddRange(
            new OrderEntity
            {
                Id = zoeSecondOrderId,
                OrderNumber = "ORD-001",
                TotalAmount = 10m,
                CustomerId = customerA.Id,
                Customer = customerA
            },
            new OrderEntity
            {
                Id = adamOrderId,
                OrderNumber = "ORD-002",
                TotalAmount = 20m,
                CustomerId = customerB.Id,
                Customer = customerB
            },
            new OrderEntity
            {
                Id = zoeFirstOrderId,
                OrderNumber = "ORD-003",
                TotalAmount = 30m,
                CustomerId = customerA.Id,
                Customer = customerA
            });

        await _dbContext.SaveChangesAsync();
    }
}
