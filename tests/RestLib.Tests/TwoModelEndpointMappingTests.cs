using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestLib.Abstractions;
using RestLib.Configuration;
using RestLib.Hypermedia;
using RestLib.InMemory;
using RestLib.Pagination;
using RestLib.Responses;
using RestLib.Tests.Fakes;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Integration tests for fluent two-model endpoint mapping.
/// </summary>
[Trait("Type", "Integration")]
[Trait("Feature", "Mapping")]
public class TwoModelEndpointMappingTests
{
    [Fact]
    public async Task MapRestLib_WithTwoModelResource_UsesDbRepositoryAndReturnsApiModel()
    {
        // Arrange
        var repository = new TrackingTwoModelRepository();
        var id = Guid.NewGuid();
        repository.Seed(new TwoModelDbItem
        {
            Id = id,
            Name = "Widget",
            Price = 12.5m,
            Category = "hardware",
            IsActive = true,
            InternalToken = "secret-db-only"
        });

        var (host, client) = await CreateHostAsync(repository);
        using var hostHandle = host;
        using var clientHandle = client;

        // Act
        var response = await client.GetAsync($"/api/items/{id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        repository.GetByIdCallCount.Should().Be(1);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("name").GetString().Should().Be("Widget");
        json.GetProperty("price").GetDecimal().Should().Be(12.5m);
        json.GetProperty("category").GetString().Should().Be("hardware");
        json.TryGetProperty("internal_token", out _).Should().BeFalse();
    }

    [Fact]
    public async Task MapRestLib_WithTwoModelResource_CreateMapsApiToDbBeforePersistence()
    {
        // Arrange
        var repository = new TrackingTwoModelRepository();
        var (host, client) = await CreateHostAsync(repository);
        using var hostHandle = host;
        using var clientHandle = client;

        // Act
        var response = await client.PostAsJsonAsync(
            "/api/items",
            new { name = "Created", price = 15.0m, category = "hardware", is_active = true });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        repository.CreateCallCount.Should().Be(1);
        repository.LastCreatedEntity.Should().NotBeNull();
        repository.LastCreatedEntity!.Name.Should().Be("Created");
        repository.LastCreatedEntity.InternalToken.Should().Be("mapped-from-api");

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("name").GetString().Should().Be("Created");
        json.TryGetProperty("internal_token", out _).Should().BeFalse();
    }

    [Fact]
    public async Task MapRestLib_WithTwoModelResource_FilteringSortingAndFieldSelectionUseApiModel()
    {
        // Arrange
        var repository = new TrackingTwoModelRepository();
        repository.Seed(
            new TwoModelDbItem
            {
                Id = Guid.NewGuid(),
                Name = "Budget Hammer",
                Price = 10m,
                Category = "hardware",
                IsActive = true,
                InternalToken = "db-1"
            },
            new TwoModelDbItem
            {
                Id = Guid.NewGuid(),
                Name = "Premium Hammer",
                Price = 20m,
                Category = "hardware",
                IsActive = true,
                InternalToken = "db-2"
            },
            new TwoModelDbItem
            {
                Id = Guid.NewGuid(),
                Name = "Garden Hose",
                Price = 30m,
                Category = "garden",
                IsActive = true,
                InternalToken = "db-3"
            });

        var (host, client) = await CreateHostAsync(repository, config =>
        {
            config.AllowFiltering(item => item.Category);
            config.AllowSorting(item => item.Price);
            config.AllowFieldSelection(item => item.Id, item => item.Name, item => item.Price, item => item.Category);
        });
        using var hostHandle = host;
        using var clientHandle = client;

        // Act
        var response = await client.GetAsync("/api/items?category=hardware&sort=price:desc&fields=name,price");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        repository.GetAllCallCount.Should().Be(1);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(2);
        items[0].GetProperty("name").GetString().Should().Be("Premium Hammer");
        items[1].GetProperty("name").GetString().Should().Be("Budget Hammer");
        items[0].TryGetProperty("category", out _).Should().BeFalse();
        items[0].TryGetProperty("internal_token", out _).Should().BeFalse();
    }

    [Fact]
    public async Task MapRestLib_WithTwoModelResource_ValidationUsesApiModel()
    {
        // Arrange
        var repository = new TrackingTwoModelRepository();
        var (host, client) = await CreateHostAsync(repository);
        using var hostHandle = host;
        using var clientHandle = client;

        // Act
        var response = await client.PostAsJsonAsync(
            "/api/items",
            new { name = string.Empty, price = 0m, category = string.Empty, is_active = true });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        repository.CreateCallCount.Should().Be(0);

        var json = await response.ShouldBeProblemDetailsJson(
            HttpStatusCode.BadRequest,
            "/problems/validation-failed");
        json.GetProperty("errors").EnumerateObject().Should().NotBeEmpty();
    }

    [Fact]
    public async Task MapRestLib_WithTwoModelResource_ApiHooksRunByDefault()
    {
        // Arrange
        var repository = new TrackingTwoModelRepository();
        object? capturedEntity = null;

        var (host, client) = await CreateHostAsync(repository, config =>
        {
            config.UseHooks(hooks =>
            {
                hooks.BeforePersist = ctx =>
                {
                    capturedEntity = ctx.Entity;
                    ctx.Entity!.Name = "API Hooked";
                    return Task.CompletedTask;
                };
            });
        });
        using var hostHandle = host;
        using var clientHandle = client;

        // Act
        var response = await client.PostAsJsonAsync(
            "/api/items",
            new { name = "Original", price = 5m, category = "hardware", is_active = true });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        capturedEntity.Should().BeOfType<TwoModelApiItem>();
        repository.LastCreatedEntity.Should().NotBeNull();
        repository.LastCreatedEntity!.Name.Should().Be("API Hooked");
    }

    [Fact]
    public async Task MapRestLib_WithTwoModelResource_UseDbModelHooksRunsDbHooks()
    {
        // Arrange
        var repository = new TrackingTwoModelRepository();
        object? capturedEntity = null;

        var (host, client) = await CreateHostAsync(repository, config =>
        {
            config.UseDbModelHooks(hooks =>
            {
                hooks.BeforePersist = ctx =>
                {
                    capturedEntity = ctx.Entity;
                    ctx.Entity!.Name = "DB Hooked";
                    ctx.Entity.InternalToken = "db-hooked";
                    return Task.CompletedTask;
                };
            });
        });
        using var hostHandle = host;
        using var clientHandle = client;

        // Act
        var response = await client.PostAsJsonAsync(
            "/api/items",
            new { name = "Original", price = 5m, category = "hardware", is_active = true });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        capturedEntity.Should().BeOfType<TwoModelDbItem>();
        repository.LastCreatedEntity.Should().NotBeNull();
        repository.LastCreatedEntity!.Name.Should().Be("DB Hooked");
        repository.LastCreatedEntity.InternalToken.Should().Be("db-hooked");
    }

    [Fact]
    public async Task MapRestLib_WithTwoModelResource_ETagUsesApiModel()
    {
        // Arrange
        var repository = new TrackingTwoModelRepository();
        var id = Guid.NewGuid();
        repository.Seed(new TwoModelDbItem
        {
            Id = id,
            Name = "Widget",
            Price = 12.5m,
            Category = "hardware",
            IsActive = true,
            InternalToken = "v1"
        });

        var (host, client) = await CreateHostAsync(repository, configureOptions: options => options.EnableETagSupport = true);
        using var hostHandle = host;
        using var clientHandle = client;

        // Act
        var firstResponse = await client.GetAsync($"/api/items/{id}");
        var firstETag = firstResponse.Headers.ETag!.Tag;

        repository.Upsert(new TwoModelDbItem
        {
            Id = id,
            Name = "Widget",
            Price = 12.5m,
            Category = "hardware",
            IsActive = true,
            InternalToken = "v2"
        });

        var secondResponse = await client.GetAsync($"/api/items/{id}");
        var secondETag = secondResponse.Headers.ETag!.Tag;

        // Assert
        firstETag.Should().Be(secondETag);
    }

    [Fact]
    public async Task MapRestLib_WithTwoModelResource_PatchWithNonObjectDocument_ReturnsBadRequest()
    {
        // Arrange
        var repository = new TrackingTwoModelRepository();
        var id = Guid.NewGuid();
        repository.Seed(new TwoModelDbItem
        {
            Id = id,
            Name = "Widget",
            Price = 12.5m,
            Category = "hardware",
            IsActive = true,
            InternalToken = "secret-db-only"
        });

        var (host, client) = await CreateHostAsync(repository);
        using var hostHandle = host;
        using var clientHandle = client;
        using var patch = new StringContent("[]", Encoding.UTF8, "application/merge-patch+json");

        // Act
        var response = await client.PatchAsync($"/api/items/{id}", patch);

        // Assert
        var json = await response.ShouldBeProblemDetailsJson(
            HttpStatusCode.BadRequest,
            ProblemTypes.BadRequest);
        json.GetProperty("detail").GetString().Should().Contain("could not be applied");
        repository.UpdateCallCount.Should().Be(0);
        repository.PatchCallCount.Should().Be(0);
    }

    [Fact]
    public async Task MapRestLib_WithTwoModelResource_HateoasUsesApiModel()
    {
        // Arrange
        var repository = new TrackingTwoModelRepository();
        var id = Guid.NewGuid();
        repository.Seed(new TwoModelDbItem
        {
            Id = id,
            Name = "Widget",
            Price = 12.5m,
            Category = "hardware",
            IsActive = true,
            InternalToken = "secret-db-only"
        });

        var provider = new CapturingApiLinkProvider();
        var (host, client) = await CreateHostAsync(
            repository,
            configureOptions: options => options.EnableHateoas = true,
            configureServices: services =>
            {
                services.AddSingleton(provider);
                services.AddSingleton<IHateoasLinkProvider<TwoModelApiItem, Guid>>(provider);
            });
        using var hostHandle = host;
        using var clientHandle = client;

        // Act
        var response = await client.GetAsync($"/api/items/{id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        provider.LastEntity.Should().NotBeNull();
        provider.LastEntity!.Name.Should().Be("Widget");

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("_links").TryGetProperty("profile", out _).Should().BeTrue();
        json.TryGetProperty("internal_token", out _).Should().BeFalse();
    }

    [Fact]
    public async Task MapRestLib_WithoutMapperRegistration_ThrowsClearStartupOrRequestException()
    {
        // Arrange
        var repository = new TrackingTwoModelRepository();
        repository.Seed(new TwoModelDbItem
        {
            Id = Guid.NewGuid(),
            Name = "Widget",
            Price = 12.5m,
            Category = "hardware",
            IsActive = true,
            InternalToken = "secret-db-only"
        });

        var (host, client) = await new TestTwoModelHostBuilder<TwoModelApiItem, TwoModelDbItem, Guid>(repository, "/api/items")
            .WithEndpoint(config => config.AllowAnonymous())
            .BuildAsync();
        using var hostHandle = host;
        using var clientHandle = client;

        // Act
        var act = async () => await client.GetAsync("/api/items");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*TwoModelApiItem*")
            .WithMessage("*TwoModelDbItem*")
            .WithMessage("*AddRestLibMapper*");
    }

    [Fact]
    public async Task MapRestLib_WithUnsupportedMappedSortProperty_ThrowsBeforeRepositoryCall()
    {
        // Arrange
        var repository = new TrackingTwoModelRepository();

        // Act
        var act = async () => await new TestTwoModelHostBuilder<InvalidSortApiItem, TwoModelDbItem, Guid>(repository, "/api/items")
            .WithServices(services =>
                services.AddRestLibMapper<InvalidSortApiItem, TwoModelDbItem>(
                    _ => new InvalidSortMapper()))
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.AllowSorting(item => item.DisplayOrder);
            })
            .BuildAsync();

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*InvalidSortApiItem*")
            .WithMessage("*TwoModelDbItem*")
            .WithMessage("*DisplayOrder*");
        repository.GetAllCallCount.Should().Be(0);
    }

