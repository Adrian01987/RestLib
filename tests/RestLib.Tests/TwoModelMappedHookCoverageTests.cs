using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestLib.Abstractions;
using RestLib.Configuration;
using RestLib.InMemory;
using RestLib.Pagination;
using RestLib.Tests.Fakes;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Coverage-focused integration tests for mapped (two-model) Update, Delete, Patch, GetAll, and
/// GetById handler paths. Existing two-model tests cover Create, Get, basic API hooks, and ETag,
/// but the mapped Update/Delete/Patch state machines and the DB-model hook branches in those
/// state machines were largely untouched, leaving the CI coverage gate below the 80% threshold.
/// These tests intentionally exercise both the API-model and DB-model hook branches across the
/// full set of mapped CRUD endpoints, including not-found, ETag, validation, and HATEOAS paths.
/// </summary>
[Trait("Type", "Integration")]
[Trait("Feature", "Mapping")]
[Trait("Category", "Coverage")]
public class TwoModelMappedHookCoverageTests
{
    [Fact]
    public async Task PutMappedEndpoint_WithApiHooks_RunsApiHookPipelineAndPersistsMappedDbEntity()
    {
        // Arrange
        var repository = new TrackingRepo();
        var existingId = Guid.NewGuid();
        repository.Seed(new DbItem
        {
            Id = existingId,
            Name = "Original",
            Price = 1m,
            Category = "hardware",
            IsActive = true,
            InternalToken = "db-original",
        });

        var beforePersistEntities = new List<object?>();
        var afterPersistEntities = new List<object?>();

        var (host, client) = await CreateHostAsync(repository, config =>
        {
            config.UseHooks(hooks =>
            {
                hooks.BeforePersist = ctx =>
                {
                    beforePersistEntities.Add(ctx.Entity);
                    ctx.Entity!.Name = "ApiHookedBeforePersist";
                    return Task.CompletedTask;
                };
                hooks.AfterPersist = ctx =>
                {
                    afterPersistEntities.Add(ctx.Entity);
                    return Task.CompletedTask;
                };
            });
        });
        using var hostHandle = host;
        using var clientHandle = client;

        // Act
        var response = await client.PutAsJsonAsync(
            $"/api/items/{existingId}",
            new { name = "FromClient", price = 9m, category = "hardware", is_active = true });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        beforePersistEntities.Should().ContainSingle().Which.Should().BeOfType<ApiItem>();
        afterPersistEntities.Should().ContainSingle().Which.Should().BeOfType<ApiItem>();
        repository.UpdateCallCount.Should().Be(1);
        repository.LastUpdatedEntity!.Name.Should().Be("ApiHookedBeforePersist");

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("name").GetString().Should().Be("ApiHookedBeforePersist");
        json.TryGetProperty("internal_token", out _).Should().BeFalse();
    }

    [Fact]
    public async Task PutMappedEndpoint_WithDbModelHooks_RunsDbHookPipelineAndPreservesDbOnlyFields()
    {
        // Arrange
        var repository = new TrackingRepo();
        var existingId = Guid.NewGuid();
        repository.Seed(new DbItem
        {
            Id = existingId,
            Name = "Original",
            Price = 1m,
            Category = "hardware",
            IsActive = true,
            InternalToken = "db-original",
        });

        var beforePersistEntities = new List<object?>();

        var (host, client) = await CreateHostAsync(repository, config =>
        {
            config.UseDbModelHooks(hooks =>
            {
                hooks.BeforePersist = ctx =>
                {
                    beforePersistEntities.Add(ctx.Entity);
                    ctx.Entity!.InternalToken = "db-token-after-hook";
                    return Task.CompletedTask;
                };
            });
        });
        using var hostHandle = host;
        using var clientHandle = client;

        // Act
        var response = await client.PutAsJsonAsync(
            $"/api/items/{existingId}",
            new { name = "FromClient", price = 9m, category = "hardware", is_active = true });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        beforePersistEntities.Should().ContainSingle().Which.Should().BeOfType<DbItem>();
        repository.LastUpdatedEntity!.InternalToken.Should().Be("db-token-after-hook");
    }

