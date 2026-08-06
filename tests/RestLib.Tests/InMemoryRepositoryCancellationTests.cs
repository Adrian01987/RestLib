using System.Collections;
using System.Text.Json;
using FluentAssertions;
using RestLib.Filtering;
using RestLib.InMemory;
using RestLib.Pagination;
using Xunit;

namespace RestLib.Tests;

[Trait("Type", "Unit")]
[Trait("Feature", "Repository")]
public class InMemoryRepositoryCancellationTests
{
    public enum CancellableOperation
    {
        GetById,
        GetAll,
        Create,
        Update,
        Patch,
        Delete,
        ConditionalUpdate,
        ConditionalPatch,
        ConditionalDelete,
        CreateMany,
        UpdateMany,
        PatchMany,
        DeleteMany,
        GetByIds,
        CountFilters,
        CountQuery
    }

    public enum LoopOperation
    {
        CreateMany,
        UpdateMany,
        PatchMany,
        DeleteMany,
        GetByIds
    }

    public enum ConditionalOperation
    {
        Update,
        Patch,
        Delete
    }

    private sealed record TestEntity(Guid Id, string Name, int Value);

    private sealed class MutableEntity
    {
        public Guid Id { get; init; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class CancellingReadOnlyList<T>(
        IReadOnlyList<T> items,
        CancellationTokenSource cancellationTokenSource,
        int cancelBeforeIndex) : IReadOnlyList<T>
    {
        public int Count => items.Count;

        public T this[int index] => items[index];

        public IEnumerator<T> GetEnumerator()
        {
            for (var index = 0; index < items.Count; index++)
            {
                if (index == cancelBeforeIndex)
                {
                    cancellationTokenSource.Cancel();
                }

                yield return items[index];
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Theory]
    [InlineData(CancellableOperation.GetById)]
    [InlineData(CancellableOperation.GetAll)]
    [InlineData(CancellableOperation.Create)]
    [InlineData(CancellableOperation.Update)]
    [InlineData(CancellableOperation.Patch)]
    [InlineData(CancellableOperation.Delete)]
    [InlineData(CancellableOperation.ConditionalUpdate)]
    [InlineData(CancellableOperation.ConditionalPatch)]
    [InlineData(CancellableOperation.ConditionalDelete)]
    [InlineData(CancellableOperation.CreateMany)]
    [InlineData(CancellableOperation.UpdateMany)]
    [InlineData(CancellableOperation.PatchMany)]
    [InlineData(CancellableOperation.DeleteMany)]
    [InlineData(CancellableOperation.GetByIds)]
    [InlineData(CancellableOperation.CountFilters)]
    [InlineData(CancellableOperation.CountQuery)]
    public async Task RepositoryOperation_PreCanceledToken_ThrowsWithoutChangingStore(
        CancellableOperation operation)
    {
        // Arrange
        var generatorCalls = 0;
        var preconditionCalls = 0;
        var repository = new InMemoryRepository<TestEntity, Guid>(
            entity => entity.Id,
            () =>
            {
                generatorCalls++;
                return Guid.NewGuid();
            });
        var original = new TestEntity(Guid.NewGuid(), "Original", 1);
        var replacement = original with { Name = "Replacement", Value = 2 };
        var createInput = new TestEntity(Guid.Empty, "Created", 3);
        var patch = JsonSerializer.SerializeToElement(new { Name = "Patched", Value = 4 });
        await repository.CreateAsync(original);
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        bool Precondition(TestEntity entity)
        {
            _ = entity;
            preconditionCalls++;
            return true;
        }

        Func<Task> act = operation switch
        {
            CancellableOperation.GetById => () => repository.GetByIdAsync(
                original.Id,
                cancellationTokenSource.Token),
            CancellableOperation.GetAll => () => repository.GetAllAsync(
                new PaginationRequest { Limit = 10 },
                cancellationTokenSource.Token),
            CancellableOperation.Create => () => repository.CreateAsync(
                createInput,
                cancellationTokenSource.Token),
            CancellableOperation.Update => () => repository.UpdateAsync(
                original.Id,
                replacement,
                cancellationTokenSource.Token),
            CancellableOperation.Patch => () => repository.PatchAsync(
                original.Id,
                patch,
                cancellationTokenSource.Token),
            CancellableOperation.Delete => () => repository.DeleteAsync(
                original.Id,
                cancellationTokenSource.Token),
            CancellableOperation.ConditionalUpdate => () => repository.UpdateConditionallyAsync(
                original.Id,
                replacement,
                Precondition,
                cancellationTokenSource.Token),
            CancellableOperation.ConditionalPatch => () => repository.PatchConditionallyAsync(
                original.Id,
                patch,
                Precondition,
                cancellationTokenSource.Token),
            CancellableOperation.ConditionalDelete => () => repository.DeleteConditionallyAsync(
                original.Id,
                Precondition,
                cancellationTokenSource.Token),
            CancellableOperation.CreateMany => () => repository.CreateManyAsync(
                [createInput],
                cancellationTokenSource.Token),
            CancellableOperation.UpdateMany => () => repository.UpdateManyAsync(
                [replacement],
                cancellationTokenSource.Token),
            CancellableOperation.PatchMany => () => repository.PatchManyAsync(
                [(original.Id, patch)],
                cancellationTokenSource.Token),
            CancellableOperation.DeleteMany => () => repository.DeleteManyAsync(
                [original.Id],
                cancellationTokenSource.Token),
            CancellableOperation.GetByIds => () => repository.GetByIdsAsync(
                [original.Id],
                cancellationTokenSource.Token),
            CancellableOperation.CountFilters => () => repository.CountAsync(
                Array.Empty<FilterValue>(),
                cancellationTokenSource.Token),
            CancellableOperation.CountQuery => () => repository.CountAsync(
                new PaginationRequest { Limit = 10 },
                cancellationTokenSource.Token),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown operation.")
        };

        // Act
        var exception = await act.Should().ThrowAsync<OperationCanceledException>();

        // Assert
        exception.Which.CancellationToken.Should().Be(cancellationTokenSource.Token);
        generatorCalls.Should().Be(0);
        preconditionCalls.Should().Be(0);
        repository.Count.Should().Be(1);
        (await repository.GetByIdAsync(original.Id)).Should().BeEquivalentTo(original);
    }

    [Theory]
    [InlineData(LoopOperation.CreateMany)]
    [InlineData(LoopOperation.UpdateMany)]
    [InlineData(LoopOperation.PatchMany)]
    [InlineData(LoopOperation.DeleteMany)]
    [InlineData(LoopOperation.GetByIds)]
    public async Task LoopOperation_CancellationDuringPlanning_ThrowsWithoutPartialPersistence(
        LoopOperation operation)
    {
        // Arrange
        var repository = CreateRepository();
        var first = CreateEntity("First", 1);
        var second = CreateEntity("Second", 2);
        await repository.CreateManyAsync([first, second]);
        var firstPatch = JsonSerializer.SerializeToElement(new { Name = "First patched" });
        var secondPatch = JsonSerializer.SerializeToElement(new { Name = "Second patched" });
        using var cancellationTokenSource = new CancellationTokenSource();

        Func<Task> act = operation switch
        {
            LoopOperation.CreateMany => () => repository.CreateManyAsync(
                CancelBeforeSecond(
                    [CreateEntity("Third", 3), CreateEntity("Fourth", 4)],
                    cancellationTokenSource),
                cancellationTokenSource.Token),
            LoopOperation.UpdateMany => () => repository.UpdateManyAsync(
                CancelBeforeSecond(
                    [first with { Name = "First updated" }, second with { Name = "Second updated" }],
                    cancellationTokenSource),
                cancellationTokenSource.Token),
            LoopOperation.PatchMany => () => repository.PatchManyAsync(
                CancelBeforeSecond(
                    [(first.Id, firstPatch), (second.Id, secondPatch)],
                    cancellationTokenSource),
                cancellationTokenSource.Token),
            LoopOperation.DeleteMany => () => repository.DeleteManyAsync(
                CancelBeforeSecond([first.Id, second.Id], cancellationTokenSource),
                cancellationTokenSource.Token),
            LoopOperation.GetByIds => () => repository.GetByIdsAsync(
                CancelBeforeSecond([first.Id, second.Id], cancellationTokenSource),
                cancellationTokenSource.Token),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown operation.")
        };

        // Act
        var exception = await act.Should().ThrowAsync<OperationCanceledException>();

        // Assert
        exception.Which.CancellationToken.Should().Be(cancellationTokenSource.Token);
        repository.Count.Should().Be(2);
        (await repository.GetByIdAsync(first.Id)).Should().BeEquivalentTo(first);
        (await repository.GetByIdAsync(second.Id)).Should().BeEquivalentTo(second);
    }

    [Fact]
    public async Task CreateAsync_GeneratorCancelsToken_ThrowsWithoutPersistingEntity()
    {
        // Arrange
        using var cancellationTokenSource = new CancellationTokenSource();
        var generatedId = Guid.NewGuid();
        var repository = new InMemoryRepository<TestEntity, Guid>(
            entity => entity.Id,
            () =>
            {
                cancellationTokenSource.Cancel();
                return generatedId;
            });
        var entity = new TestEntity(Guid.Empty, "Generated", 1);

        // Act
        var act = () => repository.CreateAsync(entity, cancellationTokenSource.Token);

        // Assert
        var exception = await act.Should().ThrowAsync<OperationCanceledException>();
        exception.Which.CancellationToken.Should().Be(cancellationTokenSource.Token);
        repository.Count.Should().Be(0);
        (await repository.GetByIdAsync(generatedId)).Should().BeNull();
    }

    [Theory]
    [InlineData(ConditionalOperation.Update)]
    [InlineData(ConditionalOperation.Patch)]
    [InlineData(ConditionalOperation.Delete)]
    public async Task ConditionalWrite_PreconditionCancelsToken_ThrowsWithoutMutatingEntity(
        ConditionalOperation operation)
    {
        // Arrange
        using var cancellationTokenSource = new CancellationTokenSource();
        var repository = CreateRepository();
        var original = CreateEntity("Original", 1);
        var replacement = original with { Name = "Replacement", Value = 2 };
        var patch = JsonSerializer.SerializeToElement(new { Name = "Patched", Value = 3 });
        await repository.CreateAsync(original);

        bool CancelAndSucceed(TestEntity entity)
        {
            _ = entity;
            cancellationTokenSource.Cancel();
            return true;
        }

        Func<Task> act = operation switch
        {
            ConditionalOperation.Update => () => repository.UpdateConditionallyAsync(
                original.Id,
                replacement,
                CancelAndSucceed,
                cancellationTokenSource.Token),
            ConditionalOperation.Patch => () => repository.PatchConditionallyAsync(
                original.Id,
                patch,
                CancelAndSucceed,
                cancellationTokenSource.Token),
            ConditionalOperation.Delete => () => repository.DeleteConditionallyAsync(
                original.Id,
                CancelAndSucceed,
                cancellationTokenSource.Token),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown operation.")
        };

        // Act
        var exception = await act.Should().ThrowAsync<OperationCanceledException>();

        // Assert
        exception.Which.CancellationToken.Should().Be(cancellationTokenSource.Token);
        repository.Count.Should().Be(1);
        (await repository.GetByIdAsync(original.Id)).Should().BeEquivalentTo(original);
    }

    [Fact]
    public async Task GetAllAsync_CancellationDuringSorting_ThrowsBeforeReturningResult()
    {
        // Arrange
        using var cancellationTokenSource = new CancellationTokenSource();
        var cancelDuringQuery = false;
        var selectorCalls = 0;
        var repository = new InMemoryRepository<TestEntity, Guid>(
            entity =>
            {
                if (cancelDuringQuery && Interlocked.Increment(ref selectorCalls) == 1)
                {
                    cancellationTokenSource.Cancel();
                }

                return entity.Id;
            },
            Guid.NewGuid);
        var entities = Enumerable.Range(0, 25)
            .Select(index => CreateEntity($"Entity{index}", index))
            .ToArray();
        await repository.CreateManyAsync(entities);
        cancelDuringQuery = true;

        // Act
        var act = () => repository.GetAllAsync(
            new PaginationRequest { Limit = 25 },
            cancellationTokenSource.Token);

        // Assert
        var exception = await act.Should().ThrowAsync<OperationCanceledException>();
        exception.Which.CancellationToken.Should().Be(cancellationTokenSource.Token);
        selectorCalls.Should().Be(1);
        repository.Count.Should().Be(25);
    }

    [Fact]
    public async Task GetByIdAsync_MutableEntity_ReturnsCallerOwnedReference()
    {
        // Arrange
        var repository = new InMemoryRepository<MutableEntity, Guid>(entity => entity.Id, Guid.NewGuid);
        var entity = new MutableEntity { Id = Guid.NewGuid(), Name = "Original" };
        await repository.CreateAsync(entity);

        // Act
        var retrieved = await repository.GetByIdAsync(entity.Id);
        entity.Name = "Mutated by caller";
        var retrievedAgain = await repository.GetByIdAsync(entity.Id);

        // Assert
        retrieved.Should().BeSameAs(entity);
        retrievedAgain.Should().BeSameAs(entity);
        retrievedAgain!.Name.Should().Be("Mutated by caller");
    }

    private static InMemoryRepository<TestEntity, Guid> CreateRepository()
        => new(entity => entity.Id, Guid.NewGuid);

    private static TestEntity CreateEntity(string name, int value)
        => new(Guid.NewGuid(), name, value);

    private static CancellingReadOnlyList<T> CancelBeforeSecond<T>(
        IReadOnlyList<T> items,
        CancellationTokenSource cancellationTokenSource)
        => new(items, cancellationTokenSource, cancelBeforeIndex: 1);
}
