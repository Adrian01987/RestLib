using System.ComponentModel.DataAnnotations;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RestLib.Abstractions;
using RestLib.Batch;
using RestLib.Configuration;
using RestLib.Hooks;
using RestLib.Mapping;
using RestLib.Pagination;
using RestLib.Tests.Fakes;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Integration tests for request-cancellation propagation.
/// </summary>
[Trait("Type", "Integration")]
[Trait("Feature", "Cancellation")]
public class CancellationTests
{
    private static readonly Guid _knownId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public async Task BulkPersistenceExecutor_DownstreamCancellationWithoutRequestCancellation_WrapsFailure()
    {
        // Arrange
        var downstreamCancellation = new OperationCanceledException("Downstream operation cancelled.");

        // Act
        var act = () => BulkPersistenceExecutor.ExecuteAsync(
            () => Task.FromException<int>(downstreamCancellation),
            CancellationToken.None);

        // Assert
        var exception = await act.Should().ThrowAsync<BulkPersistenceException>();
        exception.Which.InnerException.Should().BeSameAs(downstreamCancellation);
    }

    [Theory]
    [InlineData(nameof(CancellationOperation.Create))]
    [InlineData(nameof(CancellationOperation.GetAll))]
    [InlineData(nameof(CancellationOperation.GetById))]
    [InlineData(nameof(CancellationOperation.Update))]
    [InlineData(nameof(CancellationOperation.Patch))]
    [InlineData(nameof(CancellationOperation.Delete))]
    public async Task CrudEndpoint_RequestCancelled_PropagatesWithoutRunningErrorHook(
        string operationName)
    {
        // Arrange
        var operation = Enum.Parse<CancellationOperation>(operationName);
        var repository = new BlockingRepository(operation, _knownId);
        var errorHookCallCount = 0;
        var (host, client) = await new TestHostBuilder<CancellationEntity, Guid>(
                repository,
                "/api/cancellation-items")
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.UseHooks(hooks =>
                {
                    hooks.OnError = context =>
                    {
                        Interlocked.Increment(ref errorHookCallCount);
                        context.Handled = true;
                        context.ErrorResult = Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
                        return Task.CompletedTask;
                    };
                });
            })
            .BuildAsync();

        try
        {
            using var cancellationSource = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var request = SendCrudRequestAsync(client, operation, cancellationSource.Token);
            await repository.Started.WaitAsync(TimeSpan.FromSeconds(5));

            // Act
            cancellationSource.Cancel();
            Func<Task> act = async () => _ = await request;

            // Assert
            await act.Should().ThrowAsync<OperationCanceledException>();
            await repository.Completed.WaitAsync(TimeSpan.FromSeconds(5));
            repository.OperationCallCount.Should().Be(1);
            errorHookCallCount.Should().Be(0);
        }
        finally
        {
            client.Dispose();
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    [Trait("Category", "Story8.10")]
    public async Task BatchCreate_IndividualPersistenceCancelled_StopsBeforeNextItemAndSkipsErrorHook()
    {
        // Arrange
        var repository = new BlockingRepository(CancellationOperation.Create, _knownId);
        var errorHookCallCount = 0;
        var (host, client) = await new TestHostBuilder<CancellationEntity, Guid>(
                repository,
                "/api/cancellation-items")
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.EnableBatch(BatchAction.Create);
                config.UseHooks(hooks =>
                {
                    hooks.OnError = _ =>
                    {
                        Interlocked.Increment(ref errorHookCallCount);
                        return Task.CompletedTask;
                    };
                });
            })
            .BuildAsync();

        try
        {
            using var cancellationSource = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var request = client.PostAsync(
                "/api/cancellation-items/batch",
                BatchJson(new
                {
                    action = "create",
                    items = new[]
                    {
                        new { name = "First" },
                        new { name = "Second" }
                    }
                }),
                cancellationSource.Token);
            await repository.Started.WaitAsync(TimeSpan.FromSeconds(5));

            // Act
            cancellationSource.Cancel();
            Func<Task> act = async () => _ = await request;

            // Assert
            await act.Should().ThrowAsync<OperationCanceledException>();
            await repository.Completed.WaitAsync(TimeSpan.FromSeconds(5));
            repository.OperationCallCount.Should().Be(1);
            errorHookCallCount.Should().Be(0);
        }
        finally
        {
            client.Dispose();
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    [Trait("Category", "Story8.10")]
    public async Task BatchCreate_BulkPersistenceCancelled_PropagatesWithoutItemFailuresOrErrorHooks()
    {
        // Arrange
        var repository = new CreateTrackingRepository<CancellationEntity>();
        var batchRepository = new BlockingBatchRepository();
        var errorHookCallCount = 0;
        var (host, client) = await new TestHostBuilder<CancellationEntity, Guid>(
                repository,
                "/api/cancellation-items")
            .WithServices(services =>
                services.AddSingleton<IBatchRepository<CancellationEntity, Guid>>(batchRepository))
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.EnableBatch(BatchAction.Create);
                config.UseHooks(hooks =>
                {
                    hooks.OnError = _ =>
                    {
                        Interlocked.Increment(ref errorHookCallCount);
                        return Task.CompletedTask;
                    };
                });
            })
            .BuildAsync();

        try
        {
            using var cancellationSource = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var request = client.PostAsync(
                "/api/cancellation-items/batch",
                BatchJson(new
                {
                    action = "create",
                    items = new[]
                    {
                        new { name = "First" },
                        new { name = "Second" }
                    }
                }),
                cancellationSource.Token);
            await batchRepository.Started.WaitAsync(TimeSpan.FromSeconds(5));

            // Act
            cancellationSource.Cancel();
            Func<Task> act = async () => _ = await request;

            // Assert
            await act.Should().ThrowAsync<OperationCanceledException>();
            await batchRepository.Completed.WaitAsync(TimeSpan.FromSeconds(5));
            batchRepository.CreateManyCallCount.Should().Be(1);
            repository.CreateCallCount.Should().Be(0);
            errorHookCallCount.Should().Be(0);
        }
        finally
        {
            client.Dispose();
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    [Trait("Category", "Story8.10")]
    public async Task MappedBatchCreate_BulkPersistenceCancelled_PropagatesWithoutErrorHook()
    {
        // Arrange
        var repository = new CreateTrackingRepository<CancellationDbEntity>();
        var batchRepository = new BlockingMappedBatchRepository();
        var errorHookCallCount = 0;
        var (host, client) = await new TestTwoModelHostBuilder<CancellationApiEntity, CancellationDbEntity, Guid>(
                repository,
                "/api/mapped-cancellation-items")
            .WithServices(services =>
            {
                services.AddRestLibMapper<CancellationApiEntity, CancellationDbEntity>(
                    _ => new CancellationMapper());
                services.AddSingleton<IBatchRepository<CancellationDbEntity, Guid>>(batchRepository);
            })
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.EnableBatch(BatchAction.Create);
                config.UseHooks(hooks =>
                {
                    hooks.OnError = _ =>
                    {
                        Interlocked.Increment(ref errorHookCallCount);
                        return Task.CompletedTask;
                    };
                });
            })
            .BuildAsync();

        try
        {
            using var cancellationSource = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var request = client.PostAsync(
                "/api/mapped-cancellation-items/batch",
                BatchJson(new
                {
                    action = "create",
                    items = new[]
                    {
                        new { name = "First" },
                        new { name = "Second" }
                    }
                }),
                cancellationSource.Token);
            await batchRepository.Started.WaitAsync(TimeSpan.FromSeconds(5));

            // Act
            cancellationSource.Cancel();
            Func<Task> act = async () => _ = await request;

            // Assert
            await act.Should().ThrowAsync<OperationCanceledException>();
            await batchRepository.Completed.WaitAsync(TimeSpan.FromSeconds(5));
            batchRepository.CreateManyCallCount.Should().Be(1);
            repository.CreateCallCount.Should().Be(0);
            errorHookCallCount.Should().Be(0);
        }
        finally
        {
            client.Dispose();
            await host.StopAsync();
            host.Dispose();
        }
    }

    private static Task<HttpResponseMessage> SendCrudRequestAsync(
        HttpClient client,
        CancellationOperation operation,
        CancellationToken cancellationToken)
    {
        return operation switch
        {
            CancellationOperation.Create => client.PostAsJsonAsync(
                "/api/cancellation-items",
                new { name = "Create" },
                cancellationToken),
            CancellationOperation.GetAll => client.GetAsync(
                "/api/cancellation-items",
                cancellationToken),
            CancellationOperation.GetById => client.GetAsync(
                $"/api/cancellation-items/{_knownId}",
                cancellationToken),
            CancellationOperation.Update => client.PutAsJsonAsync(
                $"/api/cancellation-items/{_knownId}",
                new { id = _knownId, name = "Update" },
                cancellationToken),
            CancellationOperation.Patch => client.PatchAsync(
                $"/api/cancellation-items/{_knownId}",
                new StringContent("""{"name":"Patch"}""", Encoding.UTF8, "application/merge-patch+json"),
                cancellationToken),
            CancellationOperation.Delete => client.DeleteAsync(
                $"/api/cancellation-items/{_knownId}",
                cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
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

    private enum CancellationOperation
    {
        Create,
        GetAll,
        GetById,
        Update,
        Patch,
        Delete
    }

    private sealed class CancellationEntity
    {
        public Guid Id { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty;
    }

    private sealed class CancellationApiEntity
    {
        public Guid Id { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty;
    }

    private sealed class CancellationDbEntity
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class CancellationMapper : IRestLibMapper<CancellationApiEntity, CancellationDbEntity>
    {
        public CancellationApiEntity ToApi(CancellationDbEntity dbModel)
        {
            return new CancellationApiEntity { Id = dbModel.Id, Name = dbModel.Name };
        }

        public CancellationDbEntity ToDb(CancellationApiEntity apiModel)
        {
            return new CancellationDbEntity { Id = apiModel.Id, Name = apiModel.Name };
        }
    }

    private sealed class BlockingRepository : IRepository<CancellationEntity, Guid>
    {
        private readonly CancellationOperation _operation;
        private readonly CancellationEntity _existing;
        private readonly TaskCompletionSource<bool> _started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _completed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _operationCallCount;

        public BlockingRepository(CancellationOperation operation, Guid knownId)
        {
            _operation = operation;
            _existing = new CancellationEntity { Id = knownId, Name = "Existing" };
        }

        public Task Started => _started.Task;

        public Task Completed => _completed.Task;

        public int OperationCallCount => _operationCallCount;

        public Task<CancellationEntity> CreateAsync(
            CancellationEntity entity,
            CancellationToken ct = default)
        {
            return BlockAsync(entity, ct);
        }

        public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
        {
            return BlockAsync(true, ct);
        }

        public Task<PagedResult<CancellationEntity>> GetAllAsync(
            PaginationRequest pagination,
            CancellationToken ct = default)
        {
            return BlockAsync(
                new PagedResult<CancellationEntity> { Items = [_existing] },
                ct);
        }

        public Task<CancellationEntity?> GetByIdAsync(Guid id, CancellationToken ct = default)
        {
            return _operation == CancellationOperation.GetById
                ? BlockAsync<CancellationEntity?>(_existing, ct)
                : Task.FromResult<CancellationEntity?>(_existing);
        }

        public Task<CancellationEntity?> PatchAsync(
            Guid id,
            JsonElement patchDocument,
            CancellationToken ct = default)
        {
            return BlockAsync<CancellationEntity?>(_existing, ct);
        }

        public Task<CancellationEntity?> UpdateAsync(
            Guid id,
            CancellationEntity entity,
            CancellationToken ct = default)
        {
            return BlockAsync<CancellationEntity?>(entity, ct);
        }

        private async Task<TResult> BlockAsync<TResult>(TResult result, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _operationCallCount);
            _started.TrySetResult(true);

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return result;
            }
            finally
            {
                _completed.TrySetResult(true);
            }
        }
    }

    private sealed class BlockingBatchRepository : IBatchRepository<CancellationEntity, Guid>
    {
        private readonly TaskCompletionSource<bool> _started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _completed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _createManyCallCount;

        public Task Started => _started.Task;

        public Task Completed => _completed.Task;

        public int CreateManyCallCount => _createManyCallCount;

        public async Task<IReadOnlyList<CancellationEntity>> CreateManyAsync(
            IReadOnlyList<CancellationEntity> entities,
            CancellationToken ct = default)
        {
            Interlocked.Increment(ref _createManyCallCount);
            _started.TrySetResult(true);

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return entities;
            }
            finally
            {
                _completed.TrySetResult(true);
            }
        }

        public Task<int> DeleteManyAsync(IReadOnlyList<Guid> keys, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyDictionary<Guid, CancellationEntity>> GetByIdsAsync(
            IReadOnlyList<Guid> ids,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<CancellationEntity>> PatchManyAsync(
            IReadOnlyList<(Guid Id, JsonElement PatchDocument)> patches,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<CancellationEntity>> UpdateManyAsync(
            IReadOnlyList<CancellationEntity> entities,
            CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class CreateTrackingRepository<TEntity> : IRepository<TEntity, Guid>
        where TEntity : class
    {
        private int _createCallCount;

        public int CreateCallCount => _createCallCount;

        public Task<TEntity> CreateAsync(TEntity entity, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _createCallCount);
            return Task.FromResult(entity);
        }

        public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<PagedResult<TEntity>> GetAllAsync(
            PaginationRequest pagination,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<TEntity?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<TEntity?> PatchAsync(
            Guid id,
            JsonElement patchDocument,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<TEntity?> UpdateAsync(
            Guid id,
            TEntity entity,
            CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class BlockingMappedBatchRepository : IBatchRepository<CancellationDbEntity, Guid>
    {
        private readonly TaskCompletionSource<bool> _started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _completed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _createManyCallCount;

        public Task Started => _started.Task;

        public Task Completed => _completed.Task;

        public int CreateManyCallCount => _createManyCallCount;

        public async Task<IReadOnlyList<CancellationDbEntity>> CreateManyAsync(
            IReadOnlyList<CancellationDbEntity> entities,
            CancellationToken ct = default)
        {
            Interlocked.Increment(ref _createManyCallCount);
            _started.TrySetResult(true);

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return entities;
            }
            finally
            {
                _completed.TrySetResult(true);
            }
        }

        public Task<int> DeleteManyAsync(IReadOnlyList<Guid> keys, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyDictionary<Guid, CancellationDbEntity>> GetByIdsAsync(
            IReadOnlyList<Guid> ids,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<CancellationDbEntity>> PatchManyAsync(
            IReadOnlyList<(Guid Id, JsonElement PatchDocument)> patches,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<CancellationDbEntity>> UpdateManyAsync(
            IReadOnlyList<CancellationDbEntity> entities,
            CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