    [Fact]
    public async Task PutMappedEndpoint_WithApiHooks_AndMissingId_ReturnsNotFound()
    {
        // Arrange
        var repository = new TrackingRepo();

        var (host, client) = await CreateHostAsync(repository, config =>
        {
            config.UseHooks(hooks =>
            {
                hooks.BeforePersist = _ => Task.CompletedTask;
            });
        });
        using var hostHandle = host;
        using var clientHandle = client;

        // Act
        var missingId = Guid.NewGuid();
        var response = await client.PutAsJsonAsync(
            $"/api/items/{missingId}",
            new { name = "X", price = 1m, category = "hardware", is_active = true });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PutMappedEndpoint_WithApiHooks_AndETagSupport_SetsETagHeaderAndAcceptsMatchingPrecondition()
    {
        // Arrange
        var repository = new TrackingRepo();
        var existingId = Guid.NewGuid();
        repository.Seed(new DbItem
        {
            Id = existingId,
            Name = "Original",
            Price = 1m,
            Category = "hardware",
            IsActive = true,
            InternalToken = "db-original",
        });

        var (host, client) = await CreateHostAsync(
            repository,
            config => config.UseHooks(hooks => hooks.BeforePersist = _ => Task.CompletedTask),
            options =>
            {
                options.EnableETagSupport = true;
                options.EnableHateoas = true;
            });
        using var hostHandle = host;
        using var clientHandle = client;

        // First fetch to retrieve the current ETag.
        var get = await client.GetAsync($"/api/items/{existingId}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        var etag = get.Headers.ETag!.Tag;

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/items/{existingId}")
        {
            Content = JsonContent.Create(new
            {
                name = "Updated",
                price = 2m,
                category = "hardware",
                is_active = true,
            }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);

        // Act
        var response = await client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.ETag.Should().NotBeNull();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("name").GetString().Should().Be("Updated");
        json.TryGetProperty("_links", out _).Should().BeTrue();
    }

    [Fact]
    public async Task DeleteMappedEndpoint_WithApiHooks_RunsApiHookPipelineAndDeletes()
    {
        // Arrange
        var repository = new TrackingRepo();
        var existingId = Guid.NewGuid();
        repository.Seed(new DbItem
        {
            Id = existingId,
            Name = "ToDelete",
            Price = 1m,
            Category = "hardware",
            IsActive = true,
            InternalToken = "db-token",
        });

        var beforePersistEntities = new List<object?>();

        var (host, client) = await CreateHostAsync(repository, config =>
        {
            config.UseHooks(hooks =>
            {
                hooks.BeforePersist = ctx =>
                {
                    beforePersistEntities.Add(ctx.Entity);
                    return Task.CompletedTask;
                };
            });
        });
        using var hostHandle = host;
        using var clientHandle = client;

        // Act
        var response = await client.DeleteAsync($"/api/items/{existingId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        beforePersistEntities.Should().ContainSingle().Which.Should().BeOfType<ApiItem>();
        var follow = await client.GetAsync($"/api/items/{existingId}");
        follow.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteMappedEndpoint_WithDbModelHooks_RunsDbHookPipelineAndDeletes()
    {
        // Arrange
        var repository = new TrackingRepo();
        var existingId = Guid.NewGuid();
        repository.Seed(new DbItem
        {
            Id = existingId,
            Name = "ToDelete",
            Price = 1m,
            Category = "hardware",
            IsActive = true,
            InternalToken = "db-token",
        });

        var beforePersistEntities = new List<object?>();

        var (host, client) = await CreateHostAsync(repository, config =>
        {
            config.UseDbModelHooks(hooks =>
            {
                hooks.BeforePersist = ctx =>
                {
                    beforePersistEntities.Add(ctx.Entity);
                    return Task.CompletedTask;
                };
            });
        });
        using var hostHandle = host;
        using var clientHandle = client;

        // Act
        var response = await client.DeleteAsync($"/api/items/{existingId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        beforePersistEntities.Should().ContainSingle().Which.Should().BeOfType<DbItem>();
    }

    [Fact]
    public async Task DeleteMappedEndpoint_WithApiHooks_AndMissingId_ReturnsNotFound()
    {
        // Arrange
        var repository = new TrackingRepo();

        var (host, client) = await CreateHostAsync(repository, config =>
        {
            config.UseHooks(hooks =>
            {
                hooks.BeforePersist = _ => Task.CompletedTask;
            });
        });
        using var hostHandle = host;
        using var clientHandle = client;

        // Act
        var response = await client.DeleteAsync($"/api/items/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PatchMappedEndpoint_WithApiHooks_AppliesPatchAndRunsApiHookPipeline()
    {
        // Arrange
        var repository = new TrackingRepo();
        var existingId = Guid.NewGuid();
        repository.Seed(new DbItem
        {
            Id = existingId,
            Name = "Original",
            Price = 1m,
            Category = "hardware",
            IsActive = true,
            InternalToken = "db-token",
        });

        var beforePersistEntities = new List<object?>();

        var (host, client) = await CreateHostAsync(repository, config =>
        {
            config.UseHooks(hooks =>
            {
                hooks.BeforePersist = ctx =>
                {
                    beforePersistEntities.Add(ctx.Entity);
                    return Task.CompletedTask;
                };
            });
        });
        using var hostHandle = host;
        using var clientHandle = client;

        var patch = new StringContent(
            "{\"name\":\"Patched\"}",
            Encoding.UTF8,
            "application/merge-patch+json");

        var request = new HttpRequestMessage(new HttpMethod("PATCH"), $"/api/items/{existingId}")
        {
            Content = patch,
        };

        // Act
        var response = await client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        beforePersistEntities.Should().ContainSingle().Which.Should().BeOfType<ApiItem>();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("name").GetString().Should().Be("Patched");
    }

    [Fact]
    public async Task PatchMappedEndpoint_WithDbModelHooks_AppliesPatchAndRunsDbHookPipeline()
    {
        // Arrange
        var repository = new TrackingRepo();
        var existingId = Guid.NewGuid();
        repository.Seed(new DbItem
        {
            Id = existingId,
            Name = "Original",
            Price = 1m,
            Category = "hardware",
            IsActive = true,
            InternalToken = "db-token",
        });

        var beforePersistEntities = new List<object?>();

        var (host, client) = await CreateHostAsync(repository, config =>
        {
            config.UseDbModelHooks(hooks =>
            {
                hooks.BeforePersist = ctx =>
                {
                    beforePersistEntities.Add(ctx.Entity);
                    ctx.Entity!.InternalToken = "db-after-patch-hook";
                    return Task.CompletedTask;
                };
            });
        });
        using var hostHandle = host;
        using var clientHandle = client;

        var patch = new StringContent(
            "{\"name\":\"Patched\"}",
            Encoding.UTF8,
            "application/merge-patch+json");

        var request = new HttpRequestMessage(new HttpMethod("PATCH"), $"/api/items/{existingId}")
        {
            Content = patch,
        };

        // Act
        var response = await client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        beforePersistEntities.Should().ContainSingle().Which.Should().BeOfType<DbItem>();
    }

    [Fact]
    public async Task PatchMappedEndpoint_WithApiHooks_AndMissingId_ReturnsNotFound()
    {
        // Arrange
        var repository = new TrackingRepo();

        var (host, client) = await CreateHostAsync(repository, config =>
        {
            config.UseHooks(hooks =>
            {
                hooks.BeforePersist = _ => Task.CompletedTask;
            });
        });
        using var hostHandle = host;
        using var clientHandle = client;

        var patch = new StringContent(
            "{\"name\":\"X\"}",
            Encoding.UTF8,
            "application/merge-patch+json");

        var request = new HttpRequestMessage(new HttpMethod("PATCH"), $"/api/items/{Guid.NewGuid()}")
        {
            Content = patch,
        };

        // Act
        var response = await client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetAllMappedEndpoint_WithApiHooks_RunsApiHookPipelineAndReturnsApiCollection()
    {
        // Arrange
        var repository = new TrackingRepo();
        repository.Seed(
            new DbItem { Id = Guid.NewGuid(), Name = "A", Price = 1m, Category = "hardware", IsActive = true, InternalToken = "t1" },
            new DbItem { Id = Guid.NewGuid(), Name = "B", Price = 2m, Category = "hardware", IsActive = true, InternalToken = "t2" });

        var hookCalls = 0;

        var (host, client) = await CreateHostAsync(repository, config =>
        {
            config.UseHooks(hooks =>
            {
                hooks.BeforeResponse = _ =>
                {
                    Interlocked.Increment(ref hookCalls);
                    return Task.CompletedTask;
                };
            });
        });
        using var hostHandle = host;
        using var clientHandle = client;

        // Act
        var response = await client.GetAsync("/api/items");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        hookCalls.Should().BeGreaterThan(0);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("items").GetArrayLength().Should().Be(2);
        json.GetProperty("items")[0].TryGetProperty("internal_token", out _).Should().BeFalse();
    }

    [Fact]
    public async Task GetAllMappedEndpoint_WithDbModelHooks_RunsDbHookPipelineAndReturnsApiCollection()
    {
        // Arrange
        var repository = new TrackingRepo();
        repository.Seed(
            new DbItem { Id = Guid.NewGuid(), Name = "A", Price = 1m, Category = "hardware", IsActive = true, InternalToken = "t1" });

        var hookCalls = 0;

        var (host, client) = await CreateHostAsync(repository, config =>
        {
            config.UseDbModelHooks(hooks =>
            {
                hooks.BeforeResponse = _ =>
                {
                    Interlocked.Increment(ref hookCalls);
                    return Task.CompletedTask;
                };
            });
        });
        using var hostHandle = host;
        using var clientHandle = client;

        // Act
        var response = await client.GetAsync("/api/items");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        hookCalls.Should().BeGreaterThan(0);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("items").GetArrayLength().Should().Be(1);
        json.GetProperty("items")[0].TryGetProperty("internal_token", out _).Should().BeFalse();
    }

    [Fact]
    public async Task GetByIdMappedEndpoint_WithApiHooks_RunsApiHookPipelineAndReturnsApiModel()
    {
        // Arrange
        var repository = new TrackingRepo();
        var existingId = Guid.NewGuid();
        repository.Seed(new DbItem
        {
            Id = existingId,
            Name = "Item",
            Price = 1m,
            Category = "hardware",
            IsActive = true,
            InternalToken = "t1",
        });

        object? capturedHookEntity = null;

        var (host, client) = await CreateHostAsync(repository, config =>
        {
            config.UseHooks(hooks =>
            {
                hooks.BeforeResponse = ctx =>
                {
                    capturedHookEntity = ctx.Entity;
                    return Task.CompletedTask;
                };
            });
        });
        using var hostHandle = host;
        using var clientHandle = client;

        // Act
        var response = await client.GetAsync($"/api/items/{existingId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        capturedHookEntity.Should().BeOfType<ApiItem>();
    }

    [Fact]
    public async Task GetByIdMappedEndpoint_WithDbModelHooks_RunsDbHookPipelineAndReturnsApiModel()
    {
        // Arrange
        var repository = new TrackingRepo();
        var existingId = Guid.NewGuid();
        repository.Seed(new DbItem
        {
            Id = existingId,
            Name = "Item",
            Price = 1m,
            Category = "hardware",
            IsActive = true,
            InternalToken = "t1",
        });

        object? capturedHookEntity = null;

        var (host, client) = await CreateHostAsync(repository, config =>
        {
            config.UseDbModelHooks(hooks =>
            {
                hooks.BeforeResponse = ctx =>
                {
                    capturedHookEntity = ctx.Entity;
                    return Task.CompletedTask;
                };
            });
        });
        using var hostHandle = host;
        using var clientHandle = client;

        // Act
        var response = await client.GetAsync($"/api/items/{existingId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        capturedHookEntity.Should().BeOfType<DbItem>();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.TryGetProperty("internal_token", out _).Should().BeFalse();
    }

    private static async Task<(Microsoft.Extensions.Hosting.IHost Host, HttpClient Client)> CreateHostAsync(
        TrackingRepo repository,
        Action<RestLibEndpointConfiguration<ApiItem, DbItem, Guid>>? configureEndpoint = null,
        Action<RestLibOptions>? configureOptions = null)
    {
        return await new TestTwoModelHostBuilder<ApiItem, DbItem, Guid>(repository, "/api/items")
            .WithOptions(configureOptions ?? (_ => { }))
            .WithServices(services =>
            {
                services.AddRestLibMapper<ApiItem, DbItem>(_ => new Mapper());
            })
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                configureEndpoint?.Invoke(config);
            })
            .BuildAsync();
    }

    private sealed class ApiItem
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public decimal Price { get; set; }

        public string Category { get; set; } = string.Empty;

        public bool IsActive { get; set; }
    }

    private sealed class DbItem
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public decimal Price { get; set; }

        public string Category { get; set; } = string.Empty;

        public bool IsActive { get; set; }

        public string InternalToken { get; set; } = string.Empty;
    }

    private sealed class Mapper : IRestLibMapper<ApiItem, DbItem>
    {
        public ApiItem ToApi(DbItem dbModel)
        {
            return new ApiItem
            {
                Id = dbModel.Id,
                Name = dbModel.Name,
                Price = dbModel.Price,
                Category = dbModel.Category,
                IsActive = dbModel.IsActive,
            };
        }

        public DbItem ToDb(ApiItem apiModel)
        {
            return new DbItem
            {
                Id = apiModel.Id,
                Name = apiModel.Name,
                Price = apiModel.Price,
                Category = apiModel.Category,
                IsActive = apiModel.IsActive,
                InternalToken = "mapped-from-api",
            };
        }
    }

    private sealed class TrackingRepo : IRepository<DbItem, Guid>, ICountableRepository<DbItem, Guid>
    {
        private readonly InMemoryRepository<DbItem, Guid> _inner =
            new(item => item.Id, Guid.NewGuid);

        public int UpdateCallCount { get; private set; }

        public DbItem? LastUpdatedEntity { get; private set; }

        public Task<long> CountAsync(IReadOnlyList<RestLib.Filtering.FilterValue> filters, CancellationToken ct = default)
        {
            return ((ICountableRepository<DbItem, Guid>)_inner).CountAsync(filters, ct);
        }

        public Task<DbItem> CreateAsync(DbItem entity, CancellationToken ct = default)
        {
            return _inner.CreateAsync(entity, ct);
        }

        public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
        {
            return _inner.DeleteAsync(id, ct);
        }

        public Task<PagedResult<DbItem>> GetAllAsync(PaginationRequest pagination, CancellationToken ct = default)
        {
            return _inner.GetAllAsync(pagination, ct);
        }

        public Task<DbItem?> GetByIdAsync(Guid id, CancellationToken ct = default)
        {
            return _inner.GetByIdAsync(id, ct);
        }

        public Task<DbItem?> PatchAsync(Guid id, JsonElement patchDocument, CancellationToken ct = default)
        {
            return _inner.PatchAsync(id, patchDocument, ct);
        }

        public void Seed(params DbItem[] entities)
        {
            _inner.Seed(entities);
        }

        public async Task<DbItem?> UpdateAsync(Guid id, DbItem entity, CancellationToken ct = default)
        {
            UpdateCallCount++;
            LastUpdatedEntity = entity;
            return await _inner.UpdateAsync(id, entity, ct);
        }
    }
}