    [Fact]
    public async Task MapRestLib_SingleModelResource_StillSupportsExistingCrud()
    {
        // Arrange
        var repository = new TestEntityRepository();
        var (host, client) = await new TestHostBuilder<TestEntity, Guid>(repository, "/api/items")
            .WithEndpoint(config => config.AllowAnonymous())
            .BuildAsync();
        using var _ = host;
        using var __ = client;

        // Act
        var createResponse = await client.PostAsJsonAsync(
            "/api/items",
            new { name = "Created", price = 9.5m });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString();
        var getResponse = await client.GetAsync($"/api/items/{id}");

        // Assert
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("name").GetString().Should().Be("Created");
    }

    private static async Task<(Microsoft.Extensions.Hosting.IHost Host, HttpClient Client)> CreateHostAsync(
        TrackingTwoModelRepository repository,
        Action<RestLibEndpointConfiguration<TwoModelApiItem, TwoModelDbItem, Guid>>? configureEndpoint = null,
        Action<RestLibOptions>? configureOptions = null,
        Action<IServiceCollection>? configureServices = null)
    {
        return await new TestTwoModelHostBuilder<TwoModelApiItem, TwoModelDbItem, Guid>(repository, "/api/items")
            .WithOptions(configureOptions ?? (_ => { }))
            .WithServices(services =>
            {
                services.AddRestLibMapper<TwoModelApiItem, TwoModelDbItem>(
                    _ => new TwoModelMapper());
                configureServices?.Invoke(services);
            })
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                configureEndpoint?.Invoke(config);
            })
            .BuildAsync();
    }

    private sealed class TwoModelApiItem
    {
        public Guid Id { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty;

        [Range(typeof(decimal), "0.01", "79228162514264337593543950335")]
        public decimal Price { get; set; }

        [Required]
        public string Category { get; set; } = string.Empty;

        public bool IsActive { get; set; }
    }

    private sealed class TwoModelDbItem
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public decimal Price { get; set; }

        public string Category { get; set; } = string.Empty;

        public bool IsActive { get; set; }

        public string InternalToken { get; set; } = string.Empty;
    }

    private sealed class TwoModelMapper : IRestLibMapper<TwoModelApiItem, TwoModelDbItem>
    {
        public TwoModelApiItem ToApi(TwoModelDbItem dbModel)
        {
            return new TwoModelApiItem
            {
                Id = dbModel.Id,
                Name = dbModel.Name,
                Price = dbModel.Price,
                Category = dbModel.Category,
                IsActive = dbModel.IsActive
            };
        }

        public TwoModelDbItem ToDb(TwoModelApiItem apiModel)
        {
            return new TwoModelDbItem
            {
                Id = apiModel.Id,
                Name = apiModel.Name,
                Price = apiModel.Price,
                Category = apiModel.Category,
                IsActive = apiModel.IsActive,
                InternalToken = "mapped-from-api"
            };
        }
    }

    private sealed class InvalidSortApiItem
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public int DisplayOrder { get; set; }
    }

    private sealed class InvalidSortMapper : IRestLibMapper<InvalidSortApiItem, TwoModelDbItem>
    {
        public InvalidSortApiItem ToApi(TwoModelDbItem dbModel)
        {
            return new InvalidSortApiItem
            {
                Id = dbModel.Id,
                Name = dbModel.Name,
                DisplayOrder = 0
            };
        }

        public TwoModelDbItem ToDb(InvalidSortApiItem apiModel)
        {
            return new TwoModelDbItem
            {
                Id = apiModel.Id,
                Name = apiModel.Name,
                Category = "hardware",
                InternalToken = "mapped-from-api"
            };
        }
    }

    private sealed class CapturingApiLinkProvider : IHateoasLinkProvider<TwoModelApiItem, Guid>
    {
        public TwoModelApiItem? LastEntity { get; private set; }

        public IReadOnlyDictionary<string, HateoasLink>? GetLinks(TwoModelApiItem entity, Guid key)
        {
            LastEntity = entity;
            return new Dictionary<string, HateoasLink>
            {
                ["profile"] = new HateoasLink { Href = $"/api/profiles/{key}" }
            };
        }
    }

    private sealed class TrackingTwoModelRepository : IRepository<TwoModelDbItem, Guid>, ICountableRepository<TwoModelDbItem, Guid>
    {
        private readonly InMemoryRepository<TwoModelDbItem, Guid> _inner =
            new(item => item.Id, Guid.NewGuid);

        public int CreateCallCount { get; private set; }

        public int GetAllCallCount { get; private set; }

        public int GetByIdCallCount { get; private set; }

        public int PatchCallCount { get; private set; }

        public int UpdateCallCount { get; private set; }

        public TwoModelDbItem? LastCreatedEntity { get; private set; }

        public Task<long> CountAsync(IReadOnlyList<RestLib.Filtering.FilterValue> filters, CancellationToken ct = default)
        {
            return ((ICountableRepository<TwoModelDbItem, Guid>)_inner).CountAsync(filters, ct);
        }

        public async Task<TwoModelDbItem> CreateAsync(TwoModelDbItem entity, CancellationToken ct = default)
        {
            CreateCallCount++;
            LastCreatedEntity = entity;
            return await _inner.CreateAsync(entity, ct);
        }

        public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
        {
            return _inner.DeleteAsync(id, ct);
        }

        public async Task<PagedResult<TwoModelDbItem>> GetAllAsync(PaginationRequest pagination, CancellationToken ct = default)
        {
            GetAllCallCount++;
            return await _inner.GetAllAsync(pagination, ct);
        }

        public async Task<TwoModelDbItem?> GetByIdAsync(Guid id, CancellationToken ct = default)
        {
            GetByIdCallCount++;
            return await _inner.GetByIdAsync(id, ct);
        }

        public async Task<TwoModelDbItem?> PatchAsync(Guid id, JsonElement patchDocument, CancellationToken ct = default)
        {
            PatchCallCount++;
            return await _inner.PatchAsync(id, patchDocument, ct);
        }

        public void Seed(params TwoModelDbItem[] entities)
        {
            _inner.Seed(entities);
        }

        public void Upsert(TwoModelDbItem entity)
        {
            _inner.Seed([entity]);
        }

        public async Task<TwoModelDbItem?> UpdateAsync(Guid id, TwoModelDbItem entity, CancellationToken ct = default)
        {
            UpdateCallCount++;
            return await _inner.UpdateAsync(id, entity, ct);
        }
    }
}
