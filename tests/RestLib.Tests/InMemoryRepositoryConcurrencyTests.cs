using System.Text.Json;
using FluentAssertions;
using RestLib.Filtering;
using RestLib.InMemory;
using RestLib.Pagination;
using Xunit;

namespace RestLib.Tests;

public partial class InMemoryRepositoryTests
{
    private sealed record CoordinatedEntity(CoordinatedKey Id);

    private sealed class CoordinatedKey(int value, Action? onThirdHash = null) :
        IComparable<CoordinatedKey>,
        IEquatable<CoordinatedKey>
    {
        private int _hashCalls;

        public int Value { get; } = value;

        public int CompareTo(CoordinatedKey? other) => other is null ? 1 : Value.CompareTo(other.Value);

        public bool Equals(CoordinatedKey? other) => other is not null && Value == other.Value;

        public override bool Equals(object? obj) => Equals(obj as CoordinatedKey);

        public override int GetHashCode()
        {
            if (Interlocked.Increment(ref _hashCalls) == 3)
            {
                onThirdHash?.Invoke();
            }

            return Value;
        }
    }

    #region Thread Safety Tests

    [Fact]
    public async Task CreateAsync_ConcurrentUniqueKeys_PersistsEveryEntity()
    {
        // Arrange
        var repository = CreateRepository();
        var entities = Enumerable.Range(0, 100)
            .Select(index => CreateEntity($"Entity{index}", index))
            .ToArray();
        var startGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = entities.Select(entity => Task.Run(async () =>
        {
            await startGate.Task;
            return await repository.CreateAsync(entity);
        })).ToArray();

        // Act
        startGate.SetResult(true);
        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().Equal(entities);
        repository.Count.Should().Be(100);
        foreach (var entity in entities)
        {
            (await repository.GetByIdAsync(entity.Id)).Should().BeEquivalentTo(entity);
        }
    }

    [Fact]
    public async Task GetAllAsync_ConcurrentCreates_CompletesAndPreservesEveryWrite()
    {
        // Arrange
        var repository = CreateRepository();
        var initialEntities = Enumerable.Range(0, 50)
            .Select(index => CreateEntity($"Initial{index}", index))
            .ToArray();
        var createdEntities = Enumerable.Range(0, 50)
            .Select(index => CreateEntity($"Concurrent{index}", index + 50))
            .ToArray();
        await repository.CreateManyAsync(initialEntities);
        var startGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = new List<Task>();

        for (var index = 0; index < 50; index++)
        {
            var entity = createdEntities[index];
            tasks.Add(Task.Run(async () =>
            {
                await startGate.Task;
                await repository.GetAllAsync(new PaginationRequest { Limit = 10 });
            }));
            tasks.Add(Task.Run(async () =>
            {
                await startGate.Task;
                await repository.CreateAsync(entity);
            }));
        }

        // Act
        startGate.SetResult(true);
        var act = () => Task.WhenAll(tasks);

        // Assert
        await act.Should().NotThrowAsync();
        repository.Count.Should().Be(100);
        foreach (var entity in initialEntities.Concat(createdEntities))
        {
            (await repository.GetByIdAsync(entity.Id)).Should().BeEquivalentTo(entity);
        }
    }

    [Fact]
    public async Task UpdateAsync_ConcurrentReplacements_PersistsOneCompleteSubmittedEntity()
    {
        // Arrange
        var repository = CreateRepository();
        var entity = CreateEntity("Original", 100);
        await repository.CreateAsync(entity);
        var replacements = Enumerable.Range(0, 100)
            .Select(index => entity with { Name = $"Replacement{index}", Value = index })
            .ToArray();
        var startGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = replacements.Select(replacement => Task.Run(async () =>
        {
            await startGate.Task;
            return await repository.UpdateAsync(entity.Id, replacement);
        })).ToArray();

        // Act
        startGate.SetResult(true);
        var updateResults = await Task.WhenAll(tasks);

        // Assert
        updateResults.Should().OnlyContain(result => result != null);
        var result = await repository.GetByIdAsync(entity.Id);
        result.Should().NotBeNull();
        replacements.Should().Contain(result!);
    }

