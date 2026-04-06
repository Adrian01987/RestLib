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
        var repository = CreateRepository();
        var tasks = new List<Task>();

        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                var entity = CreateEntity($"Entity{Guid.NewGuid()}", Random.Shared.Next());
                await repository.CreateAsync(entity);
            }));
        }
        await Task.WhenAll(tasks);

        repository.Count.Should().Be(100);
    }

    [Fact]
    public async Task ConcurrentReadsAndWrites_NoExceptions()
    {
        var repository = CreateRepository();
        var entities = Enumerable.Range(1, 50).Select(i => CreateEntity($"Entity{i}", i)).ToList();
        foreach (var entity in entities) await repository.CreateAsync(entity);
        var tasks = new List<Task>();

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

        var act = () => Task.WhenAll(tasks);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ConcurrentUpdates_LastWriteWins()
    {
        var repository = CreateRepository();
        var entity = CreateEntity("Original", 100);
        await repository.CreateAsync(entity);
        var tasks = new List<Task>();

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

        var result = await repository.GetByIdAsync(entity.Id);
        result.Should().NotBeNull();
        result!.Value.Should().BeInRange(0, 99);
    }

    [Fact]
    public async Task ConcurrentDeletes_OnlyOneSucceeds()
    {
        var repository = CreateRepository();
        var entity = CreateEntity();
        await repository.CreateAsync(entity);
        var results = new List<bool>();
        var lockObj = new object();

        var tasks = Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
        {
            var deleteResult = await repository.DeleteAsync(entity.Id);
            lock (lockObj) { results.Add(deleteResult); }
        }));
        await Task.WhenAll(tasks);

        results.Count(r => r).Should().Be(1);
        repository.Count.Should().Be(0);
    }

    #endregion
}
