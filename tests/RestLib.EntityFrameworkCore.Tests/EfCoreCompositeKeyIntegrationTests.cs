using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using RestLib.Configuration;
using RestLib.EntityFrameworkCore.Tests.Fakes;
using RestLib.Responses;
using RestLib.Serialization;
using Xunit;

namespace RestLib.EntityFrameworkCore.Tests;

/// <summary>
/// Integration tests for EF Core resources that use two-part composite keys.
/// </summary>
[Trait("Type", "Integration")]
[Trait("Feature", "CompositeKeys")]
public class EfCoreCompositeKeyIntegrationTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = RestLibJsonOptions.CreateDefault();

    private IHost _host = null!;
    private HttpClient _client = null!;
    private TestDbContext _db = null!;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        (_host, _client, _db) = await new EfCoreTestHostBuilder<TenantProductEntity, RestLibCompositeKey<Guid, string>>("/api/tenant-products")
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.EnableBatch();
                config.AllowSorting(entity => entity.UnitPrice);
                RestLibCompositeKeyConfigurationExtensions.UseCompositeKey<
                    RestLibEndpointConfiguration<TenantProductEntity, RestLibCompositeKey<Guid, string>>,
                    TenantProductEntity,
                    Guid,
                    string>(
                    config,
                    entity => entity.TenantId,
                    "tenantId",
                    entity => entity.Sku,
                    "sku");
            })
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
    public async Task CompositeKeyResource_GetById_ReturnsEntity()
    {
        // Arrange
        var entity = CreateTenantProduct(productName: "Widget A", unitPrice: 12m);
        await SeedTenantProductsAsync(entity);

        // Act
        var response = await _client.GetAsync(GetItemPath(entity));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await DeserializeTenantProductAsync(response);
        body.Should().NotBeNull();
        body!.TenantId.Should().Be(entity.TenantId);
        body.Sku.Should().Be(entity.Sku);
        body.ProductName.Should().Be("Widget A");
    }

    [Fact]
    public async Task CompositeKeyResource_Create_ReturnsLocationWithBothKeySegments()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var payload = new
        {
            tenant_id = tenantId,
            sku = "sku-created",
            product_name = "Created",
            unit_price = 42.5m,
            stock_quantity = 5,
            is_active = true,
            created_at = "2025-01-01T00:00:00Z"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/tenant-products", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().EndWith($"/api/tenant-products/{tenantId}/sku-created");

        var body = await DeserializeTenantProductAsync(response);
        body.Should().NotBeNull();
        body!.TenantId.Should().Be(tenantId);
        body.Sku.Should().Be("sku-created");
    }

    [Fact]
    public async Task CompositeKeyResource_Update_ReplacesEntityByCompositeKey()
    {
        // Arrange
        var entity = CreateTenantProduct(productName: "Original", unitPrice: 10m);
        await SeedTenantProductsAsync(entity);
        var payload = new
        {
            product_name = "Updated",
            unit_price = 99.9m,
            stock_quantity = 44,
            is_active = false,
            created_at = entity.CreatedAt.ToString("O")
        };

        // Act
        var response = await _client.PutAsJsonAsync(GetItemPath(entity), payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await DeserializeTenantProductAsync(response);
        body.Should().NotBeNull();
        body!.TenantId.Should().Be(entity.TenantId);
        body.Sku.Should().Be(entity.Sku);
        body.ProductName.Should().Be("Updated");
        body.UnitPrice.Should().Be(99.9m);
    }

    [Fact]
    public async Task CompositeKeyResource_Patch_UpdatesEntityByCompositeKey()
    {
        // Arrange
        var entity = CreateTenantProduct(productName: "Original", unitPrice: 10m);
        await SeedTenantProductsAsync(entity);
        var payload = new
        {
            product_name = "Patched"
        };

        // Act
        var response = await _client.PatchAsJsonAsync(GetItemPath(entity), payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await DeserializeTenantProductAsync(response);
        body.Should().NotBeNull();
        body!.TenantId.Should().Be(entity.TenantId);
        body.Sku.Should().Be(entity.Sku);
        body.ProductName.Should().Be("Patched");
        body.UnitPrice.Should().Be(10m);
    }

    [Fact]
    public async Task CompositeKeyResource_Delete_RemovesEntityByCompositeKey()
    {
        // Arrange
        var entity = CreateTenantProduct(productName: "Delete me", unitPrice: 10m);
        await SeedTenantProductsAsync(entity);

        // Act
        var response = await _client.DeleteAsync(GetItemPath(entity));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        _db.ChangeTracker.Clear();
        var deleted = await _db.TenantProducts.FindAsync(entity.TenantId, entity.Sku);
        deleted.Should().BeNull();
    }

    [Fact]
    public async Task CompositeKeyResource_GetAll_UsesStablePaginationAcrossDuplicateSortValues()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var entities = new[]
        {
            CreateTenantProduct(tenantId, "sku-a", "A", 10m),
            CreateTenantProduct(tenantId, "sku-b", "B", 10m),
            CreateTenantProduct(tenantId, "sku-c", "C", 10m),
            CreateTenantProduct(tenantId, "sku-d", "D", 20m),
            CreateTenantProduct(tenantId, "sku-e", "E", 20m)
        };
        await SeedTenantProductsAsync(entities);

        var seenKeys = new List<string>();
        string? cursor = null;

        // Act
        do
        {
            var url = cursor is null
                ? "/api/tenant-products?sort=unit_price:asc&limit=2"
                : $"/api/tenant-products?sort=unit_price:asc&limit=2&cursor={Uri.EscapeDataString(cursor)}";
            var response = await _client.GetAsync(url);
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var page = await DeserializeCollectionAsync(response);
            seenKeys.AddRange(page.Items.Select(item => $"{item.TenantId}:{item.Sku}"));
            cursor = GetCursorFromNextLink(page.Next);
        }
        while (cursor is not null);

        // Assert
        seenKeys.Should().HaveCount(5);
        seenKeys.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task CompositeKeyResource_BatchUpdatePatchDelete_UsesCompositeKeys()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var first = CreateTenantProduct(tenantId, "sku-update", "Update", 10m);
        var second = CreateTenantProduct(tenantId, "sku-patch", "Patch", 20m);
        var third = CreateTenantProduct(tenantId, "sku-delete", "Delete", 30m);
        await SeedTenantProductsAsync(first, second, third);

        var updatePayload = new
        {
            action = "update",
            items = new[]
            {
                new
                {
                    id = new { tenant_id = first.TenantId, sku = first.Sku },
                    body = new
                    {
                        product_name = "Updated Name",
                        unit_price = 77.7m,
                        stock_quantity = 9,
                        is_active = false,
                        created_at = first.CreatedAt.ToString("O")
                    }
                }
            }
        };
        var patchPayload = new
        {
            action = "patch",
            items = new[]
            {
                new
                {
                    id = new { tenant_id = second.TenantId, sku = second.Sku },
                    body = new { product_name = "Patched Name" }
                }
            }
        };
        var deletePayload = new
        {
            action = "delete",
            items = new[]
            {
                new { tenant_id = third.TenantId, sku = third.Sku }
            }
        };

        // Act
        var updateResponse = await _client.PostAsync("/api/tenant-products/batch", BatchJson(updatePayload));
        var patchResponse = await _client.PostAsync("/api/tenant-products/batch", BatchJson(patchPayload));
        var deleteResponse = await _client.PostAsync("/api/tenant-products/batch", BatchJson(deletePayload));

        // Assert
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        patchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        _db.ChangeTracker.Clear();
        var updated = await _db.TenantProducts.FindAsync(first.TenantId, first.Sku);
        var patched = await _db.TenantProducts.FindAsync(second.TenantId, second.Sku);
        var deleted = await _db.TenantProducts.FindAsync(third.TenantId, third.Sku);

        updated.Should().NotBeNull();
        updated!.ProductName.Should().Be("Updated Name");
        patched.Should().NotBeNull();
        patched!.ProductName.Should().Be("Patched Name");
        deleted.Should().BeNull();
    }

    [Fact]
    public async Task CompositeKeyResource_GetById_NotFound_UsesCompositeProblemDetails()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var sku = "missing";

        // Act
        var response = await _client.GetAsync($"/api/tenant-products/{tenantId}/{sku}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var problem = await DeserializeProblemAsync(response);
        problem.Should().NotBeNull();
        problem!.Type.Should().Be(ProblemTypes.NotFound);
        problem.Detail.Should().Contain("tenantId");
        problem.Detail.Should().Contain("sku");
    }

    private static StringContent BatchJson(object payload)
    {
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private static string GetCursorFromNextLink(string? next)
    {
        if (string.IsNullOrEmpty(next))
        {
            return null!;
        }

        var uri = new Uri(next, UriKind.RelativeOrAbsolute);
        var query = uri.IsAbsoluteUri ? uri.Query : next[next.IndexOf('?', StringComparison.Ordinal)..];
        var pairs = query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(parts => parts[0], parts => parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty, StringComparer.OrdinalIgnoreCase);

        return pairs.TryGetValue("cursor", out var cursor) ? cursor : null!;
    }

    private static string GetItemPath(TenantProductEntity entity)
    {
        return $"/api/tenant-products/{entity.TenantId}/{entity.Sku}";
    }

    private static TenantProductEntity CreateTenantProduct(
        Guid? tenantId = null,
        string sku = "sku-1",
        string productName = "Widget",
        decimal unitPrice = 10m)
    {
        return new TenantProductEntity
        {
            TenantId = tenantId ?? Guid.NewGuid(),
            Sku = sku,
            ProductName = productName,
            UnitPrice = unitPrice,
            StockQuantity = 5,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
    }

    private async Task SeedTenantProductsAsync(params TenantProductEntity[] entities)
    {
        await ClearTenantProductsAsync();
        _db.TenantProducts.AddRange(entities);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    private async Task ClearTenantProductsAsync()
    {
        _db.TenantProducts.RemoveRange(_db.TenantProducts);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    private async Task<TenantProductEntity?> DeserializeTenantProductAsync(HttpResponseMessage response)
    {
        return JsonSerializer.Deserialize<TenantProductEntity>(
            await response.Content.ReadAsStringAsync(),
            JsonOptions);
    }

    private async Task<CollectionResponse> DeserializeCollectionAsync(HttpResponseMessage response)
    {
        return JsonSerializer.Deserialize<CollectionResponse>(
            await response.Content.ReadAsStringAsync(),
            JsonOptions)!;
    }

    private async Task<RestLibProblemDetails?> DeserializeProblemAsync(HttpResponseMessage response)
    {
        return JsonSerializer.Deserialize<RestLibProblemDetails>(
            await response.Content.ReadAsStringAsync(),
            JsonOptions);
    }

    private sealed class CollectionResponse
    {
        /// <summary>
        /// Gets or sets the returned items.
        /// </summary>
        public List<TenantProductEntity> Items { get; set; } = [];

        /// <summary>
        /// Gets or sets the next page link.
        /// </summary>
        public string? Next { get; set; }
    }
}