    [Fact]
    public async Task DeleteAsync_ConcurrentCalls_ExactlyOneSucceeds()
    {
        // Arrange
        var repository = CreateRepository();
        var entity = CreateEntity();
        await repository.CreateAsync(entity);
        var startGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
        {
            await startGate.Task;
            return await repository.DeleteAsync(entity.Id);
        })).ToArray();

        // Act
        startGate.SetResult(true);
        var results = await Task.WhenAll(tasks);

        // Assert
        results.Count(r => r).Should().Be(1);
        repository.Count.Should().Be(0);
        (await repository.GetByIdAsync(entity.Id)).Should().BeNull();
    }

    [Fact]
    public async Task UpdateAndDeleteAsync_ConcurrentCalls_DeletePreventsReinsertion()
    {
        // Arrange
        var repository = CreateRepository();
        var entity = CreateEntity("Target", 42);
        await repository.CreateAsync(entity);
        var deleteResults = new List<bool>();
        var resultLock = new object();
        var startGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = new List<Task>();
        for (var value = 0; value < 100; value++)
        {
            if (value % 2 == 0)
            {
                tasks.Add(Task.Run(async () =>
                {
                    await startGate.Task;
                    var updated = entity with { Value = value };
                    await repository.UpdateAsync(entity.Id, updated);
                }));
            }
            else
            {
                tasks.Add(Task.Run(async () =>
                {
                    await startGate.Task;
                    var deleted = await repository.DeleteAsync(entity.Id);
                    lock (resultLock)
                    {
                        deleteResults.Add(deleted);
                    }
                }));
            }
        }

        // Act
        startGate.SetResult(true);
        await Task.WhenAll(tasks);

        // Assert
        deleteResults.Count(result => result).Should().Be(1);
        repository.Count.Should().Be(0);
        (await repository.GetByIdAsync(entity.Id)).Should().BeNull();
    }

    [Fact]
    public async Task PatchAsync_ConcurrentDisjointPatches_PreservesBothChanges()
    {
        // Arrange
        var repository = CreateRepository();
        var entity = CreateEntity("PatchTarget", 0);
        await repository.CreateAsync(entity);
        var namePatch = JsonSerializer.SerializeToElement(new { Name = "Patched name" });
        var valuePatch = JsonSerializer.SerializeToElement(new { Value = 42 });
        var startGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var patches = new[] { namePatch, valuePatch };
        var tasks = patches.Select(patch => Task.Run(async () =>
        {
            await startGate.Task;
            return await repository.PatchAsync(entity.Id, patch);
        })).ToArray();

        // Act
        startGate.SetResult(true);
        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().OnlyContain(result => result != null);
        var result = await repository.GetByIdAsync(entity.Id);
        result.Should().NotBeNull();
        result!.Name.Should().Be("Patched name");
        result.Value.Should().Be(42);
        result.Id.Should().Be(entity.Id);
        result.CreatedAt.Should().Be(entity.CreatedAt);
    }

    [Fact]
    public async Task MembershipReads_BatchCommitInProgress_ObserveCompleteBatch()
    {
        // Arrange
        using var releaseCommit = new ManualResetEventSlim();
        var commitPaused = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstKey = new CoordinatedKey(1);
        var secondKey = new CoordinatedKey(2, () =>
        {
            commitPaused.TrySetResult(true);
            releaseCommit.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        });
        var repository = new InMemoryRepository<CoordinatedEntity, CoordinatedKey>(
            entity => entity.Id,
            () => throw new InvalidOperationException("A generated key is not expected."));
        var batchTask = Task.Run(() => repository.CreateManyAsync(
            [new CoordinatedEntity(firstKey), new CoordinatedEntity(secondKey)]));
        await commitPaused.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var countStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var collectionStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var countTask = Task.Factory.StartNew(
            () =>
            {
                countStarted.SetResult(true);
                return repository.Count;
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        var collectionTask = Task.Factory.StartNew(
            () =>
            {
                collectionStarted.SetResult(true);
                return repository.GetAllAsync(new PaginationRequest { Limit = 10 });
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).Unwrap();
        await Task.WhenAll(countStarted.Task, collectionStarted.Task);
        await Task.Delay(TimeSpan.FromMilliseconds(200));

        // Act
        var countCompletedDuringCommit = countTask.IsCompleted;
        var collectionCompletedDuringCommit = collectionTask.IsCompleted;
        releaseCommit.Set();
        var observedCount = await countTask;
        var observedPage = await collectionTask;
        await batchTask;

        // Assert
        countCompletedDuringCommit.Should().BeFalse();
        collectionCompletedDuringCommit.Should().BeFalse();
        observedCount.Should().Be(2);
        observedPage.Items.Should().HaveCount(2);
        repository.Count.Should().Be(2);
    }

    #endregion
}
