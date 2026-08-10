using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using RestLib.Abstractions;
using RestLib.Batch;
using RestLib.Configuration;
using RestLib.Hooks;
using RestLib.Pagination;
using RestLib.Tests.Fakes;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Integration tests for associating custom bulk-repository results with batch request items.
/// </summary>
[Trait("Type", "Integration")]
[Trait("Feature", "Batch")]
public class BatchResultAssociationTests : IAsyncLifetime
{
    private IHost? _host;
    private HttpClient? _client;

    /// <inheritdoc />
    public Task InitializeAsync() => Task.CompletedTask;

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
    }

    [Theory]
    [InlineData(MalformedCreateResult.NullList)]
    [InlineData(MalformedCreateResult.TooFew)]
    [InlineData(MalformedCreateResult.TooMany)]
    [InlineData(MalformedCreateResult.NullItem)]
    public async Task BatchCreate_BulkResultViolatesContract_ReturnsPerItem500WithoutHooksOrRetry(
        MalformedCreateResult malformedResult)
    {
        // Arrange
        var repository = CreateRepositorySubstitute();
        var batchRepository = (IBatchRepository<BatchEntity, Guid>)repository;
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var individualCreateCalls = 0;
        var afterPersistCalls = 0;

        batchRepository.CreateManyAsync(
                Arg.Any<IReadOnlyList<BatchEntity>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var input = callInfo.ArgAt<IReadOnlyList<BatchEntity>>(0);
                var created = new List<BatchEntity>
                {
                    Copy(input[0], firstId),
                    Copy(input[1], secondId)
                };

                IReadOnlyList<BatchEntity> result = malformedResult switch
                {
                    MalformedCreateResult.NullList => null!,
                    MalformedCreateResult.TooFew => [created[0]],
                    MalformedCreateResult.TooMany =>
                    [
                        created[0],
                        created[1],
                        new BatchEntity { Id = Guid.NewGuid(), Name = "Unexpected", Price = 99m }
                    ],
                    MalformedCreateResult.NullItem => [created[0], null!],
                    _ => throw new InvalidOperationException($"Unknown malformed result: {malformedResult}")
                };

                return Task.FromResult(result);
            });
        repository.CreateAsync(Arg.Any<BatchEntity>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                individualCreateCalls++;
                return Task.FromResult(callInfo.ArgAt<BatchEntity>(0));
            });

        await CreateUnmappedHostAsync(
            repository,
            batchRepository,
            BatchAction.Create,
            hooks => hooks.AfterPersist = _ =>
            {
                afterPersistCalls++;
                return Task.CompletedTask;
            });
        var payload = new
        {
            action = "create",
            items = new[]
            {
                new { name = "First", price = 1m },
                new { name = "Second", price = 2m }
            }
        };

        // Act
        var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.MultiStatus);
        var items = await ReadItemsAsync(response);
        AssertStatuses(items, 500, 500);
        afterPersistCalls.Should().Be(0);
        individualCreateCalls.Should().Be(0);
    }

    [Fact]
    public async Task BatchCreate_ExplicitKeyResultsAreReordered_ReturnsPerItem500WithoutHooksOrRetry()
    {
        // Arrange
        var repository = CreateRepositorySubstitute();
        var batchRepository = (IBatchRepository<BatchEntity, Guid>)repository;
        var afterPersistCalls = 0;

        batchRepository.CreateManyAsync(
                Arg.Any<IReadOnlyList<BatchEntity>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult<IReadOnlyList<BatchEntity>>(
                callInfo.ArgAt<IReadOnlyList<BatchEntity>>(0).Reverse().ToList()));

        await CreateUnmappedHostAsync(
            repository,
            batchRepository,
            BatchAction.Create,
            hooks => hooks.AfterPersist = _ =>
            {
                afterPersistCalls++;
                return Task.CompletedTask;
            });
        var payload = new
        {
            action = "create",
            items = new[]
            {
                new { id = Guid.NewGuid(), name = "First", price = 1m },
                new { id = Guid.NewGuid(), name = "Second", price = 2m }
            }
        };

        // Act
        var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.MultiStatus);
        var items = await ReadItemsAsync(response);
        AssertStatuses(items, 500, 500);
        afterPersistCalls.Should().Be(0);
        await repository.DidNotReceive()
            .CreateAsync(Arg.Any<BatchEntity>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BatchUpdate_BulkOmitsFirstResult_AssociatesRemainingEntityWithOriginalIndex()
    {
        // Arrange
        var first = new BatchEntity { Id = Guid.NewGuid(), Name = "First", Price = 1m };
        var second = new BatchEntity { Id = Guid.NewGuid(), Name = "Second", Price = 2m };
        var stored = new Dictionary<Guid, BatchEntity>
        {
            [first.Id] = first,
            [second.Id] = second
        };
        var repository = CreateRepositorySubstitute(stored);
        var batchRepository = (IBatchRepository<BatchEntity, Guid>)repository;
        var individualUpdateCalls = 0;
        var afterPersistCalls = 0;

        ConfigureBulkRead(batchRepository, stored);
        batchRepository.UpdateManyAsync(
                Arg.Any<IReadOnlyList<BatchEntity>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult<IReadOnlyList<BatchEntity>>(
                [callInfo.ArgAt<IReadOnlyList<BatchEntity>>(0)[1]]));
        repository.UpdateAsync(Arg.Any<Guid>(), Arg.Any<BatchEntity>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                individualUpdateCalls++;
                return Task.FromResult<BatchEntity?>(callInfo.ArgAt<BatchEntity>(1));
            });

        await CreateUnmappedHostAsync(
            repository,
            batchRepository,
            BatchAction.Update,
            hooks => hooks.AfterPersist = _ =>
            {
                afterPersistCalls++;
                return Task.CompletedTask;
            });
        var payload = UpdatePayload(first.Id, second.Id);

        // Act
        var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.MultiStatus);
        var items = await ReadItemsAsync(response);
        AssertStatuses(items, 404, 200);
        items[1].GetProperty("entity").GetProperty("id").GetGuid().Should().Be(second.Id);
        items[1].GetProperty("entity").GetProperty("name").GetString().Should().Be("Updated second");
        afterPersistCalls.Should().Be(1);
        individualUpdateCalls.Should().Be(0);
    }

    [Fact]
    public async Task BatchUpdate_RepeatedKeyResults_PreservesBothOriginalResponseSlots()
    {
        // Arrange
        var id = Guid.NewGuid();
        var stored = new Dictionary<Guid, BatchEntity>
        {
            [id] = new BatchEntity { Id = id, Name = "Original", Price = 1m }
        };
        var repository = CreateRepositorySubstitute(stored);
        var batchRepository = (IBatchRepository<BatchEntity, Guid>)repository;
        var afterPersistCalls = 0;

        ConfigureBulkRead(batchRepository, stored);
        batchRepository.UpdateManyAsync(
                Arg.Any<IReadOnlyList<BatchEntity>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var input = callInfo.ArgAt<IReadOnlyList<BatchEntity>>(0);
                return Task.FromResult<IReadOnlyList<BatchEntity>>([input[1], input[1]]);
            });

        await CreateUnmappedHostAsync(
            repository,
            batchRepository,
            BatchAction.Update,
            hooks => hooks.AfterPersist = _ =>
            {
                afterPersistCalls++;
                return Task.CompletedTask;
            });
        var payload = new
        {
            action = "update",
            items = new object[]
            {
                new { id, body = new { name = "First value", price = 11m } },
                new { id, body = new { name = "Final value", price = 22m } }
            }
        };

        // Act
        var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await ReadItemsAsync(response);
        AssertStatuses(items, 200, 200);
        items.EnumerateArray()
            .Select(item => item.GetProperty("entity").GetProperty("name").GetString())
            .Should().Equal("Final value", "Final value");
        afterPersistCalls.Should().Be(2);
        await repository.DidNotReceive()
            .UpdateAsync(Arg.Any<Guid>(), Arg.Any<BatchEntity>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BatchPatch_BulkOmitsFirstResult_AssociatesRemainingEntityWithOriginalIndex()
    {
        // Arrange
        var first = new BatchEntity { Id = Guid.NewGuid(), Name = "First", Price = 1m };
        var second = new BatchEntity { Id = Guid.NewGuid(), Name = "Second", Price = 2m };
        var stored = new Dictionary<Guid, BatchEntity>
        {
            [first.Id] = first,
            [second.Id] = second
        };
        var repository = CreateRepositorySubstitute(stored);
        var batchRepository = (IBatchRepository<BatchEntity, Guid>)repository;
        var individualPatchCalls = 0;
        var afterPersistCalls = 0;

        ConfigureBulkRead(batchRepository, stored);
        batchRepository.PatchManyAsync(
                Arg.Any<IReadOnlyList<(Guid Id, JsonElement PatchDocument)>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<BatchEntity>>(
                [new BatchEntity { Id = second.Id, Name = second.Name, Price = 22m }]));
        repository.PatchAsync(Arg.Any<Guid>(), Arg.Any<JsonElement>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                individualPatchCalls++;
                return Task.FromResult<BatchEntity?>(stored[callInfo.ArgAt<Guid>(0)]);
            });

        await CreateUnmappedHostAsync(
            repository,
            batchRepository,
            BatchAction.Patch,
            hooks => hooks.AfterPersist = _ =>
            {
                afterPersistCalls++;
                return Task.CompletedTask;
            });
        var payload = new
        {
            action = "patch",
            items = new object[]
            {
                new { id = first.Id, body = new { price = 11m } },
                new { id = second.Id, body = new { price = 22m } }
            }
        };

        // Act
        var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.MultiStatus);
        var items = await ReadItemsAsync(response);
        AssertStatuses(items, 404, 200);
        items[1].GetProperty("entity").GetProperty("id").GetGuid().Should().Be(second.Id);
        items[1].GetProperty("entity").GetProperty("price").GetDecimal().Should().Be(22m);
        afterPersistCalls.Should().Be(1);
        individualPatchCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(MalformedKeyedResult.Reordered)]
    [InlineData(MalformedKeyedResult.Extra)]
    [InlineData(MalformedKeyedResult.Duplicate)]
    public async Task BatchUpdate_BulkResultViolatesKeyedContract_ReturnsPerItem500WithoutHooksOrRetry(
        MalformedKeyedResult malformedResult)
    {
        // Arrange
        var first = new BatchEntity { Id = Guid.NewGuid(), Name = "First", Price = 1m };
        var second = new BatchEntity { Id = Guid.NewGuid(), Name = "Second", Price = 2m };
        var stored = new Dictionary<Guid, BatchEntity>
        {
            [first.Id] = first,
            [second.Id] = second
        };
        var repository = CreateRepositorySubstitute(stored);
        var batchRepository = (IBatchRepository<BatchEntity, Guid>)repository;
        var individualUpdateCalls = 0;
        var afterPersistCalls = 0;

        ConfigureBulkRead(batchRepository, stored);
        batchRepository.UpdateManyAsync(
                Arg.Any<IReadOnlyList<BatchEntity>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var input = callInfo.ArgAt<IReadOnlyList<BatchEntity>>(0);
                IReadOnlyList<BatchEntity> result = malformedResult switch
                {
                    MalformedKeyedResult.Reordered => [input[1], input[0]],
                    MalformedKeyedResult.Extra =>
                    [
                        input[0],
                        input[1],
                        new BatchEntity { Id = Guid.NewGuid(), Name = "Unexpected", Price = 99m }
                    ],
                    MalformedKeyedResult.Duplicate => [input[0], input[0]],
                    _ => throw new InvalidOperationException($"Unknown malformed result: {malformedResult}")
                };

                return Task.FromResult(result);
            });
        repository.UpdateAsync(Arg.Any<Guid>(), Arg.Any<BatchEntity>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                individualUpdateCalls++;
                return Task.FromResult<BatchEntity?>(callInfo.ArgAt<BatchEntity>(1));
            });

        await CreateUnmappedHostAsync(
            repository,
            batchRepository,
            BatchAction.Update,
            hooks => hooks.AfterPersist = _ =>
            {
                afterPersistCalls++;
                return Task.CompletedTask;
            });

        // Act
        var response = await _client!.PostAsync(
            "/api/items/batch",
            BatchJson(UpdatePayload(first.Id, second.Id)));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.MultiStatus);
        var items = await ReadItemsAsync(response);
        AssertStatuses(items, 500, 500);
        afterPersistCalls.Should().Be(0);
        individualUpdateCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task BatchDelete_BulkCountDoesNotMatchDistinctExistingKeys_ReturnsPerItem500WithoutHooksOrRetry(
        int deletedCount)
    {
        // Arrange
        var first = new BatchEntity { Id = Guid.NewGuid(), Name = "First", Price = 1m };
        var second = new BatchEntity { Id = Guid.NewGuid(), Name = "Second", Price = 2m };
        var stored = new Dictionary<Guid, BatchEntity>
        {
            [first.Id] = first,
            [second.Id] = second
        };
        var repository = CreateRepositorySubstitute(stored);
        var batchRepository = (IBatchRepository<BatchEntity, Guid>)repository;
        var individualDeleteCalls = 0;
        var afterPersistCalls = 0;

        ConfigureBulkRead(batchRepository, stored);
        batchRepository.DeleteManyAsync(
                Arg.Any<IReadOnlyList<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(deletedCount));
        repository.DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                individualDeleteCalls++;
                return Task.FromResult(true);
            });

        await CreateUnmappedHostAsync(
            repository,
            batchRepository,
            BatchAction.Delete,
            hooks => hooks.AfterPersist = _ =>
            {
                afterPersistCalls++;
                return Task.CompletedTask;
            });
        var payload = new { action = "delete", items = new[] { first.Id, second.Id } };

        // Act
        var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.MultiStatus);
        var items = await ReadItemsAsync(response);
        AssertStatuses(items, 500, 500);
        afterPersistCalls.Should().Be(0);
        individualDeleteCalls.Should().Be(0);
    }

    [Fact]
    public async Task BatchDelete_RepeatedExistingKeyCountsOneDeletionAndPreservesBothResponseSlots()
    {
        // Arrange
        var entity = new BatchEntity { Id = Guid.NewGuid(), Name = "Existing", Price = 1m };
        var stored = new Dictionary<Guid, BatchEntity> { [entity.Id] = entity };
        var repository = CreateRepositorySubstitute(stored);
        var batchRepository = (IBatchRepository<BatchEntity, Guid>)repository;
        var afterPersistCalls = 0;

        ConfigureBulkRead(batchRepository, stored);
        batchRepository.DeleteManyAsync(
                Arg.Any<IReadOnlyList<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(1));

        await CreateUnmappedHostAsync(
            repository,
            batchRepository,
            BatchAction.Delete,
            hooks => hooks.AfterPersist = _ =>
            {
                afterPersistCalls++;
                return Task.CompletedTask;
            });
        var payload = new { action = "delete", items = new[] { entity.Id, entity.Id } };

        // Act
        var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await ReadItemsAsync(response);
        AssertStatuses(items, 204, 204);
        afterPersistCalls.Should().Be(2);
        await repository.DidNotReceive()
            .DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MappedBatchCreate_ExplicitKeyResultsAreReordered_ReturnsPerItem500WithoutHooksOrRetry()
    {
        // Arrange
        var repository = CreateMappedRepositorySubstitute(
            new Dictionary<Guid, MappedDbEntity>());
        var batchRepository = (IBatchRepository<MappedDbEntity, Guid>)repository;
        var afterPersistCalls = 0;

        batchRepository.CreateManyAsync(
                Arg.Any<IReadOnlyList<MappedDbEntity>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult<IReadOnlyList<MappedDbEntity>>(
                callInfo.ArgAt<IReadOnlyList<MappedDbEntity>>(0).Reverse().ToList()));

        await CreateMappedHostAsync(
            repository,
            batchRepository,
            hooks => hooks.AfterPersist = _ =>
            {
                afterPersistCalls++;
                return Task.CompletedTask;
            },
            BatchAction.Create);
        var payload = new
        {
            action = "create",
            items = new[]
            {
                new { id = Guid.NewGuid(), name = "First", price = 1m },
                new { id = Guid.NewGuid(), name = "Second", price = 2m }
            }
        };

        // Act
        var response = await _client!.PostAsync("/api/mapped-items/batch", BatchJson(payload));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.MultiStatus);
        var items = await ReadItemsAsync(response);
        AssertStatuses(items, 500, 500);
        afterPersistCalls.Should().Be(0);
        await repository.DidNotReceive()
            .CreateAsync(Arg.Any<MappedDbEntity>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MappedBatchUpdate_BulkOmitsFirstResult_AssociatesRemainingApiEntityWithOriginalIndex()
    {
        // Arrange
        var first = new MappedDbEntity { Id = Guid.NewGuid(), Name = "First", InternalValue = "db-1" };
        var second = new MappedDbEntity { Id = Guid.NewGuid(), Name = "Second", InternalValue = "db-2" };
        var stored = new Dictionary<Guid, MappedDbEntity>
        {
            [first.Id] = first,
            [second.Id] = second
        };
        var repository = CreateMappedRepositorySubstitute(stored);
        var batchRepository = (IBatchRepository<MappedDbEntity, Guid>)repository;
        var individualUpdateCalls = 0;
        var afterPersistCalls = 0;

        ConfigureMappedBulkRead(batchRepository, stored);
        batchRepository.UpdateManyAsync(
                Arg.Any<IReadOnlyList<MappedDbEntity>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult<IReadOnlyList<MappedDbEntity>>(
                [callInfo.ArgAt<IReadOnlyList<MappedDbEntity>>(0)[1]]));
        repository.UpdateAsync(Arg.Any<Guid>(), Arg.Any<MappedDbEntity>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                individualUpdateCalls++;
                return Task.FromResult<MappedDbEntity?>(callInfo.ArgAt<MappedDbEntity>(1));
            });

        await CreateMappedHostAsync(
            repository,
            batchRepository,
            hooks => hooks.AfterPersist = _ =>
            {
                afterPersistCalls++;
                return Task.CompletedTask;
            });

        // Act
        var response = await _client!.PostAsync(
            "/api/mapped-items/batch",
            BatchJson(UpdatePayload(first.Id, second.Id)));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.MultiStatus);
        var items = await ReadItemsAsync(response);
        AssertStatuses(items, 404, 200);
        var entity = items[1].GetProperty("entity");
        entity.GetProperty("id").GetGuid().Should().Be(second.Id);
        entity.GetProperty("name").GetString().Should().Be("Updated second");
        entity.TryGetProperty("internal_value", out _).Should().BeFalse();
        afterPersistCalls.Should().Be(1);
        individualUpdateCalls.Should().Be(0);
    }

    [Fact]
    public async Task MappedBatchUpdate_BulkReordersResults_ReturnsPerItem500WithoutHooksOrRetry()
    {
        // Arrange
        var first = new MappedDbEntity { Id = Guid.NewGuid(), Name = "First", InternalValue = "db-1" };
        var second = new MappedDbEntity { Id = Guid.NewGuid(), Name = "Second", InternalValue = "db-2" };
        var stored = new Dictionary<Guid, MappedDbEntity>
        {
            [first.Id] = first,
            [second.Id] = second
        };
        var repository = CreateMappedRepositorySubstitute(stored);
        var batchRepository = (IBatchRepository<MappedDbEntity, Guid>)repository;
        var individualUpdateCalls = 0;
        var afterPersistCalls = 0;

        ConfigureMappedBulkRead(batchRepository, stored);
        batchRepository.UpdateManyAsync(
                Arg.Any<IReadOnlyList<MappedDbEntity>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult<IReadOnlyList<MappedDbEntity>>(
                callInfo.ArgAt<IReadOnlyList<MappedDbEntity>>(0).Reverse().ToList()));
        repository.UpdateAsync(Arg.Any<Guid>(), Arg.Any<MappedDbEntity>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                individualUpdateCalls++;
                return Task.FromResult<MappedDbEntity?>(callInfo.ArgAt<MappedDbEntity>(1));
            });

        await CreateMappedHostAsync(
            repository,
            batchRepository,
            hooks => hooks.AfterPersist = _ =>
            {
                afterPersistCalls++;
                return Task.CompletedTask;
            });

        // Act
        var response = await _client!.PostAsync(
            "/api/mapped-items/batch",
            BatchJson(UpdatePayload(first.Id, second.Id)));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.MultiStatus);
        var items = await ReadItemsAsync(response);
        AssertStatuses(items, 500, 500);
        afterPersistCalls.Should().Be(0);
        individualUpdateCalls.Should().Be(0);
    }

    [Fact]
    public async Task MappedBatchPatch_BulkOmitsFirstResult_AssociatesRemainingApiEntityWithOriginalIndex()
    {
        // Arrange
        var first = new MappedDbEntity { Id = Guid.NewGuid(), Name = "First", InternalValue = "db-1" };
        var second = new MappedDbEntity { Id = Guid.NewGuid(), Name = "Second", InternalValue = "db-2" };
        var stored = new Dictionary<Guid, MappedDbEntity>
        {
            [first.Id] = first,
            [second.Id] = second
        };
        var repository = CreateMappedRepositorySubstitute(stored);
        var batchRepository = (IBatchRepository<MappedDbEntity, Guid>)repository;
        var afterPersistCalls = 0;

        ConfigureMappedBulkRead(batchRepository, stored);
        batchRepository.UpdateManyAsync(
                Arg.Any<IReadOnlyList<MappedDbEntity>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult<IReadOnlyList<MappedDbEntity>>(
                [callInfo.ArgAt<IReadOnlyList<MappedDbEntity>>(0)[1]]));

        await CreateMappedHostAsync(
            repository,
            batchRepository,
            hooks => hooks.AfterPersist = _ =>
            {
                afterPersistCalls++;
                return Task.CompletedTask;
            },
            BatchAction.Patch);
        var payload = new
        {
            action = "patch",
            items = new object[]
            {
                new { id = first.Id, body = new { price = 11m } },
                new { id = second.Id, body = new { price = 22m } }
            }
        };

        // Act
        var response = await _client!.PostAsync("/api/mapped-items/batch", BatchJson(payload));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.MultiStatus);
        var items = await ReadItemsAsync(response);
        AssertStatuses(items, 404, 200);
        var entity = items[1].GetProperty("entity");
        entity.GetProperty("id").GetGuid().Should().Be(second.Id);
        entity.GetProperty("price").GetDecimal().Should().Be(22m);
        entity.TryGetProperty("internal_value", out _).Should().BeFalse();
        afterPersistCalls.Should().Be(1);
        await repository.DidNotReceive()
            .UpdateAsync(Arg.Any<Guid>(), Arg.Any<MappedDbEntity>(), Arg.Any<CancellationToken>());
    }

    private static IRepository<BatchEntity, Guid> CreateRepositorySubstitute(
        IReadOnlyDictionary<Guid, BatchEntity>? stored = null)
    {
        var repository = Substitute.For<
            IRepository<BatchEntity, Guid>,
            IBatchRepository<BatchEntity, Guid>>();

        repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult<BatchEntity?>(
                stored is not null && stored.TryGetValue(callInfo.ArgAt<Guid>(0), out var entity)
                    ? entity
                    : null));

        return repository;
    }

    private static IRepository<MappedDbEntity, Guid> CreateMappedRepositorySubstitute(
        IReadOnlyDictionary<Guid, MappedDbEntity> stored)
    {
        var repository = Substitute.For<
            IRepository<MappedDbEntity, Guid>,
            IBatchRepository<MappedDbEntity, Guid>>();

        repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult<MappedDbEntity?>(
                stored.TryGetValue(callInfo.ArgAt<Guid>(0), out var entity) ? entity : null));

        return repository;
    }

    private static void ConfigureBulkRead(
        IBatchRepository<BatchEntity, Guid> batchRepository,
        IReadOnlyDictionary<Guid, BatchEntity> stored)
    {
        batchRepository.GetByIdsAsync(
                Arg.Any<IReadOnlyList<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var ids = callInfo.ArgAt<IReadOnlyList<Guid>>(0);
                IReadOnlyDictionary<Guid, BatchEntity> found = ids
                    .Distinct()
                    .Where(stored.ContainsKey)
                    .ToDictionary(id => id, id => stored[id]);
                return Task.FromResult(found);
            });
    }

    private static void ConfigureMappedBulkRead(
        IBatchRepository<MappedDbEntity, Guid> batchRepository,
        IReadOnlyDictionary<Guid, MappedDbEntity> stored)
    {
        batchRepository.GetByIdsAsync(
                Arg.Any<IReadOnlyList<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var ids = callInfo.ArgAt<IReadOnlyList<Guid>>(0);
                IReadOnlyDictionary<Guid, MappedDbEntity> found = ids
                    .Distinct()
                    .Where(stored.ContainsKey)
                    .ToDictionary(id => id, id => stored[id]);
                return Task.FromResult(found);
            });
    }

    private static object UpdatePayload(Guid firstId, Guid secondId)
    {
        return new
        {
            action = "update",
            items = new object[]
            {
                new { id = firstId, body = new { name = "Updated first", price = 11m } },
                new { id = secondId, body = new { name = "Updated second", price = 22m } }
            }
        };
    }

    private static BatchEntity Copy(BatchEntity source, Guid id)
    {
        return new BatchEntity
        {
            Id = id,
            Name = source.Name,
            Price = source.Price,
            IsActive = source.IsActive
        };
    }

    private static StringContent BatchJson(object payload)
    {
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        return new StringContent(json, Encoding.UTF8, "application/json");
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
            .Select(item => item.GetProperty("index").GetInt32())
            .Should().Equal(Enumerable.Range(0, statuses.Length));
        items.EnumerateArray()
            .Select(item => item.GetProperty("status").GetInt32())
            .Should().Equal(statuses);
    }

    private async Task CreateUnmappedHostAsync(
        IRepository<BatchEntity, Guid> repository,
        IBatchRepository<BatchEntity, Guid> batchRepository,
        BatchAction action,
        Action<RestLibHooks<BatchEntity, Guid>> configureHooks)
    {
        (_host, _client) = await new TestHostBuilder<BatchEntity, Guid>(repository, "/api/items")
            .WithServices(services =>
                services.AddSingleton(batchRepository))
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.EnableBatch(action);
                config.UseHooks(configureHooks);
            })
            .BuildAsync();
    }

    private async Task CreateMappedHostAsync(
        IRepository<MappedDbEntity, Guid> repository,
        IBatchRepository<MappedDbEntity, Guid> batchRepository,
        Action<RestLibHooks<MappedApiEntity, Guid>> configureHooks,
        BatchAction action = BatchAction.Update)
    {
        (_host, _client) = await new TestTwoModelHostBuilder<MappedApiEntity, MappedDbEntity, Guid>(
            repository,
            "/api/mapped-items")
            .WithServices(services =>
            {
                services.AddRestLibMapper<MappedApiEntity, MappedDbEntity>(_ => new MappedEntityMapper());
                services.AddSingleton(batchRepository);
            })
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.EnableBatch(action);
                config.UseHooks(configureHooks);
            })
            .BuildAsync();
    }

    /// <summary>
    /// Describes invalid create result shapes returned by a custom batch repository.
    /// </summary>
    public enum MalformedCreateResult
    {
        /// <summary>The repository returns a null result list.</summary>
        NullList,

        /// <summary>The result contains fewer entities than the input.</summary>
        TooFew,

        /// <summary>The result contains more entities than the input.</summary>
        TooMany,

        /// <summary>The result contains a null entity.</summary>
        NullItem
    }

    /// <summary>
    /// Describes invalid keyed result sequences returned by a custom batch repository.
    /// </summary>
    public enum MalformedKeyedResult
    {
        /// <summary>The result contains the expected keys in the wrong order.</summary>
        Reordered,

        /// <summary>The result contains an unexpected extra key.</summary>
        Extra,

        /// <summary>The result duplicates one key and omits another.</summary>
        Duplicate
    }

    /// <summary>
    /// API model used by the mapped bulk-result association tests.
    /// </summary>
    public sealed class MappedApiEntity
    {
        /// <summary>Gets or sets the entity identifier.</summary>
        public Guid Id { get; set; }

        /// <summary>Gets or sets the entity name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets the entity price.</summary>
        public decimal Price { get; set; }
    }

    /// <summary>
    /// Persistence model used by the mapped bulk-result association tests.
    /// </summary>
    public sealed class MappedDbEntity
    {
        /// <summary>Gets or sets the entity identifier.</summary>
        public Guid Id { get; set; }

        /// <summary>Gets or sets the entity name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets the entity price.</summary>
        public decimal Price { get; set; }

        /// <summary>Gets or sets a persistence-only value.</summary>
        public string InternalValue { get; set; } = string.Empty;
    }

    private sealed class MappedEntityMapper : IRestLibMapper<MappedApiEntity, MappedDbEntity>
    {
        public MappedApiEntity ToApi(MappedDbEntity dbModel)
        {
            return new MappedApiEntity
            {
                Id = dbModel.Id,
                Name = dbModel.Name,
                Price = dbModel.Price
            };
        }

        public MappedDbEntity ToDb(MappedApiEntity apiModel)
        {
            return new MappedDbEntity
            {
                Id = apiModel.Id,
                Name = apiModel.Name,
                Price = apiModel.Price,
                InternalValue = "mapped"
            };
        }
    }
}
