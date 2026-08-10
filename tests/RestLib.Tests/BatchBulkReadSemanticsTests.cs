using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RestLib.Abstractions;
using RestLib.Batch;
using RestLib.Configuration;
using RestLib.Hooks;
using RestLib.Pagination;
using RestLib.Tests.Fakes;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Regression tests for the batch update and patch bulk-read preparation stage.
/// </summary>
[Trait("Type", "Integration")]
[Trait("Feature", "Batch")]
public class BatchBulkReadSemanticsTests
{
    [Theory]
    [InlineData(BatchAction.Update)]
    [InlineData(BatchAction.Patch)]
    public async Task BatchWrite_BulkReadSnapshot_RunsUnmappedHooksInRequestOrderAndPersistsMutations(
        BatchAction action)
    {
        // Arrange
        var first = NewEntity("First", "first-original");
        var second = NewEntity("Second", "second-original");
        var missingId = Guid.NewGuid();
        var repository = new TrackingBatchRepository<SnapshotEntity>(Clone);
        repository.Seed(first, second);
        var originalNames = new List<string>();
        var hookIds = new List<Guid>();
        var afterPersistCalls = 0;
        var (host, client) = await CreateUnmappedHostAsync(repository, action, hooks =>
        {
            hooks.BeforePersist = context =>
            {
                originalNames.Add(context.OriginalEntity!.Name);
                hookIds.Add(context.ResourceId);
                context.Entity!.Name = $"hooked-{context.OriginalEntity.Name}";
                return Task.CompletedTask;
            };
            hooks.AfterPersist = _ =>
            {
                afterPersistCalls++;
                return Task.CompletedTask;
            };
        });
        using var hostHandle = host;
        using var clientHandle = client;

        // Act
        var response = await client.PostAsJsonAsync(
            "/api/items/batch",
            CreatePayload(action, second.Id, missingId, first.Id));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.MultiStatus);
        var items = await ReadItemsAsync(response);
        AssertStatuses(items, 200, 404, 200);
        AssertResponseNames(items, "hooked-Second", "hooked-First");
        originalNames.Should().Equal("Second", "First");
        hookIds.Should().Equal(second.Id, first.Id);
        repository.Find(second.Id)!.Name.Should().Be("hooked-Second");
        repository.Find(first.Id)!.Name.Should().Be("hooked-First");
        repository.Find(missingId).Should().BeNull();
        repository.GetByIdsCallCount.Should().Be(1);
        repository.GetByIdCallCount.Should().Be(0);
        repository.UpdateManyCallCount.Should().Be(1);
        repository.PatchManyCallCount.Should().Be(0);
        repository.SingleWriteCallCount.Should().Be(0);
        afterPersistCalls.Should().Be(2);
    }

    [Theory]
    [InlineData(BatchAction.Update)]
    [InlineData(BatchAction.Patch)]
    public async Task MappedBatchWrite_BulkReadSnapshot_RunsApiHooksInRequestOrderAndPersistsMutations(
        BatchAction action)
    {
        // Arrange
        var first = NewDbEntity("First", "first-original");
        var second = NewDbEntity("Second", "second-original");
        var missingId = Guid.NewGuid();
        var repository = new TrackingBatchRepository<SnapshotDbEntity>(Clone);
        repository.Seed(first, second);
        var originalNames = new List<string>();
        var hookIds = new List<Guid>();
        var afterPersistCalls = 0;
        var (host, client) = await CreateMappedApiHookHostAsync(repository, action, hooks =>
        {
            hooks.BeforePersist = context =>
            {
                originalNames.Add(context.OriginalEntity!.Name);
                hookIds.Add(context.ResourceId);
                context.Entity!.Name = $"api-hooked-{context.OriginalEntity.Name}";
                return Task.CompletedTask;
            };
            hooks.AfterPersist = _ =>
            {
                afterPersistCalls++;
                return Task.CompletedTask;
            };
        });
        using var hostHandle = host;
        using var clientHandle = client;

        // Act
        var response = await client.PostAsJsonAsync(
            "/api/items/batch",
            CreatePayload(action, second.Id, missingId, first.Id));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.MultiStatus);
        var items = await ReadItemsAsync(response);
        AssertStatuses(items, 200, 404, 200);
        AssertResponseNames(items, "api-hooked-Second", "api-hooked-First");
        originalNames.Should().Equal("Second", "First");
        hookIds.Should().Equal(second.Id, first.Id);
        repository.Find(second.Id)!.Name.Should().Be("api-hooked-Second");
        repository.Find(first.Id)!.Name.Should().Be("api-hooked-First");
        repository.Find(missingId).Should().BeNull();
        AssertBulkCallCounts(repository, afterPersistCalls);
    }

    [Theory]
    [InlineData(BatchAction.Update)]
    [InlineData(BatchAction.Patch)]
    public async Task MappedBatchWrite_BulkReadSnapshot_RunsDbHooksInRequestOrderAndPersistsMutations(
        BatchAction action)
    {
        // Arrange
        var first = NewDbEntity("First", "first-original");
        var second = NewDbEntity("Second", "second-original");
        var missingId = Guid.NewGuid();
        var repository = new TrackingBatchRepository<SnapshotDbEntity>(Clone);
        repository.Seed(first, second);
        var originalNames = new List<string>();
        var originalMarkers = new List<string>();
        var hookIds = new List<Guid>();
        var afterPersistCalls = 0;
        var (host, client) = await CreateMappedDbHookHostAsync(repository, action, hooks =>
        {
            hooks.BeforePersist = context =>
            {
                originalNames.Add(context.OriginalEntity!.Name);
                originalMarkers.Add(context.OriginalEntity.Marker);
                hookIds.Add(context.ResourceId);
                context.Entity!.Name = $"db-hooked-{context.OriginalEntity.Name}";
                context.Entity.Marker = $"persisted-{context.OriginalEntity.Marker}";
                return Task.CompletedTask;
            };
            hooks.AfterPersist = _ =>
            {
                afterPersistCalls++;
                return Task.CompletedTask;
            };
        });
        using var hostHandle = host;
        using var clientHandle = client;

        // Act
        var response = await client.PostAsJsonAsync(
            "/api/items/batch",
            CreatePayload(action, second.Id, missingId, first.Id));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.MultiStatus);
        var items = await ReadItemsAsync(response);
        AssertStatuses(items, 200, 404, 200);
        AssertResponseNames(items, "db-hooked-Second", "db-hooked-First");
        originalNames.Should().Equal("Second", "First");
        originalMarkers.Should().Equal("second-original", "first-original");
        hookIds.Should().Equal(second.Id, first.Id);
        repository.Find(second.Id)!.Marker.Should().Be("persisted-second-original");
        repository.Find(first.Id)!.Marker.Should().Be("persisted-first-original");
        repository.Find(missingId).Should().BeNull();
        AssertBulkCallCounts(repository, afterPersistCalls);
    }

    [Fact]
    public async Task BatchUpdate_RepeatedKey_UsesOneSnapshotAndPersistsLastHookMutation()
    {
        // Arrange
        var original = NewEntity("Original", "original-marker");
        var repository = new TrackingBatchRepository<SnapshotEntity>(Clone);
        repository.Seed(original);
        var hookOriginalNames = new List<string>();
        var hookInputNames = new List<string>();
        var (host, client) = await CreateUnmappedHostAsync(repository, BatchAction.Update, hooks =>
        {
            hooks.BeforePersist = context =>
            {
                hookOriginalNames.Add(context.OriginalEntity!.Name);
                hookInputNames.Add(context.Entity!.Name);
                context.Entity.Name += "-hooked";
                return Task.CompletedTask;
            };
        });
        using var hostHandle = host;
        using var clientHandle = client;
        var payload = new
        {
            action = "update",
            items = new object[]
            {
                new { id = original.Id, body = new { name = "first", marker = "one" } },
                new { id = original.Id, body = new { name = "second", marker = "two" } }
            }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/items/batch", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await ReadItemsAsync(response);
        AssertStatuses(items, 200, 200);
        items.EnumerateArray()
            .Select(static item => item.GetProperty("entity").GetProperty("name").GetString())
            .Should().Equal("second-hooked", "second-hooked");
        hookOriginalNames.Should().Equal("Original", "Original");
        hookInputNames.Should().Equal("first", "second");
        repository.Find(original.Id)!.Name.Should().Be("second-hooked");
        repository.GetByIdsCallCount.Should().Be(1);
        repository.UpdateManyCallCount.Should().Be(1);
        repository.GetByIdCallCount.Should().Be(0);
        repository.SingleWriteCallCount.Should().Be(0);
    }

    [Theory]
    [InlineData(BatchAction.Update, MalformedLookup.NullResult)]
    [InlineData(BatchAction.Update, MalformedLookup.UnrequestedKey)]
    [InlineData(BatchAction.Update, MalformedLookup.MismatchedEntityKey)]
    [InlineData(BatchAction.Update, MalformedLookup.NullValue)]
    [InlineData(BatchAction.Patch, MalformedLookup.NullResult)]
    [InlineData(BatchAction.Patch, MalformedLookup.UnrequestedKey)]
    [InlineData(BatchAction.Patch, MalformedLookup.MismatchedEntityKey)]
    [InlineData(BatchAction.Patch, MalformedLookup.NullValue)]
    public async Task BatchWrite_MalformedBulkLookup_ReturnsIndexed500WithoutMutationHooksOrRetry(
        BatchAction action,
        MalformedLookup malformedLookup)
    {
        // Arrange
        var first = NewEntity("First", "first-original");
        var second = NewEntity("Second", "second-original");
        var repository = new TrackingBatchRepository<SnapshotEntity>(Clone);
        repository.Seed(first, second);
        repository.LookupOverride = ids => MalformedResult(ids, first, second, malformedLookup);
        var afterPersistCalls = 0;
        var (host, client) = await CreateUnmappedHostAsync(repository, action, hooks =>
        {
            hooks.AfterPersist = _ =>
            {
                afterPersistCalls++;
                return Task.CompletedTask;
            };
        });
        using var hostHandle = host;
        using var clientHandle = client;

        // Act
        var response = await client.PostAsJsonAsync(
            "/api/items/batch",
            CreatePayload(action, first.Id, second.Id));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.MultiStatus);
        var items = await ReadItemsAsync(response);
        AssertStatuses(items, 500, 500);
        items.EnumerateArray().Should().OnlyContain(
            static item => item.GetProperty("error").GetProperty("status").GetInt32() == 500);
        repository.Find(first.Id)!.Name.Should().Be("First");
        repository.Find(first.Id)!.Marker.Should().Be("first-original");
        repository.Find(second.Id)!.Name.Should().Be("Second");
        repository.Find(second.Id)!.Marker.Should().Be("second-original");
        repository.GetByIdsCallCount.Should().Be(1);
        repository.GetByIdCallCount.Should().Be(0);
        repository.UpdateManyCallCount.Should().Be(0);
        repository.PatchManyCallCount.Should().Be(0);
        repository.SingleWriteCallCount.Should().Be(0);
        afterPersistCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(BatchAction.Update, MalformedLookup.NullResult)]
    [InlineData(BatchAction.Update, MalformedLookup.UnrequestedKey)]
    [InlineData(BatchAction.Update, MalformedLookup.MismatchedEntityKey)]
    [InlineData(BatchAction.Update, MalformedLookup.NullValue)]
    [InlineData(BatchAction.Patch, MalformedLookup.NullResult)]
    [InlineData(BatchAction.Patch, MalformedLookup.UnrequestedKey)]
    [InlineData(BatchAction.Patch, MalformedLookup.MismatchedEntityKey)]
    [InlineData(BatchAction.Patch, MalformedLookup.NullValue)]
    public async Task MappedBatchWrite_MalformedBulkLookup_ReturnsIndexed500WithoutMutationHooksOrRetry(
        BatchAction action,
        MalformedLookup malformedLookup)
    {
        // Arrange
        var first = NewDbEntity("First", "first-original");
        var second = NewDbEntity("Second", "second-original");
        var repository = new TrackingBatchRepository<SnapshotDbEntity>(Clone);
        repository.Seed(first, second);
        repository.LookupOverride = ids => MalformedResult(ids, first, second, malformedLookup);
        var afterPersistCalls = 0;
        var (host, client) = await CreateMappedApiHookHostAsync(repository, action, hooks =>
        {
            hooks.AfterPersist = _ =>
            {
                afterPersistCalls++;
                return Task.CompletedTask;
            };
        });
        using var hostHandle = host;
        using var clientHandle = client;

        // Act
        var response = await client.PostAsJsonAsync(
            "/api/items/batch",
            CreatePayload(action, first.Id, second.Id));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.MultiStatus);
        var items = await ReadItemsAsync(response);
        AssertStatuses(items, 500, 500);
        repository.Find(first.Id)!.Name.Should().Be("First");
        repository.Find(first.Id)!.Marker.Should().Be("first-original");
        repository.Find(second.Id)!.Name.Should().Be("Second");
        repository.Find(second.Id)!.Marker.Should().Be("second-original");
        repository.GetByIdsCallCount.Should().Be(1);
        repository.GetByIdCallCount.Should().Be(0);
        repository.UpdateManyCallCount.Should().Be(0);
        repository.PatchManyCallCount.Should().Be(0);
        repository.SingleWriteCallCount.Should().Be(0);
        afterPersistCalls.Should().Be(0);
    }

    private static void AssertBulkCallCounts(
        TrackingBatchRepository<SnapshotDbEntity> repository,
        int afterPersistCalls)
    {
        repository.GetByIdsCallCount.Should().Be(1);
        repository.GetByIdCallCount.Should().Be(0);
        repository.UpdateManyCallCount.Should().Be(1);
        repository.PatchManyCallCount.Should().Be(0);
        repository.SingleWriteCallCount.Should().Be(0);
        afterPersistCalls.Should().Be(2);
    }

    private static object CreatePayload(BatchAction action, params Guid[] ids)
    {
        var actionName = action == BatchAction.Update ? "update" : "patch";
        return new
        {
            action = actionName,
            items = ids.Select((id, index) => new
            {
                id,
                body = new { name = $"request-{index}", marker = $"request-marker-{index}" }
            })
        };
    }

    private static IReadOnlyDictionary<Guid, SnapshotEntity> MalformedResult(
        IReadOnlyList<Guid> requestedIds,
        SnapshotEntity first,
        SnapshotEntity second,
        MalformedLookup malformedLookup)
    {
        var unrequestedId = Guid.NewGuid();
        return malformedLookup switch
        {
            MalformedLookup.NullResult => null!,
            MalformedLookup.UnrequestedKey => new Dictionary<Guid, SnapshotEntity>
            {
                [unrequestedId] = new SnapshotEntity { Id = unrequestedId, Name = "Unrequested" }
            },
            MalformedLookup.MismatchedEntityKey => new Dictionary<Guid, SnapshotEntity>
            {
                [requestedIds[0]] = Clone(second)
            },
            MalformedLookup.NullValue => new Dictionary<Guid, SnapshotEntity>
            {
                [requestedIds[0]] = null!
            },
            _ => new Dictionary<Guid, SnapshotEntity>
            {
                [first.Id] = Clone(first),
                [second.Id] = Clone(second)
            }
        };
    }

    private static IReadOnlyDictionary<Guid, SnapshotDbEntity> MalformedResult(
        IReadOnlyList<Guid> requestedIds,
        SnapshotDbEntity first,
        SnapshotDbEntity second,
        MalformedLookup malformedLookup)
    {
        var unrequestedId = Guid.NewGuid();
        return malformedLookup switch
        {
            MalformedLookup.NullResult => null!,
            MalformedLookup.UnrequestedKey => new Dictionary<Guid, SnapshotDbEntity>
            {
                [unrequestedId] = new SnapshotDbEntity { Id = unrequestedId, Name = "Unrequested" }
            },
            MalformedLookup.MismatchedEntityKey => new Dictionary<Guid, SnapshotDbEntity>
            {
                [requestedIds[0]] = Clone(second)
            },
            MalformedLookup.NullValue => new Dictionary<Guid, SnapshotDbEntity>
            {
                [requestedIds[0]] = null!
            },
            _ => new Dictionary<Guid, SnapshotDbEntity>
            {
                [first.Id] = Clone(first),
                [second.Id] = Clone(second)
            }
        };
    }

    private static async Task<(IHost Host, HttpClient Client)> CreateUnmappedHostAsync(
        TrackingBatchRepository<SnapshotEntity> repository,
        BatchAction action,
        Action<RestLibHooks<SnapshotEntity, Guid>> configureHooks)
    {
        return await new TestHostBuilder<SnapshotEntity, Guid>(repository, "/api/items")
            .WithServices(services =>
                services.AddSingleton<IBatchRepository<SnapshotEntity, Guid>>(repository))
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.EnableBatch(action);
                config.UseHooks(configureHooks);
            })
            .BuildAsync();
    }

    private static async Task<(IHost Host, HttpClient Client)> CreateMappedApiHookHostAsync(
        TrackingBatchRepository<SnapshotDbEntity> repository,
        BatchAction action,
        Action<RestLibHooks<SnapshotApiEntity, Guid>> configureHooks)
    {
        return await CreateMappedHostAsync(repository, action, config =>
            config.UseHooks(configureHooks));
    }

    private static async Task<(IHost Host, HttpClient Client)> CreateMappedDbHookHostAsync(
        TrackingBatchRepository<SnapshotDbEntity> repository,
        BatchAction action,
        Action<RestLibHooks<SnapshotDbEntity, Guid>> configureHooks)
    {
        return await CreateMappedHostAsync(repository, action, config =>
            config.UseDbModelHooks(configureHooks));
    }

    private static async Task<(IHost Host, HttpClient Client)> CreateMappedHostAsync(
        TrackingBatchRepository<SnapshotDbEntity> repository,
        BatchAction action,
        Action<RestLibEndpointConfiguration<SnapshotApiEntity, SnapshotDbEntity, Guid>> configureEndpoint)
    {
        return await new TestTwoModelHostBuilder<SnapshotApiEntity, SnapshotDbEntity, Guid>(
            repository,
            "/api/items")
            .WithServices(services =>
            {
                services.AddRestLibMapper<SnapshotApiEntity, SnapshotDbEntity>(
                    _ => new SnapshotMapper());
                services.AddSingleton<IBatchRepository<SnapshotDbEntity, Guid>>(repository);
            })
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.EnableBatch(action);
                configureEndpoint(config);
            })
            .BuildAsync();
    }

    private static async Task<JsonElement> ReadItemsAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("items");
    }

    private static void AssertStatuses(JsonElement items, params int[] statuses)
    {
        items.GetArrayLength().Should().Be(statuses.Length);
        items.EnumerateArray()
            .Select(static item => item.GetProperty("index").GetInt32())
            .Should().Equal(Enumerable.Range(0, statuses.Length));
        items.EnumerateArray()
            .Select(static item => item.GetProperty("status").GetInt32())
            .Should().Equal(statuses);
    }

    private static void AssertResponseNames(
        JsonElement items,
        string firstExpectedName,
        string secondExpectedName)
    {
        items[0].GetProperty("entity").GetProperty("name").GetString()
            .Should().Be(firstExpectedName);
        items[2].GetProperty("entity").GetProperty("name").GetString()
            .Should().Be(secondExpectedName);
    }

    private static SnapshotEntity NewEntity(string name, string marker)
    {
        return new SnapshotEntity { Id = Guid.NewGuid(), Name = name, Marker = marker };
    }

    private static SnapshotDbEntity NewDbEntity(string name, string marker)
    {
        return new SnapshotDbEntity { Id = Guid.NewGuid(), Name = name, Marker = marker };
    }

    private static SnapshotEntity Clone(SnapshotEntity entity)
    {
        return new SnapshotEntity { Id = entity.Id, Name = entity.Name, Marker = entity.Marker };
    }

    private static SnapshotDbEntity Clone(SnapshotDbEntity entity)
    {
        return new SnapshotDbEntity { Id = entity.Id, Name = entity.Name, Marker = entity.Marker };
    }

    /// <summary>
    /// Describes an invalid result from <see cref="IBatchRepository{TEntity,TKey}.GetByIdsAsync"/>.
    /// </summary>
    public enum MalformedLookup
    {
        /// <summary>The lookup result itself is null.</summary>
        NullResult,

        /// <summary>The lookup contains a key that was not requested.</summary>
        UnrequestedKey,

        /// <summary>A dictionary key does not match the resource key of its value.</summary>
        MismatchedEntityKey,

        /// <summary>The lookup contains a null entity value.</summary>
        NullValue
    }

    private interface ITestEntity
    {
        Guid Id { get; set; }
    }

    private sealed class SnapshotEntity : ITestEntity
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Marker { get; set; } = string.Empty;
    }

    private sealed class SnapshotApiEntity
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Marker { get; set; } = string.Empty;
    }

    private sealed class SnapshotDbEntity : ITestEntity
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Marker { get; set; } = string.Empty;
    }

    private sealed class SnapshotMapper : IRestLibMapper<SnapshotApiEntity, SnapshotDbEntity>
    {
        public SnapshotApiEntity ToApi(SnapshotDbEntity dbModel)
        {
            return new SnapshotApiEntity
            {
                Id = dbModel.Id,
                Name = dbModel.Name,
                Marker = dbModel.Marker
            };
        }

        public SnapshotDbEntity ToDb(SnapshotApiEntity apiModel)
        {
            return new SnapshotDbEntity
            {
                Id = apiModel.Id,
                Name = apiModel.Name,
                Marker = apiModel.Marker
            };
        }
    }

    private sealed class TrackingBatchRepository<TEntity> :
        IRepository<TEntity, Guid>,
        IBatchRepository<TEntity, Guid>
        where TEntity : class, ITestEntity
    {
        private readonly Func<TEntity, TEntity> _clone;
        private readonly Dictionary<Guid, TEntity> _entities = new();

        public TrackingBatchRepository(Func<TEntity, TEntity> clone)
        {
            _clone = clone;
        }

        public Func<IReadOnlyList<Guid>, IReadOnlyDictionary<Guid, TEntity>>? LookupOverride { get; set; }

        public int GetByIdsCallCount { get; private set; }

        public int GetByIdCallCount { get; private set; }

        public int UpdateManyCallCount { get; private set; }

        public int PatchManyCallCount { get; private set; }

        public int SingleWriteCallCount { get; private set; }

        public void Seed(params TEntity[] entities)
        {
            foreach (var entity in entities)
            {
                _entities[entity.Id] = _clone(entity);
            }
        }

        public TEntity? Find(Guid id)
        {
            return _entities.TryGetValue(id, out var entity) ? _clone(entity) : null;
        }

        public Task<IReadOnlyDictionary<Guid, TEntity>> GetByIdsAsync(
            IReadOnlyList<Guid> ids,
            CancellationToken ct = default)
        {
            GetByIdsCallCount++;
            if (LookupOverride is not null)
            {
                return Task.FromResult(LookupOverride(ids));
            }

            IReadOnlyDictionary<Guid, TEntity> result = ids
                .Distinct()
                .Where(_entities.ContainsKey)
                .ToDictionary(id => id, id => _clone(_entities[id]));
            return Task.FromResult(result);
        }

        public Task<IReadOnlyList<TEntity>> UpdateManyAsync(
            IReadOnlyList<TEntity> entities,
            CancellationToken ct = default)
        {
            UpdateManyCallCount++;
            foreach (var entity in entities)
            {
                _entities[entity.Id] = _clone(entity);
            }

            IReadOnlyList<TEntity> result = entities
                .Select(entity => _clone(_entities[entity.Id]))
                .ToList();
            return Task.FromResult(result);
        }

        public Task<IReadOnlyList<TEntity>> PatchManyAsync(
            IReadOnlyList<(Guid Id, JsonElement PatchDocument)> patches,
            CancellationToken ct = default)
        {
            PatchManyCallCount++;
            throw new InvalidOperationException("PatchManyAsync was not expected in this fixture.");
        }

        public Task<TEntity?> GetByIdAsync(Guid id, CancellationToken ct = default)
        {
            GetByIdCallCount++;
            return Task.FromResult(Find(id));
        }

        public Task<TEntity?> UpdateAsync(Guid id, TEntity entity, CancellationToken ct = default)
        {
            SingleWriteCallCount++;
            _entities[id] = _clone(entity);
            return Task.FromResult<TEntity?>(_clone(_entities[id]));
        }

        public Task<TEntity?> PatchAsync(
            Guid id,
            JsonElement patchDocument,
            CancellationToken ct = default)
        {
            SingleWriteCallCount++;
            throw new InvalidOperationException("PatchAsync was not expected in this fixture.");
        }

        public Task<TEntity> CreateAsync(TEntity entity, CancellationToken ct = default)
        {
            SingleWriteCallCount++;
            _entities[entity.Id] = _clone(entity);
            return Task.FromResult(_clone(entity));
        }

        public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
        {
            SingleWriteCallCount++;
            return Task.FromResult(_entities.Remove(id));
        }

        public Task<IReadOnlyList<TEntity>> CreateManyAsync(
            IReadOnlyList<TEntity> entities,
            CancellationToken ct = default)
        {
            throw new InvalidOperationException("CreateManyAsync was not expected in this fixture.");
        }

        public Task<int> DeleteManyAsync(
            IReadOnlyList<Guid> keys,
            CancellationToken ct = default)
        {
            throw new InvalidOperationException("DeleteManyAsync was not expected in this fixture.");
        }

        public Task<PagedResult<TEntity>> GetAllAsync(
            PaginationRequest pagination,
            CancellationToken ct = default)
        {
            throw new InvalidOperationException("GetAllAsync was not expected in this fixture.");
        }
    }
}
