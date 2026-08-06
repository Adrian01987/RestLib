using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RestLib.Abstractions;
using RestLib.InMemory;
using Xunit;

namespace RestLib.EntityFrameworkCore.Tests;

/// <summary>
/// Verifies the batch repository contract against every built-in adapter.
/// </summary>
[Trait("Category", "Story8.1")]
[Trait("Type", "Integration")]
public class BatchRepositoryContractTests
{
    private const string EfCoreAdapter = "EF Core";
    private const string InMemoryAdapter = "InMemory";

    private interface IBatchRepositoryHarness : IAsyncDisposable
    {
        IBatchRepository<BatchContractEntity, Guid> Repository { get; }

        Task SeedAsync(params BatchContractEntity[] entities);

        Task<BatchContractEntity?> FindAsync(Guid id);
    }

    [Theory]
    [InlineData(InMemoryAdapter)]
    [InlineData(EfCoreAdapter)]
    public async Task GetByIdsAsync_MissingAndRepeatedKeys_ReturnsEachExistingEntityOnce(
        string adapter)
    {
        // Arrange
        await using var harness = await CreateHarnessAsync(adapter);
        var first = CreateEntity("First", 1);
        var second = CreateEntity("Second", 2);
        var missingId = Guid.NewGuid();
        await harness.SeedAsync(first, second);

        // Act
        var result = await harness.Repository.GetByIdsAsync(
            [second.Id, missingId, second.Id, first.Id]);

        // Assert
        result.Should().HaveCount(2);
        result.Keys.Should().BeEquivalentTo([first.Id, second.Id]);
        result.Should().NotContainKey(missingId);
        result[first.Id].Name.Should().Be("First");
        result[second.Id].Name.Should().Be("Second");
    }

    [Theory]
    [InlineData(InMemoryAdapter)]
    [InlineData(EfCoreAdapter)]
    public async Task UpdateManyAsync_MissingAndRepeatedKeys_ReturnFinalEntitiesInRequestOrder(
        string adapter)
    {
        // Arrange
        await using var harness = await CreateHarnessAsync(adapter);
        var first = CreateEntity("First", 1);
        var second = CreateEntity("Second", 2);
        await harness.SeedAsync(first, second);
        var updatedSecond = CreateEntity("Second updated", 20, second.Id);
        var missing = CreateEntity("Missing", 30);
        var updatedFirst = CreateEntity("First updated", 10, first.Id);
        var finalFirst = CreateEntity("First final", 100, first.Id);

        // Act
        var result = await harness.Repository.UpdateManyAsync(
            [updatedSecond, missing, updatedFirst, finalFirst]);

        // Assert
        result.Select(entity => entity.Id).Should().Equal(second.Id, first.Id, first.Id);
        result[1].Should().BeEquivalentTo(finalFirst);
        result[2].Should().BeEquivalentTo(finalFirst);
        (await harness.FindAsync(first.Id))!.Version.Should().Be(100);
        (await harness.FindAsync(second.Id))!.Version.Should().Be(20);
        (await harness.FindAsync(missing.Id)).Should().BeNull();
    }

    [Theory]
    [InlineData(InMemoryAdapter)]
    [InlineData(EfCoreAdapter)]
    public async Task PatchManyAsync_RepeatedKey_AppliesDocumentsInOrderAndPreservesResultSlots(
        string adapter)
    {
        // Arrange
        await using var harness = await CreateHarnessAsync(adapter);
        var entity = CreateEntity("Original", 0);
        var missingId = Guid.NewGuid();
        await harness.SeedAsync(entity);
        var firstPatch = JsonSerializer.SerializeToElement(new { name = "First patch", version = 1 });
        var missingPatch = JsonSerializer.SerializeToElement(new { name = "Missing" });
        var finalPatch = JsonSerializer.SerializeToElement(new { name = "Final patch", version = 2 });

        // Act
        var result = await harness.Repository.PatchManyAsync(
            [(entity.Id, firstPatch), (missingId, missingPatch), (entity.Id, finalPatch)]);

        // Assert
        result.Select(item => item.Id).Should().Equal(entity.Id, entity.Id);
        result.Should().OnlyContain(item => item.Name == "Final patch" && item.Version == 2);
        var persisted = await harness.FindAsync(entity.Id);
        persisted!.Name.Should().Be("Final patch");
        persisted.Version.Should().Be(2);
        (await harness.FindAsync(missingId)).Should().BeNull();
    }

    [Theory]
    [InlineData(InMemoryAdapter)]
    [InlineData(EfCoreAdapter)]
    public async Task DeleteManyAsync_MissingAndRepeatedKeys_ReturnsDistinctDeletedCount(
        string adapter)
    {
        // Arrange
        await using var harness = await CreateHarnessAsync(adapter);
        var first = CreateEntity("First", 1);
        var second = CreateEntity("Second", 2);
        var retained = CreateEntity("Retained", 3);
        var missingId = Guid.NewGuid();
        await harness.SeedAsync(first, second, retained);

        // Act
        var result = await harness.Repository.DeleteManyAsync(
            [first.Id, missingId, first.Id, second.Id, missingId]);

        // Assert
        result.Should().Be(2);
        (await harness.FindAsync(first.Id)).Should().BeNull();
        (await harness.FindAsync(second.Id)).Should().BeNull();
        (await harness.FindAsync(retained.Id)).Should().NotBeNull();
    }

