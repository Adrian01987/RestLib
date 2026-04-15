using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RestLib.Abstractions;
using RestLib.EntityFrameworkCore.Tests.Fakes;
using RestLib.Responses;
using Xunit;

namespace RestLib.EntityFrameworkCore.Tests;

/// <summary>
/// Integration tests that verify batch operations work correctly through the full
/// HTTP pipeline with the EF Core adapter.
/// </summary>
[Trait("Category", "Story9.2")]
[Trait("Type", "Integration")]
public class EfCoreBatchIntegrationTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private TestDbContext _db = null!;

    public async Task InitializeAsync()
    {
        (_host, _client, _db) = await new EfCoreTestHostBuilder<ProductEntity, Guid>("/api/products")
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.EnableBatch();
            })
            .BuildAsync();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    [Fact]
    public async Task BatchCreate_ValidItems_Returns200WithCreatedEntities()
    {
        // Arrange
        await ClearProductsAsync();
        var payload = new
        {
            action = "create",
            items = new[]
            {
                new { product_name = "Widget A", unit_price = 10.00m, stock_quantity = 5, is_active = true, created_at = "2025-01-01T00:00:00Z" },
                new { product_name = "Widget B", unit_price = 20.00m, stock_quantity = 10, is_active = true, created_at = "2025-01-01T00:00:00Z" }
            }
        };

        // Act
        var response = await _client.PostAsync("/api/products/batch", BatchJson(payload));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(2);

        items[0].GetProperty("index").GetInt32().Should().Be(0);
        items[0].GetProperty("status").GetInt32().Should().Be(201);
        items[0].GetProperty("entity").GetProperty("product_name").GetString().Should().Be("Widget A");

        items[1].GetProperty("index").GetInt32().Should().Be(1);
        items[1].GetProperty("status").GetInt32().Should().Be(201);
        items[1].GetProperty("entity").GetProperty("product_name").GetString().Should().Be("Widget B");
    }

    [Fact]
    public async Task BatchUpdate_ExistingItems_Returns200WithUpdatedEntities()
    {
        // Arrange
        await ClearProductsAsync();
        var product1 = CreateProduct(name: "Original 1", unitPrice: 10m, stockQuantity: 1);
        var product2 = CreateProduct(name: "Original 2", unitPrice: 20m, stockQuantity: 2);
        await SeedProductsAsync(product1, product2);

        var payload = new
        {
            action = "update",
            items = new[]
            {
                new { id = product1.Id, body = new { product_name = "Updated 1", unit_price = 100m, stock_quantity = 10, is_active = true, created_at = product1.CreatedAt.ToString("O") } },
                new { id = product2.Id, body = new { product_name = "Updated 2", unit_price = 200m, stock_quantity = 20, is_active = true, created_at = product2.CreatedAt.ToString("O") } }
            }
        };

        // Act
        var response = await _client.PostAsync("/api/products/batch", BatchJson(payload));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(2);

        items[0].GetProperty("status").GetInt32().Should().Be(200);
        items[0].GetProperty("entity").GetProperty("product_name").GetString().Should().Be("Updated 1");

        items[1].GetProperty("status").GetInt32().Should().Be(200);
        items[1].GetProperty("entity").GetProperty("product_name").GetString().Should().Be("Updated 2");
    }

    [Fact]
    public async Task BatchPatch_PartialUpdate_Returns200WithPatchedEntities()
    {
        // Arrange
        await ClearProductsAsync();
        var product1 = CreateProduct(name: "Original 1", unitPrice: 10m, stockQuantity: 5);
        var product2 = CreateProduct(name: "Original 2", unitPrice: 20m, stockQuantity: 10);
        await SeedProductsAsync(product1, product2);

        var payload = new
        {
            action = "patch",
            items = new object[]
            {
                new { id = product1.Id, body = new { product_name = "Patched 1" } },
                new { id = product2.Id, body = new { unit_price = 99.99m } }
            }
        };

        // Act
        var response = await _client.PostAsync("/api/products/batch", BatchJson(payload));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(2);

        items[0].GetProperty("status").GetInt32().Should().Be(200);
        items[0].GetProperty("entity").GetProperty("product_name").GetString().Should().Be("Patched 1");
        items[0].GetProperty("entity").GetProperty("unit_price").GetDecimal().Should().Be(10m);

        items[1].GetProperty("status").GetInt32().Should().Be(200);
        items[1].GetProperty("entity").GetProperty("unit_price").GetDecimal().Should().Be(99.99m);
        items[1].GetProperty("entity").GetProperty("product_name").GetString().Should().Be("Original 2");
    }

    [Fact]
    public async Task BatchDelete_ExistingItems_Returns200WithStatus204()
    {
        // Arrange
        await ClearProductsAsync();
        var product1 = CreateProduct(name: "ToDelete 1", unitPrice: 10m, stockQuantity: 1);
        var product2 = CreateProduct(name: "ToDelete 2", unitPrice: 20m, stockQuantity: 2);
        await SeedProductsAsync(product1, product2);

        var payload = new
        {
            action = "delete",
            items = new[] { product1.Id, product2.Id }
        };

        // Act
        var response = await _client.PostAsync("/api/products/batch", BatchJson(payload));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(2);
        items[0].GetProperty("status").GetInt32().Should().Be(204);
        items[1].GetProperty("status").GetInt32().Should().Be(204);

        _db.ChangeTracker.Clear();
        var remaining1 = await _db.Products.FindAsync(product1.Id);
        var remaining2 = await _db.Products.FindAsync(product2.Id);
        remaining1.Should().BeNull();
        remaining2.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdsAsync_WithExistingIds_ReturnsAllEntities()
    {
        // Arrange
        await ClearProductsAsync();
        var product1 = CreateProduct(name: "Product 1", unitPrice: 10m, stockQuantity: 1);
        var product2 = CreateProduct(name: "Product 2", unitPrice: 20m, stockQuantity: 2);
        var product3 = CreateProduct(name: "Product 3", unitPrice: 30m, stockQuantity: 3);
        await SeedProductsAsync(product1, product2, product3);

        var batchRepository = _host.Services.CreateScope().ServiceProvider
            .GetRequiredService<IBatchRepository<ProductEntity, Guid>>();

        // Act
        var result = await batchRepository.GetByIdsAsync([product1.Id, product2.Id, product3.Id]);

        // Assert
        result.Should().HaveCount(3);
        result.Keys.Should().BeEquivalentTo([product1.Id, product2.Id, product3.Id]);
        result[product1.Id].ProductName.Should().Be("Product 1");
    }

    [Fact]
    public async Task BatchCreate_MixedValidation_Returns207WithPerItemStatus()
    {
        // Arrange
        await ClearProductsAsync();
        var payload = new
        {
            action = "create",
            items = new object[]
            {
                new { product_name = "Valid Product", unit_price = 10m, stock_quantity = 5, is_active = true, created_at = "2025-01-01T00:00:00Z" },
                new { product_name = string.Empty, unit_price = 5m, stock_quantity = 1, is_active = true, created_at = "2025-01-01T00:00:00Z" }
            }
        };

        // Act
        var response = await _client.PostAsync("/api/products/batch", BatchJson(payload));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.MultiStatus);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(2);
        items[0].GetProperty("status").GetInt32().Should().Be(201);
        items[0].GetProperty("entity").GetProperty("product_name").GetString().Should().Be("Valid Product");
        items[1].GetProperty("status").GetInt32().Should().Be(400);
        items[1].GetProperty("error").GetProperty("type").GetString().Should().Be(ProblemTypes.ValidationFailed);
    }

    [Fact]
    public async Task BatchCreate_SizeExceeded_Returns400()
    {
        // Arrange
        var (sizedHost, sizedClient, _) = await new EfCoreTestHostBuilder<ProductEntity, Guid>("/api/products")
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.EnableBatch();
            })
            .WithOptions(options => options.MaxBatchSize = 3)
            .BuildAsync();

        try
        {
            var items = Enumerable.Range(0, 5)
                .Select(i => new
                {
                    product_name = $"Item{i}",
                    unit_price = (decimal)i,
                    stock_quantity = i,
                    is_active = true,
                    created_at = "2025-01-01T00:00:00Z"
                })
                .ToArray();
            var payload = new { action = "create", items };

            // Act
            var response = await sizedClient.PostAsync("/api/products/batch", BatchJson(payload));

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            json.GetProperty("type").GetString().Should().Be(ProblemTypes.BatchSizeExceeded);
            json.GetProperty("status").GetInt32().Should().Be(400);
        }
        finally
        {
            sizedClient.Dispose();
            await sizedHost.StopAsync();
            sizedHost.Dispose();
        }
    }

    private static StringContent BatchJson(object payload)
    {
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private static ProductEntity CreateProduct(
        string name = "Test Product",
        decimal unitPrice = 10.00m,
        int stockQuantity = 5,
        bool isActive = true)
    {
        return new ProductEntity
        {
            Id = Guid.NewGuid(),
            ProductName = name,
            UnitPrice = unitPrice,
            StockQuantity = stockQuantity,
            CreatedAt = DateTime.UtcNow,
            IsActive = isActive
        };
    }

    private async Task SeedProductsAsync(params ProductEntity[] products)
    {
        _db.Products.AddRange(products);
        await _db.SaveChangesAsync();
    }

    private async Task ClearProductsAsync()
    {
        _db.Products.RemoveRange(_db.Products);
        await _db.SaveChangesAsync();
    }
}
