using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RestLib.Abstractions;
using RestLib.Batch;
using RestLib.Configuration;
using RestLib.Hooks;
using RestLib.Hypermedia;
using RestLib.InMemory;
using RestLib.Pagination;
using RestLib.Responses;
using RestLib.Tests.Fakes;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Integration tests for mapped batch operations.
/// </summary>
[Trait("Type", "Integration")]
[Trait("Feature", "Mapping")]
public class TwoModelBatchOperationsTests : IAsyncLifetime
{
    private TrackingTwoModelBatchRepository _repository = null!;
    private IHost? _host;
    private HttpClient? _client;
    private BatchRepositorySpy<TwoModelBatchDbItem, Guid>? _batchSpy;
    private RepositorySpy<TwoModelBatchDbItem, Guid>? _repositorySpy;

    /// <inheritdoc />
    public Task InitializeAsync()
    {
        _repository = new TrackingTwoModelBatchRepository();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_host is not null)
        {
            await _host.StopAsync();
        }

        _host?.Dispose();
    }

    [Fact]
    public async Task MapRestLib_WithTwoModelBatchCreate_ReturnsApiModelsAndPersistsDbModels()
    {
        // Arrange
        await CreateHostAsync(config => config.EnableBatch(BatchAction.Create));

        var payload = new
        {
            action = "create",
            items = new[]
            {
                new { name = "Keyboard", price = 49.99m, category = "hardware", is_active = true },
                new { name = "Mouse", price = 29.99m, category = "hardware", is_active = true }
            }
        };

        // Act
        var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(2);
        items[0].GetProperty("status").GetInt32().Should().Be(201);
        items[0].GetProperty("entity").GetProperty("name").GetString().Should().Be("Keyboard");
        items[0].GetProperty("entity").TryGetProperty("internal_token", out _).Should().BeFalse();

        _repository.CreateCallCount.Should().Be(2);
        _repository.LastCreatedEntity.Should().NotBeNull();
        _repository.LastCreatedEntity!.InternalToken.Should().Be("mapped-from-api");
    }

    [Fact]
    public async Task MapRestLib_WithTwoModelBatchUpdate_ValidationUsesApiModelFieldNames()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.Seed(new TwoModelBatchDbItem
        {
            Id = id,
            Name = "Original",
            Price = 5m,
            Category = "hardware",
            IsActive = true,
            InternalToken = "db-only"
        });

        await CreateHostAsync(config => config.EnableBatch(BatchAction.Update));

        var payload = new
        {
            action = "update",
            items = new object[]
            {
                new { id, body = new { name = string.Empty, price = 10m, category = "hardware", is_active = true } }
            }
        };

        // Act
        var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.MultiStatus);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var error = json.GetProperty("items")[0].GetProperty("error");
        error.GetProperty("type").GetString().Should().Be(ProblemTypes.ValidationFailed);
        error.GetProperty("errors").TryGetProperty("name", out _).Should().BeTrue();
        _repository.UpdateCallCount.Should().Be(0);
    }

    [Fact]
    public async Task MapRestLib_WithTwoModelBatchPatch_UsesFullDbUpdateInsteadOfDbPatch()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.Seed(new TwoModelBatchDbItem
        {
            Id = id,
            Name = "Original",
            Price = 5m,
            Category = "hardware",
            IsActive = true,
            InternalToken = "db-only"
        });

        await CreateHostAsync(config => config.EnableBatch(BatchAction.Patch));

        var payload = new
        {
            action = "patch",
            items = new object[]
            {
                new { id, body = new { price = 15m } }
            }
        };

        // Act
        var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("items")[0].GetProperty("status").GetInt32().Should().Be(200);
        json.GetProperty("items")[0].GetProperty("entity").GetProperty("price").GetDecimal().Should().Be(15m);

        _repository.UpdateCallCount.Should().Be(1);
        _repository.PatchCallCount.Should().Be(0);

        var stored = await _repository.GetByIdAsync(id);
        stored.Should().NotBeNull();
        stored!.Price.Should().Be(15m);
    }

    [Fact]
    public async Task MapRestLib_WithTwoModelBatchDelete_UsesApiHooksByDefault()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.Seed(new TwoModelBatchDbItem
        {
            Id = id,
            Name = "Delete Me",
            Price = 5m,
            Category = "hardware",
            IsActive = true,
            InternalToken = "db-only"
        });

        object? capturedEntity = null;

        await CreateHostAsync(config =>
        {
            config.EnableBatch(BatchAction.Delete);
            config.UseHooks(hooks =>
            {
                hooks.BeforePersist = ctx =>
                {
                    capturedEntity = ctx.Entity;
                    return Task.CompletedTask;
                };
            });
        });

        var payload = new
        {
            action = "delete",
            items = new[] { id }
        };

        // Act
        var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        capturedEntity.Should().BeOfType<TwoModelBatchApiItem>();
    }

    [Fact]
    public async Task MapRestLib_WithTwoModelBatchCreate_UseDbModelHooksRunsDbHooks()
    {
        // Arrange
        object? capturedEntity = null;

        await CreateHostAsync(config =>
        {
            config.EnableBatch(BatchAction.Create);
            config.UseDbModelHooks(hooks =>
            {
                hooks.BeforePersist = ctx =>
                {
                    capturedEntity = ctx.Entity;
                    ctx.Entity!.InternalToken = "db-hooked";
                    return Task.CompletedTask;
                };
            });
        });

        var payload = new
        {
            action = "create",
            items = new[]
            {
                new { name = "Created", price = 10m, category = "hardware", is_active = true }
            }
        };

        // Act
        var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        capturedEntity.Should().BeOfType<TwoModelBatchDbItem>();
        _repository.LastCreatedEntity.Should().NotBeNull();
        _repository.LastCreatedEntity!.InternalToken.Should().Be("db-hooked");
    }

    [Fact]
    public async Task MapRestLib_WithTwoModelBatchCreate_HateoasUsesApiModel()
    {
        // Arrange
        var provider = new CapturingBatchApiLinkProvider();
        await CreateHostAsync(
            config => config.EnableBatch(BatchAction.Create),
            configureOptions: options => options.EnableHateoas = true,
            configureServices: services =>
            {
                services.AddSingleton(provider);
                services.AddSingleton<IHateoasLinkProvider<TwoModelBatchApiItem, Guid>>(provider);
            });

        var payload = new
        {
            action = "create",
            items = new[]
            {
                new { name = "Linked", price = 10m, category = "hardware", is_active = true }
            }
        };

        // Act
        var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        provider.LastEntity.Should().NotBeNull();
        provider.LastEntity!.Name.Should().Be("Linked");

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var entity = json.GetProperty("items")[0].GetProperty("entity");
        entity.GetProperty("_links").TryGetProperty("profile", out _).Should().BeTrue();
        entity.TryGetProperty("internal_token", out _).Should().BeFalse();
    }

    [Fact]
    public async Task MapRestLib_WithTwoModelBatchUpdate_UsesBatchRepository_WhenAvailable()
    {
        // Arrange
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        _repository.Seed(
            new TwoModelBatchDbItem
            {
                Id = id1,
                Name = "Old1",
                Price = 1m,
                Category = "hardware",
                IsActive = true,
                InternalToken = "db-1"
            },
            new TwoModelBatchDbItem
            {
                Id = id2,
                Name = "Old2",
                Price = 2m,
                Category = "hardware",
                IsActive = true,
                InternalToken = "db-2"
            });

        await CreateHostWithBatchRepositoryAsync(config => config.EnableBatch(BatchAction.Update));

        var payload = new
        {
            action = "update",
            items = new object[]
            {
                new { id = id1, body = new { name = "New1", price = 11m, category = "hardware", is_active = true } },
                new { id = id2, body = new { name = "New2", price = 22m, category = "hardware", is_active = true } }
            }
        };

        // Act
        var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _batchSpy!.UpdateManyCallCount.Should().Be(1);
    }

    [Fact]
    public async Task MapRestLib_WithTwoModelBatchPatch_UsesBulkUpdateAndSkipsDbPatchMany()
    {
        // Arrange
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        _repository.Seed(
            new TwoModelBatchDbItem
            {
                Id = id1,
                Name = "Patch1",
                Price = 1m,
                Category = "hardware",
                IsActive = true,
                InternalToken = "db-1"
            },
            new TwoModelBatchDbItem
            {
                Id = id2,
                Name = "Patch2",
                Price = 2m,
                Category = "hardware",
                IsActive = true,
                InternalToken = "db-2"
            });

        await CreateHostWithBatchRepositoryAsync(config => config.EnableBatch(BatchAction.Patch));

        var payload = new
        {
            action = "patch",
            items = new object[]
            {
                new { id = id1, body = new { price = 11m } },
                new { id = id2, body = new { price = 22m } }
            }
        };

        // Act
        var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _batchSpy!.UpdateManyCallCount.Should().Be(1);
        _batchSpy.PatchManyCallCount.Should().Be(0);
        _batchSpy.GetByIdsCallCount.Should().Be(1);
    }

    [Fact]
    public async Task MapRestLib_WithTwoModelBatchPatch_BulkThrows_FallsBackToIndividualUpdate()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.Seed(new TwoModelBatchDbItem
        {
            Id = id,
            Name = "Original",
            Price = 5m,
            Category = "hardware",
            IsActive = true,
            InternalToken = "db-only"
        });

        await CreateHostWithThrowingBulkRepositoryAsync(config => config.EnableBatch(BatchAction.Patch));

        var payload = new
        {
            action = "patch",
            items = new object[]
            {
                new { id, body = new { price = 15m } }
            }
        };

        // Act
        var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _repository.UpdateCallCount.Should().Be(1);
        _repository.PatchCallCount.Should().Be(0);
    }

    [Fact]
    public async Task MapRestLib_WithTwoModelBatchPatch_HookMutationsAreRevalidatedAgainstApiModel()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.Seed(new TwoModelBatchDbItem
        {
            Id = id,
            Name = "Original",
            Price = 5m,
            Category = "hardware",
            IsActive = true,
            InternalToken = "db-only"
        });

        await CreateHostAsync(config =>
        {
            config.EnableBatch(BatchAction.Patch);
            config.UseDbModelHooks(hooks =>
            {
                hooks.OnRequestValidated = ctx =>
                {
                    ctx.Entity!.Name = string.Empty;
                    return Task.CompletedTask;
                };
            });
        });

        var payload = new
        {
            action = "patch",
            items = new object[]
            {
                new { id, body = new { price = 15m } }
            }
        };

        // Act
        var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.MultiStatus);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var item = json.GetProperty("items")[0];
        item.GetProperty("status").GetInt32().Should().Be(400);
        item.GetProperty("error").GetProperty("errors").TryGetProperty("name", out _).Should().BeTrue();
        _repository.UpdateCallCount.Should().Be(0);
    }

    [Fact]
    public async Task MapRestLib_WithTwoModelBatchPatch_NonObjectPatchBodyReturnsBadRequest()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.Seed(new TwoModelBatchDbItem
        {
            Id = id,
            Name = "Original",
            Price = 5m,
            Category = "hardware",
            IsActive = true,
            InternalToken = "db-only"
        });

        await CreateHostWithBatchRepositoryAsync(config => config.EnableBatch(BatchAction.Patch));

        using var content = new StringContent(
            $$"""
            {
              "action": "patch",
              "items": [
                {
                  "id": "{{id}}",
                  "body": []
                }
              ]
            }
            """,
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await _client!.PostAsync("/api/items/batch", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.MultiStatus);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var item = json.GetProperty("items")[0];
        item.GetProperty("status").GetInt32().Should().Be(400);
        item.GetProperty("error").GetProperty("type").GetString().Should().Be(ProblemTypes.BadRequest);
        _batchSpy!.PatchManyCallCount.Should().Be(0);
        _batchSpy.UpdateManyCallCount.Should().Be(0);
    }

    [Fact]
    public async Task MapRestLib_WithTwoModelBatchCustomKey_BulkUpdateAndPatchPreserveConfiguredKey()
    {
        // Arrange
        var repository = new TrackingTwoModelBatchCustomKeyRepository();
        var id = Guid.NewGuid();
        repository.Seed(new TwoModelBatchCustomKeyDbItem
        {
            Code = id,
            Name = "Original",
            Price = 5m,
            Category = "hardware",
            IsActive = true,
            InternalToken = "db-only"
        });

        var batchSpy = new BatchRepositorySpy<TwoModelBatchCustomKeyDbItem, Guid>(repository);
        var repositorySpy = new RepositorySpy<TwoModelBatchCustomKeyDbItem, Guid>(repository);

        (_host, _client) = await new TestTwoModelHostBuilder<TwoModelBatchCustomKeyApiItem, TwoModelBatchCustomKeyDbItem, Guid>(repositorySpy, "/api/items")
            .WithServices(services =>
            {
                services.AddRestLibMapper<TwoModelBatchCustomKeyApiItem, TwoModelBatchCustomKeyDbItem>(
                    _ => new TwoModelBatchCustomKeyMapper());
                services.AddSingleton<IBatchRepository<TwoModelBatchCustomKeyDbItem, Guid>>(batchSpy);
            })
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.KeySelector = item => item.Code;
                config.EnableBatch(BatchAction.Update, BatchAction.Patch);
            })
            .BuildAsync();

        var updatePayload = new
        {
            action = "update",
            items = new object[]
            {
                new { id, body = new { name = "Updated", price = 20m, category = "hardware", is_active = true } }
            }
        };

        // Act
        var updateResponse = await _client!.PostAsync("/api/items/batch", BatchJson(updatePayload));

        // Assert
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        batchSpy.UpdateManyCallCount.Should().Be(1);
        var updated = await repository.GetByIdAsync(id);
        updated.Should().NotBeNull();
        updated!.Code.Should().Be(id);
        updated.Name.Should().Be("Updated");

        var patchPayload = new
        {
            action = "patch",
            items = new object[]
            {
                new { id, body = new { price = 30m } }
            }
        };

        // Act
        var patchResponse = await _client!.PostAsync("/api/items/batch", BatchJson(patchPayload));

        // Assert
        patchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        batchSpy.UpdateManyCallCount.Should().Be(2);
        var patched = await repository.GetByIdAsync(id);
        patched.Should().NotBeNull();
        patched!.Code.Should().Be(id);
        patched.Price.Should().Be(30m);
    }

    private static StringContent BatchJson(object payload)
    {
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private async Task CreateHostAsync(
        Action<RestLibEndpointConfiguration<TwoModelBatchApiItem, TwoModelBatchDbItem, Guid>> configure,
        Action<RestLibOptions>? configureOptions = null,
        Action<IServiceCollection>? configureServices = null)
    {
        (_host, _client) = await new TestTwoModelHostBuilder<TwoModelBatchApiItem, TwoModelBatchDbItem, Guid>(_repository, "/api/items")
            .WithOptions(configureOptions ?? (_ => { }))
            .WithServices(services =>
            {
                services.AddRestLibMapper<TwoModelBatchApiItem, TwoModelBatchDbItem>(_ => new TwoModelBatchMapper());
                configureServices?.Invoke(services);
            })
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                configure(config);
            })
            .BuildAsync();
    }

    private async Task CreateHostWithBatchRepositoryAsync(
        Action<RestLibEndpointConfiguration<TwoModelBatchApiItem, TwoModelBatchDbItem, Guid>> configure)
    {
        _batchSpy = new BatchRepositorySpy<TwoModelBatchDbItem, Guid>(_repository);
        _repositorySpy = new RepositorySpy<TwoModelBatchDbItem, Guid>(_repository);

        (_host, _client) = await new TestTwoModelHostBuilder<TwoModelBatchApiItem, TwoModelBatchDbItem, Guid>(_repositorySpy, "/api/items")
            .WithServices(services =>
            {
                services.AddRestLibMapper<TwoModelBatchApiItem, TwoModelBatchDbItem>(_ => new TwoModelBatchMapper());
                services.AddSingleton<IBatchRepository<TwoModelBatchDbItem, Guid>>(_batchSpy);
            })
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                configure(config);
            })
            .BuildAsync();
    }

    private async Task CreateHostWithThrowingBulkRepositoryAsync(
        Action<RestLibEndpointConfiguration<TwoModelBatchApiItem, TwoModelBatchDbItem, Guid>> configure)
    {
        (_host, _client) = await new TestTwoModelHostBuilder<TwoModelBatchApiItem, TwoModelBatchDbItem, Guid>(_repository, "/api/items")
            .WithServices(services =>
            {
                services.AddRestLibMapper<TwoModelBatchApiItem, TwoModelBatchDbItem>(_ => new TwoModelBatchMapper());
                services.AddSingleton<IBatchRepository<TwoModelBatchDbItem, Guid>>(new ThrowingMappedBulkBatchRepository());
            })
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                configure(config);
            })
            .BuildAsync();
    }

    private sealed class TwoModelBatchApiItem
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

    private sealed class TwoModelBatchDbItem
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public decimal Price { get; set; }

        public string Category { get; set; } = string.Empty;

        public bool IsActive { get; set; }

        public string InternalToken { get; set; } = string.Empty;
    }

    private sealed class TwoModelBatchCustomKeyApiItem
    {
        public Guid Code { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty;

        [Range(typeof(decimal), "0.01", "79228162514264337593543950335")]
        public decimal Price { get; set; }

        [Required]
        public string Category { get; set; } = string.Empty;

        public bool IsActive { get; set; }
    }

    private sealed class TwoModelBatchCustomKeyDbItem
    {
        public Guid Code { get; set; }

        public string Name { get; set; } = string.Empty;

        public decimal Price { get; set; }

        public string Category { get; set; } = string.Empty;

        public bool IsActive { get; set; }

        public string InternalToken { get; set; } = string.Empty;
    }

    private sealed class TwoModelBatchMapper : IRestLibMapper<TwoModelBatchApiItem, TwoModelBatchDbItem>
    {
        public TwoModelBatchApiItem ToApi(TwoModelBatchDbItem dbModel)
        {
            return new TwoModelBatchApiItem
            {
                Id = dbModel.Id,
                Name = dbModel.Name,
                Price = dbModel.Price,
                Category = dbModel.Category,
                IsActive = dbModel.IsActive
            };
        }

        public TwoModelBatchDbItem ToDb(TwoModelBatchApiItem apiModel)
        {
            return new TwoModelBatchDbItem
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

    private sealed class TwoModelBatchCustomKeyMapper : IRestLibMapper<TwoModelBatchCustomKeyApiItem, TwoModelBatchCustomKeyDbItem>
    {
        public TwoModelBatchCustomKeyApiItem ToApi(TwoModelBatchCustomKeyDbItem dbModel)
        {
            return new TwoModelBatchCustomKeyApiItem
            {
                Code = dbModel.Code,
                Name = dbModel.Name,
                Price = dbModel.Price,
                Category = dbModel.Category,
                IsActive = dbModel.IsActive
            };
        }

        public TwoModelBatchCustomKeyDbItem ToDb(TwoModelBatchCustomKeyApiItem apiModel)
        {
            return new TwoModelBatchCustomKeyDbItem
            {
                Code = apiModel.Code,
                Name = apiModel.Name,
                Price = apiModel.Price,
                Category = apiModel.Category,
                IsActive = apiModel.IsActive,
                InternalToken = "mapped-from-api"
            };
        }
    }

    private sealed class CapturingBatchApiLinkProvider : IHateoasLinkProvider<TwoModelBatchApiItem, Guid>
    {
        public TwoModelBatchApiItem? LastEntity { get; private set; }

        public IReadOnlyDictionary<string, HateoasLink>? GetLinks(TwoModelBatchApiItem entity, Guid key)
        {
            LastEntity = entity;
            return new Dictionary<string, HateoasLink>
            {
                ["profile"] = new HateoasLink { Href = $"/api/profiles/{key}" }
            };
        }
    }

    private sealed class TrackingTwoModelBatchRepository : IRepository<TwoModelBatchDbItem, Guid>, IBatchRepository<TwoModelBatchDbItem, Guid>
    {
        private readonly InMemoryRepository<TwoModelBatchDbItem, Guid> _inner =
            new(item => item.Id, Guid.NewGuid);

        public int CreateCallCount { get; private set; }

        public int PatchCallCount { get; private set; }

        public int UpdateCallCount { get; private set; }

        public TwoModelBatchDbItem? LastCreatedEntity { get; private set; }

        public async Task<TwoModelBatchDbItem> CreateAsync(TwoModelBatchDbItem entity, CancellationToken ct = default)
        {
            CreateCallCount++;
            LastCreatedEntity = entity;
            return await _inner.CreateAsync(entity, ct);
        }

        public Task<IReadOnlyList<TwoModelBatchDbItem>> CreateManyAsync(IReadOnlyList<TwoModelBatchDbItem> entities, CancellationToken ct = default)
        {
            return _inner.CreateManyAsync(entities, ct);
        }

        public Task<int> DeleteManyAsync(IReadOnlyList<Guid> keys, CancellationToken ct = default)
        {
            return _inner.DeleteManyAsync(keys, ct);
        }

        public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
        {
            return _inner.DeleteAsync(id, ct);
        }

        public Task<PagedResult<TwoModelBatchDbItem>> GetAllAsync(PaginationRequest pagination, CancellationToken ct = default)
        {
            return _inner.GetAllAsync(pagination, ct);
        }

        public Task<TwoModelBatchDbItem?> GetByIdAsync(Guid id, CancellationToken ct = default)
        {
            return _inner.GetByIdAsync(id, ct);
        }

        public Task<IReadOnlyDictionary<Guid, TwoModelBatchDbItem>> GetByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default)
        {
            return _inner.GetByIdsAsync(ids, ct);
        }

        public async Task<TwoModelBatchDbItem?> PatchAsync(Guid id, JsonElement patchDocument, CancellationToken ct = default)
        {
            PatchCallCount++;
            return await _inner.PatchAsync(id, patchDocument, ct);
        }

        public Task<IReadOnlyList<TwoModelBatchDbItem>> PatchManyAsync(IReadOnlyList<(Guid Id, JsonElement PatchDocument)> patches, CancellationToken ct = default)
        {
            return _inner.PatchManyAsync(patches, ct);
        }

        public void Seed(params TwoModelBatchDbItem[] entities)
        {
            _inner.Seed(entities);
        }

        public async Task<TwoModelBatchDbItem?> UpdateAsync(Guid id, TwoModelBatchDbItem entity, CancellationToken ct = default)
        {
            UpdateCallCount++;
            return await _inner.UpdateAsync(id, entity, ct);
        }

        public Task<IReadOnlyList<TwoModelBatchDbItem>> UpdateManyAsync(IReadOnlyList<TwoModelBatchDbItem> entities, CancellationToken ct = default)
        {
            return _inner.UpdateManyAsync(entities, ct);
        }
    }

    private sealed class TrackingTwoModelBatchCustomKeyRepository : IRepository<TwoModelBatchCustomKeyDbItem, Guid>, IBatchRepository<TwoModelBatchCustomKeyDbItem, Guid>
    {
        private readonly InMemoryRepository<TwoModelBatchCustomKeyDbItem, Guid> _inner =
            new(item => item.Code, Guid.NewGuid);

        public Task<TwoModelBatchCustomKeyDbItem> CreateAsync(TwoModelBatchCustomKeyDbItem entity, CancellationToken ct = default)
        {
            return _inner.CreateAsync(entity, ct);
        }

        public Task<IReadOnlyList<TwoModelBatchCustomKeyDbItem>> CreateManyAsync(IReadOnlyList<TwoModelBatchCustomKeyDbItem> entities, CancellationToken ct = default)
        {
            return _inner.CreateManyAsync(entities, ct);
        }

        public Task<int> DeleteManyAsync(IReadOnlyList<Guid> keys, CancellationToken ct = default)
        {
            return _inner.DeleteManyAsync(keys, ct);
        }

        public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
        {
            return _inner.DeleteAsync(id, ct);
        }

        public Task<PagedResult<TwoModelBatchCustomKeyDbItem>> GetAllAsync(PaginationRequest pagination, CancellationToken ct = default)
        {
            return _inner.GetAllAsync(pagination, ct);
        }

        public Task<TwoModelBatchCustomKeyDbItem?> GetByIdAsync(Guid id, CancellationToken ct = default)
        {
            return _inner.GetByIdAsync(id, ct);
        }

        public Task<IReadOnlyDictionary<Guid, TwoModelBatchCustomKeyDbItem>> GetByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default)
        {
            return _inner.GetByIdsAsync(ids, ct);
        }

        public Task<TwoModelBatchCustomKeyDbItem?> PatchAsync(Guid id, JsonElement patchDocument, CancellationToken ct = default)
        {
            return _inner.PatchAsync(id, patchDocument, ct);
        }

        public Task<IReadOnlyList<TwoModelBatchCustomKeyDbItem>> PatchManyAsync(IReadOnlyList<(Guid Id, JsonElement PatchDocument)> patches, CancellationToken ct = default)
        {
            return _inner.PatchManyAsync(patches, ct);
        }

        public void Seed(params TwoModelBatchCustomKeyDbItem[] entities)
        {
            _inner.Seed(entities);
        }

        public Task<TwoModelBatchCustomKeyDbItem?> UpdateAsync(Guid id, TwoModelBatchCustomKeyDbItem entity, CancellationToken ct = default)
        {
            return _inner.UpdateAsync(id, entity, ct);
        }

        public Task<IReadOnlyList<TwoModelBatchCustomKeyDbItem>> UpdateManyAsync(IReadOnlyList<TwoModelBatchCustomKeyDbItem> entities, CancellationToken ct = default)
        {
            return _inner.UpdateManyAsync(entities, ct);
        }
    }

    private sealed class ThrowingMappedBulkBatchRepository : IBatchRepository<TwoModelBatchDbItem, Guid>
    {
        public Task<IReadOnlyList<TwoModelBatchDbItem>> CreateManyAsync(IReadOnlyList<TwoModelBatchDbItem> entities, CancellationToken ct = default)
            => throw new InvalidOperationException("Simulated bulk create failure");

        public Task<IReadOnlyList<TwoModelBatchDbItem>> UpdateManyAsync(IReadOnlyList<TwoModelBatchDbItem> entities, CancellationToken ct = default)
            => throw new InvalidOperationException("Simulated bulk update failure");

        public Task<IReadOnlyList<TwoModelBatchDbItem>> PatchManyAsync(IReadOnlyList<(Guid Id, JsonElement PatchDocument)> patches, CancellationToken ct = default)
            => throw new InvalidOperationException("Simulated bulk patch failure");

        public Task<int> DeleteManyAsync(IReadOnlyList<Guid> keys, CancellationToken ct = default)
            => throw new InvalidOperationException("Simulated bulk delete failure");

        public Task<IReadOnlyDictionary<Guid, TwoModelBatchDbItem>> GetByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default)
            => throw new InvalidOperationException("Simulated bulk get-by-ids failure");
    }
}
