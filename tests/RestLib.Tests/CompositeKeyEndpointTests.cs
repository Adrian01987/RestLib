using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi;
using RestLib.Abstractions;
using RestLib.Configuration;
using RestLib.InMemory;
using RestLib.Responses;
using RestLib.Serialization;
using RestLib.Tests.Fakes;
using Xunit;

namespace RestLib.Tests;

[Trait("Type", "Integration")]
[Trait("Feature", "CompositeKeys")]
public class CompositeKeyEndpointTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = RestLibJsonOptions.CreateDefault();

    private readonly InMemoryRepository<CompositeCatalogItem, RestLibCompositeKey<Guid, string>> _repository;
    private IHost _host = null!;
    private HttpClient _client = null!;

    public CompositeKeyEndpointTests()
    {
        _repository = new InMemoryRepository<CompositeCatalogItem, RestLibCompositeKey<Guid, string>>(
            static entity => new RestLibCompositeKey<Guid, string>(entity.TenantId, entity.Sku),
            static () => new RestLibCompositeKey<Guid, string>(Guid.NewGuid(), $"generated-{Guid.NewGuid():N}"),
            JsonOptions);
    }

    public async Task InitializeAsync()
    {
        (_host, _client) = await new TestHostBuilder<CompositeCatalogItem, RestLibCompositeKey<Guid, string>>(
                _repository,
                "/api/catalog-items")
            .WithOptions(options => options.EnableHateoas = true)
            .WithServices(services =>
            {
                services.AddOpenApi();
                services.AddSingleton<IBatchRepository<CompositeCatalogItem, RestLibCompositeKey<Guid, string>>>(_repository);
            })
            .WithAdditionalEndpoints(endpoints => endpoints.MapOpenApi())
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.EnableBatch();
                config.UseCompositeKey(
                    entity => entity.TenantId,
                    "tenantId",
                    entity => entity.Sku,
                    "sku");
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
    public async Task GetById_WithCompositeRoute_ReturnsEntity()
    {
        // Arrange
        var entity = CreateItem(productName: "Widget A");
        Seed(entity);

        // Act
        var response = await _client.GetAsync(GetItemPath(entity));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await DeserializeItemAsync(response);
        body.Should().NotBeNull();
        body!.TenantId.Should().Be(entity.TenantId);
        body.Sku.Should().Be(entity.Sku);
        body.ProductName.Should().Be("Widget A");
    }

    [Fact]
    public async Task Create_WithCompositeKey_ReturnsLocationAndHateoasLinks()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var payload = new
        {
            tenant_id = tenantId,
            sku = "sku-created",
            product_name = "Created",
            price = 42.5m
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/catalog-items", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().EndWith($"/api/catalog-items/{tenantId}/sku-created");

        var json = await DeserializeJsonAsync(response);
        json.GetProperty("tenant_id").GetGuid().Should().Be(tenantId);
        json.GetProperty("sku").GetString().Should().Be("sku-created");

        var links = json.GetProperty("_links");
        links.GetProperty("self").GetProperty("href").GetString()
            .Should().EndWith($"/api/catalog-items/{tenantId}/sku-created");
        links.GetProperty("collection").GetProperty("href").GetString()
            .Should().EndWith("/api/catalog-items");
    }

    [Fact]
    public async Task Update_WithCompositeRouteAndBodyMissingKeyParts_UsesRouteKeyParts()
    {
        // Arrange
        var entity = CreateItem(productName: "Original", price: 10m);
        Seed(entity);

        var payload = new
        {
            product_name = "Updated",
            price = 99.9m
        };

        // Act
        var response = await _client.PutAsJsonAsync(GetItemPath(entity), payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await DeserializeItemAsync(response);
        body.Should().NotBeNull();
        body!.TenantId.Should().Be(entity.TenantId);
        body.Sku.Should().Be(entity.Sku);
        body.ProductName.Should().Be("Updated");
        body.Price.Should().Be(99.9m);
    }

    [Fact]
    public async Task GetById_WithCompositeKeyNotFound_UsesRouteParameterNamesInProblemDetails()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        const string Sku = "missing";

        // Act
        var response = await _client.GetAsync($"/api/catalog-items/{tenantId}/{Sku}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var problem = await DeserializeProblemAsync(response);
        problem.Should().NotBeNull();
        problem!.Type.Should().Be(ProblemTypes.NotFound);
        problem.Detail.Should().Contain("tenantId");
        problem.Detail.Should().Contain("sku");
        problem.Detail.Should().Contain(tenantId.ToString());
        problem.Detail.Should().Contain(Sku);
    }

    [Fact]
    public async Task BatchOperations_WithCompositeKeys_UseObjectIdsAndReturnSuccess()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var first = CreateItem(tenantId, "sku-update", "Update", 10m);
        var second = CreateItem(tenantId, "sku-patch", "Patch", 20m);
        var third = CreateItem(tenantId, "sku-delete", "Delete", 30m);
        Seed(first, second, third);

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
                        price = 77.7m
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
        var updateResponse = await _client.PostAsync("/api/catalog-items/batch", BatchJson(updatePayload));
        var patchResponse = await _client.PostAsync("/api/catalog-items/batch", BatchJson(patchPayload));
        var deleteResponse = await _client.PostAsync("/api/catalog-items/batch", BatchJson(deletePayload));

        // Assert
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        patchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var updateJson = await DeserializeJsonAsync(updateResponse);
        var patchJson = await DeserializeJsonAsync(patchResponse);
        var deleteJson = await DeserializeJsonAsync(deleteResponse);
        updateJson.GetProperty("items")[0].GetProperty("status").GetInt32().Should().Be(200);
        patchJson.GetProperty("items")[0].GetProperty("status").GetInt32().Should().Be(200);
        deleteJson.GetProperty("items")[0].GetProperty("status").GetInt32().Should().Be(204);

        var updated = await _repository.GetByIdAsync(new RestLibCompositeKey<Guid, string>(first.TenantId, first.Sku));
        var patched = await _repository.GetByIdAsync(new RestLibCompositeKey<Guid, string>(second.TenantId, second.Sku));
        var deleted = await _repository.GetByIdAsync(new RestLibCompositeKey<Guid, string>(third.TenantId, third.Sku));

        updated.Should().NotBeNull();
        updated!.ProductName.Should().Be("Updated Name");
        patched.Should().NotBeNull();
        patched!.ProductName.Should().Be("Patched Name");
        deleted.Should().BeNull();
    }

    [Fact]
    public async Task OpenApi_GetByIdWithCompositeKey_DocumentsEachPathParameter()
    {
        // Arrange

        // Act
        var openApi = await _client.GetStringAsync("/openapi/v1.json");
        var result = OpenApiDocument.Parse(openApi, "json");
        var document = result.Document!;
        var operation = document.Paths!["/api/catalog-items/{tenantId}/{sku}"]!.Operations![HttpMethod.Get]!;
        var tenantIdParameter = operation.Parameters!.Single(parameter => parameter.Name == "tenantId");
        var skuParameter = operation.Parameters.Single(parameter => parameter.Name == "sku");

        // Assert
        tenantIdParameter.In.Should().Be(ParameterLocation.Path);
        tenantIdParameter.Required.Should().BeTrue();
        tenantIdParameter.Schema!.Type.Should().Be(JsonSchemaType.String);
        tenantIdParameter.Schema.Format.Should().Be("uuid");
        skuParameter.In.Should().Be(ParameterLocation.Path);
        skuParameter.Required.Should().BeTrue();
        skuParameter.Schema!.Type.Should().Be(JsonSchemaType.String);
    }

    [Fact]
    public void UseCompositeKey_WithDuplicateRouteParameterNames_ThrowsArgumentException()
    {
        // Arrange
        var config = new RestLibEndpointConfiguration<CompositeCatalogItem, RestLibCompositeKey<Guid, string>>();

        // Act
        Action act = () => config.UseCompositeKey(
            entity => entity.TenantId,
            "id",
            entity => entity.Sku,
            "id");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*must be unique*");
    }

    private static StringContent BatchJson(object payload)
    {
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private static CompositeCatalogItem CreateItem(
        Guid? tenantId = null,
        string sku = "sku-1",
        string productName = "Widget",
        decimal price = 10m)
    {
        return new CompositeCatalogItem
        {
            TenantId = tenantId ?? Guid.NewGuid(),
            Sku = sku,
            ProductName = productName,
            Price = price
        };
    }

    private static string GetItemPath(CompositeCatalogItem entity)
    {
        return $"/api/catalog-items/{entity.TenantId}/{entity.Sku}";
    }

    private static async Task<CompositeCatalogItem?> DeserializeItemAsync(HttpResponseMessage response)
    {
        return JsonSerializer.Deserialize<CompositeCatalogItem>(
            await response.Content.ReadAsStringAsync(),
            JsonOptions);
    }

    private static async Task<JsonElement> DeserializeJsonAsync(HttpResponseMessage response)
    {
        return JsonSerializer.Deserialize<JsonElement>(
            await response.Content.ReadAsStringAsync(),
            JsonOptions);
    }

    private static async Task<RestLibProblemDetails?> DeserializeProblemAsync(HttpResponseMessage response)
    {
        return JsonSerializer.Deserialize<RestLibProblemDetails>(
            await response.Content.ReadAsStringAsync(),
            JsonOptions);
    }

    private void Seed(params CompositeCatalogItem[] items)
    {
        _repository.Clear();
        _repository.Seed(items);
    }
}

internal sealed class CompositeCatalogItem
{
    [Required]
    public Guid TenantId { get; set; }

    [Required]
    [StringLength(64)]
    public string Sku { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string ProductName { get; set; } = string.Empty;

    [Range(0, double.MaxValue)]
    public decimal Price { get; set; }
}