    [Theory]
    [InlineData(InMemoryAdapter)]
    [InlineData(EfCoreAdapter)]
    public async Task CreateManyAsync_LaterDuplicateKey_RejectsBatchAtomically(
        string adapter)
    {
        // Arrange
        await using var harness = await CreateHarnessAsync(adapter);
        var retained = CreateEntity("Retained", 1);
        var newEntity = CreateEntity("New", 2);
        var duplicate = CreateEntity("Duplicate", 3, newEntity.Id);
        await harness.SeedAsync(retained);

        // Act
        Func<Task> act = async () =>
        {
            _ = await harness.Repository.CreateManyAsync([newEntity, duplicate]);
        };

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        (await harness.FindAsync(newEntity.Id)).Should().BeNull();
        var persistedRetained = await harness.FindAsync(retained.Id);
        persistedRetained!.Name.Should().Be("Retained");
        persistedRetained.Version.Should().Be(1);
    }

    [Theory]
    [InlineData(InMemoryAdapter)]
    [InlineData(EfCoreAdapter)]
    public async Task PatchManyAsync_LaterKeyMutation_RejectsBatchAtomically(
        string adapter)
    {
        // Arrange
        await using var harness = await CreateHarnessAsync(adapter);
        var first = CreateEntity("First", 1);
        var second = CreateEntity("Second", 2);
        await harness.SeedAsync(first, second);
        var validPatch = JsonSerializer.SerializeToElement(new { name = "Changed" });
        var invalidPatch = JsonSerializer.SerializeToElement(new { id = Guid.NewGuid() });

        // Act
        Func<Task> act = async () =>
        {
            _ = await harness.Repository.PatchManyAsync(
                [(first.Id, validPatch), (second.Id, invalidPatch)]);
        };

        // Assert
        await act.Should().ThrowAsync<PatchValidationException>();
        (await harness.FindAsync(first.Id))!.Name.Should().Be("First");
        (await harness.FindAsync(second.Id))!.Name.Should().Be("Second");
    }

    private static BatchContractEntity CreateEntity(
        string name,
        int version,
        Guid? id = null)
    {
        return new BatchContractEntity
        {
            Id = id ?? Guid.NewGuid(),
            Name = name,
            Version = version
        };
    }

    private static async Task<IBatchRepositoryHarness> CreateHarnessAsync(string adapter)
    {
        return adapter switch
        {
            InMemoryAdapter => new InMemoryBatchRepositoryHarness(),
            EfCoreAdapter => await EfCoreBatchRepositoryHarness.CreateAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(adapter), adapter, "Unknown adapter.")
        };
    }

    private sealed class InMemoryBatchRepositoryHarness : IBatchRepositoryHarness
    {
        private readonly InMemoryRepository<BatchContractEntity, Guid> _repository =
            new(entity => entity.Id, Guid.NewGuid);

        public IBatchRepository<BatchContractEntity, Guid> Repository => _repository;

        public Task SeedAsync(params BatchContractEntity[] entities)
        {
            _repository.Seed(entities);
            return Task.CompletedTask;
        }

        public Task<BatchContractEntity?> FindAsync(Guid id)
        {
            return _repository.GetByIdAsync(id);
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class EfCoreBatchRepositoryHarness : IBatchRepositoryHarness
    {
        private readonly SqliteConnection _connection;
        private readonly BatchContractDbContext _context;

        private EfCoreBatchRepositoryHarness(
            SqliteConnection connection,
            BatchContractDbContext context)
        {
            _connection = connection;
            _context = context;
            Repository = new EfCoreRepository<BatchContractDbContext, BatchContractEntity, Guid>(
                context,
                new EfCoreRepositoryOptions<BatchContractEntity, Guid>
                {
                    KeySelector = entity => entity.Id
                });
        }

        public IBatchRepository<BatchContractEntity, Guid> Repository { get; }

        public static async Task<EfCoreBatchRepositoryHarness> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            BatchContractDbContext? context = null;
            try
            {
                await connection.OpenAsync();
                var options = new DbContextOptionsBuilder<BatchContractDbContext>()
                    .UseSqlite(connection)
                    .Options;
                context = new BatchContractDbContext(options);
                await context.Database.EnsureCreatedAsync();
                return new EfCoreBatchRepositoryHarness(connection, context);
            }
            catch
            {
                if (context is not null)
                {
                    await context.DisposeAsync();
                }

                await connection.DisposeAsync();
                throw;
            }
        }

        public async Task SeedAsync(params BatchContractEntity[] entities)
        {
            _context.Set<BatchContractEntity>().AddRange(entities);
            await _context.SaveChangesAsync();
            _context.ChangeTracker.Clear();
        }

        public async Task<BatchContractEntity?> FindAsync(Guid id)
        {
            _context.ChangeTracker.Clear();
            return await _context.Set<BatchContractEntity>()
                .AsNoTracking()
                .SingleOrDefaultAsync(entity => entity.Id == id);
        }

        public async ValueTask DisposeAsync()
        {
            await _context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class BatchContractDbContext : DbContext
    {
        internal BatchContractDbContext(DbContextOptions<BatchContractDbContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<BatchContractEntity>(entity =>
            {
                entity.HasKey(item => item.Id);
                entity.Property(item => item.Name).IsRequired();
            });
        }
    }

    private sealed class BatchContractEntity
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public int Version { get; set; }
    }
}
