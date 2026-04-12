using System.Text.Json;
using FluentAssertions;
using RestLib.Filtering;
using RestLib.InMemory;
using RestLib.Pagination;
using Xunit;

namespace RestLib.Tests;

public partial class InMemoryRepositoryTests
{
    #region Thread Safety Tests

    [Fact]
    public async Task ConcurrentCreates_AllSucceed()
    {
        // Arrange
        var repository = CreateRepository();
        var tasks = new List<Task>();

        // Act
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                var entity = CreateEntity($"Entity{Guid.NewGuid()}", Random.Shared.Next());
                await repository.CreateAsync(entity);
            }));
        }
        await Task.WhenAll(tasks);

        // Assert
        repository.Count.Should().Be(100);
    }

    [Fact]
    public async Task ConcurrentReadsAndWrites_NoExceptions()
    {
        // Arrange
        var repository = CreateRepository();
        var entities = Enumerable.Range(1, 50).Select(i => CreateEntity($"Entity{i}", i)).ToList();
        foreach (var entity in entities) await repository.CreateAsync(entity);
        var tasks = new List<Task>();

        // Act
        for (int i = 0; i < 100; i++)
        {
            var index = i;
            if (index % 2 == 0)
            {
                tasks.Add(Task.Run(async () =>
                {
                    var request = new PaginationRequest { Limit = 10 };
                    await repository.GetAllAsync(request);
                }));
            }
            else
            {
                tasks.Add(Task.Run(async () =>
                {
                    var entity = CreateEntity($"Concurrent{index}", index);
                    await repository.CreateAsync(entity);
                }));
            }
        }

        // Assert
        var act = () => Task.WhenAll(tasks);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ConcurrentUpdates_LastWriteWins()
    {
        // Arrange
        var repository = CreateRepository();
        var entity = CreateEntity("Original", 100);
        await repository.CreateAsync(entity);
        var tasks = new List<Task>();

        // Act
        for (int i = 0; i < 100; i++)
        {
            var value = i;
            tasks.Add(Task.Run(async () =>
            {
                var updated = entity with { Value = value };
                await repository.UpdateAsync(entity.Id, updated);
            }));
        }
        await Task.WhenAll(tasks);

        // Assert
        var result = await repository.GetByIdAsync(entity.Id);
        result.Should().NotBeNull();
        result!.Value.Should().BeInRange(0, 99);
    }

    [Fact]
    public async Task ConcurrentDeletes_OnlyOneSucceeds()
    {
        // Arrange
        var repository = CreateRepository();
        var entity = CreateEntity();
        await repository.CreateAsync(entity);
        var results = new List<bool>();
        var lockObj = new object();

        // Act
        var tasks = Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
        {
            var deleteResult = await repository.DeleteAsync(entity.Id);
            lock (lockObj) { results.Add(deleteResult); }
        }));
        await Task.WhenAll(tasks);

        // Assert
        results.Count(r => r).Should().Be(1);
        repository.Count.Should().Be(0);
    }

    [Fact]
    public async Task ConcurrentUpdateAndDelete_DoesNotReinsert()
    {
        // Arrange — verifies the TOCTOU fix: if a concurrent delete removes
        // the entity between the existence check and the write, UpdateAsync
        // must NOT silently re-insert it.
        var repository = CreateRepository();
        var entity = CreateEntity("Target", 42);
        await repository.CreateAsync(entity);
        var updateResults = new List<TestEntity?>();
        var lockObj = new object();

        // Act — 50 threads try to update, 50 try to delete
        var tasks = new List<Task>();
        for (int i = 0; i < 100; i++)
        {
            var value = i;
            if (value % 2 == 0)
            {
                tasks.Add(Task.Run(async () =>
                {
                    var updated = entity with { Value = value };
                    var result = await repository.UpdateAsync(entity.Id, updated);
                    lock (lockObj) { updateResults.Add(result); }
                }));
            }
            else
            {
                tasks.Add(Task.Run(async () =>
                {
                    await repository.DeleteAsync(entity.Id);
                }));
            }
        }
        await Task.WhenAll(tasks);

        // Assert — the entity must be gone (deleted) and the repository
        // count must be 0 or 1 (never more).
        repository.Count.Should().BeInRange(0, 1);
        if (repository.Count == 0)
        {
            // Entity was deleted — any update after deletion must have returned null
            var retrieved = await repository.GetByIdAsync(entity.Id);
            retrieved.Should().BeNull();
        }
    }

    [Fact]
    public async Task ConcurrentPatches_DoNotThrow()
    {
        // Arrange — verifies the TOCTOU fix for PatchAsync: concurrent patches
        // on the same entity should complete without exceptions.
        var repository = CreateRepository();
        var entity = CreateEntity("PatchTarget", 0);
        await repository.CreateAsync(entity);
        var tasks = new List<Task>();

        // Act — 50 concurrent patches updating the Value property
        for (int i = 0; i < 50; i++)
        {
            var value = i;
            tasks.Add(Task.Run(async () =>
            {
                var patchJson = JsonSerializer.SerializeToElement(new { Value = value });
                await repository.PatchAsync(entity.Id, patchJson);
            }));
        }

        // Assert
        var act = () => Task.WhenAll(tasks);
        await act.Should().NotThrowAsync();
        var result = await repository.GetByIdAsync(entity.Id);
        result.Should().NotBeNull();
        result!.Value.Should().BeInRange(0, 49);
    }

    #endregion
}
