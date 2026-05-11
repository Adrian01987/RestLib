using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using RestLib.EntityFrameworkCore.Tests.Fakes;
using Xunit;

namespace RestLib.EntityFrameworkCore.Tests;

/// <summary>
/// Integration tests for collection search in the EF Core adapter.
/// </summary>
[Trait("Feature", "Search")]
[Trait("Type", "Integration")]
public class EfCoreSearchIntegrationTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private TestDbContext _dbContext = null!;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        (_host, _client, _dbContext) = await new EfCoreTestHostBuilder<ProductEntity, Guid>("/api/products")
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.AllowFiltering(product => product.IsActive);
                config.AllowSorting(product => product.ProductName);
                config.AllowFieldSelection(product => product.ProductName, product => product.OptionalDescription);
                config.AllowSearch(product => product.ProductName, product => product.OptionalDescription!);
            })
            .WithRepositoryOptions(options => options.EnableProjectionPushdown = true)
            .BuildAsync();
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    [Fact]
    public async Task Search_WithConfiguredProperties_ReturnsOrOfContainsMatches()
    {
        // Arrange
        await ClearProductsAsync();
        await SeedProductsAsync(
            CreateProduct("Blue Widget", "Basic item"),
            CreateProduct("Accessory", "Widget companion"),
            CreateProduct("Hammer", "Heavy tool"));

        // Act
        var response = await _client.GetAsync("/api/products?q=widget");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        json.GetProperty("items").GetArrayLength().Should().Be(2);
        json.GetProperty("total_count").GetInt64().Should().Be(2);
    }

    [Fact]
    public async Task Search_IsCaseInsensitiveByDefault()
    {
        // Arrange
        await ClearProductsAsync();
        await SeedProductsAsync(CreateProduct("Blue Widget", "Basic item"));

        // Act
        var response = await _client.GetAsync("/api/products?q=WIDGET");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        json.GetProperty("items").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Search_ComposesWithFilteringSortingAndPagination()
    {
        // Arrange
        await ClearProductsAsync();
        await SeedProductsAsync(
            CreateProduct("Beta Widget", "One", isActive: true),
            CreateProduct("Alpha Widget", "Two", isActive: true),
            CreateProduct("Dormant Widget", "Three", isActive: false));

        // Act
        var response = await _client.GetAsync("/api/products?q=widget&is_active=true&sort=product_name:asc&limit=1");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        json.GetProperty("items").GetArrayLength().Should().Be(1);
        json.GetProperty("items")[0].GetProperty("product_name").GetString().Should().Be("Alpha Widget");
        json.GetProperty("total_count").GetInt64().Should().Be(2);
        json.GetProperty("next").ValueKind.Should().Be(JsonValueKind.String);
    }

    [Fact]
    public async Task Search_WithFieldSelection_ReturnsSelectedFieldsOnly()
    {
        // Arrange
        await ClearProductsAsync();
        await SeedProductsAsync(CreateProduct("Blue Widget", "Basic item"));

        // Act
        var response = await _client.GetAsync("/api/products?q=widget&fields=product_name");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var item = json.GetProperty("items")[0];
        item.TryGetProperty("product_name", out _).Should().BeTrue();
        item.TryGetProperty("optional_description", out _).Should().BeFalse();
        json.GetProperty("total_count").GetInt64().Should().Be(1);
    }

    [Fact]
    public async Task Search_WithNestedPath_TranslatesServerSide()
    {
        // Arrange
        var (nestedHost, client, dbContext) = await CreateNestedHostAsync();
        using var _ = nestedHost;

        await ClearOrdersAsync(dbContext);
        await SeedOrdersAsync(
            dbContext,
            ("ORD-001", "Zoe", "zoe@example.com"),
            ("ORD-002", "Adam", "adam@example.com"));

        // Act
        var response = await client.GetAsync("/api/orders?q=zoe@example.com");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        json.GetProperty("items").GetArrayLength().Should().Be(1);
        json.GetProperty("items")[0].GetProperty("order_number").GetString().Should().Be("ORD-001");
        json.GetProperty("total_count").GetInt64().Should().Be(1);
    }

    [Fact]
    public async Task Search_WithNestedFieldSelection_ReturnsMatchedNavigationValue()
    {
        // Arrange
        var (nestedHost, client, dbContext) = await CreateNestedHostAsync(configureEndpoint: config =>
        {
            config.AllowFieldSelection(order => order.OrderNumber, order => order.Customer!.Email);
        });
        using var _ = nestedHost;

        await ClearOrdersAsync(dbContext);
        await SeedOrdersAsync(
            dbContext,
            ("ORD-001", "Zoe", "zoe@example.com"),
            ("ORD-002", "Adam", "adam@example.com"));

        // Act
        var response = await client.GetAsync("/api/orders?q=zoe@example.com&fields=customer.email");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        json.GetProperty("items").GetArrayLength().Should().Be(1);
        json.GetProperty("items")[0].GetProperty("customer.email").GetString().Should().Be("zoe@example.com");
        json.GetProperty("total_count").GetInt64().Should().Be(1);
    }

    [Fact]
    public async Task Search_WhenCaseSensitiveOptionEnabled_RequiresExactCase()
    {
        // Arrange
        var (nestedHost, client, dbContext) = await CreateNestedHostAsync(configureEndpoint: config =>
        {
            config.AllowSearch(options =>
            {
                options.CaseSensitive = true;
            }, order => order.OrderNumber, order => order.Customer!.Email);
        }, includeDefaultSearch: false);
        using var _ = nestedHost;

        await ClearOrdersAsync(dbContext);
        await SeedOrdersAsync(
            dbContext,
            ("ORD-001", "Zoe", "zoe@example.com"));

        // Act
        var exactCaseResponse = await client.GetAsync("/api/orders?q=zoe@example.com");
        var exactCaseJson = await exactCaseResponse.Content.ReadFromJsonAsync<JsonElement>();
        var mismatchedCaseResponse = await client.GetAsync("/api/orders?q=ZOE@EXAMPLE.COM");
        var mismatchedCaseJson = await mismatchedCaseResponse.Content.ReadFromJsonAsync<JsonElement>();

        // Assert
        exactCaseResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        exactCaseJson.GetProperty("items").GetArrayLength().Should().Be(1);

        mismatchedCaseResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        mismatchedCaseJson.GetProperty("items").GetArrayLength().Should().Be(0);
    }

    private static ProductEntity CreateProduct(
        string name,
        string? description,
        bool isActive = true)
    {
        return new ProductEntity
        {
            Id = Guid.NewGuid(),
            ProductName = name,
            OptionalDescription = description,
            UnitPrice = 10m,
            StockQuantity = 1,
            CreatedAt = DateTime.UtcNow,
            IsActive = isActive
        };
    }

    private static async Task<(IHost Host, HttpClient Client, TestDbContext DbContext)> CreateNestedHostAsync(
        Action<RestLib.Configuration.RestLibEndpointConfiguration<OrderEntity, Guid>>? configureEndpoint = null,
        bool includeDefaultSearch = true)
    {
        var builder = new EfCoreTestHostBuilder<OrderEntity, Guid>("/api/orders")
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                if (includeDefaultSearch)
                {
                    config.AllowSearch(order => order.OrderNumber, order => order.Customer!.Email);
                }

                configureEndpoint?.Invoke(config);
            });

        return await builder.BuildAsync();
    }

    private static async Task ClearOrdersAsync(TestDbContext dbContext)
    {
        dbContext.Orders.RemoveRange(dbContext.Orders);
        dbContext.Customers.RemoveRange(dbContext.Customers);
        await dbContext.SaveChangesAsync();
    }

    private static async Task SeedOrdersAsync(
        TestDbContext dbContext,
        params (string OrderNumber, string Name, string Email)[] orders)
    {
        foreach (var (orderNumber, name, email) in orders)
        {
            var customer = new OrderCustomerEntity
            {
                Id = Guid.NewGuid(),
                Name = name,
                Email = email
            };

            dbContext.Customers.Add(customer);
            dbContext.Orders.Add(new OrderEntity
            {
                Id = Guid.NewGuid(),
                OrderNumber = orderNumber,
                TotalAmount = 10m,
                CustomerId = customer.Id,
                Customer = customer
            });
        }

        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
    }

    private async Task ClearProductsAsync()
    {
        _dbContext.Products.RemoveRange(_dbContext.Products);
        await _dbContext.SaveChangesAsync();
    }

    private async Task SeedProductsAsync(params ProductEntity[] products)
    {
        _dbContext.Products.AddRange(products);
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();
    }
}
