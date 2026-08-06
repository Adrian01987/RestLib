using System.Data.Common;
using System.Linq.Expressions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RestLib.EntityFrameworkCore.Tests.Fakes;
using Xunit;

namespace RestLib.EntityFrameworkCore.Tests;

/// <summary>
/// Verifies bounded resource-key query construction and execution.
/// </summary>
[Trait("Type", "Integration")]
public sealed class EfCoreBatchKeyQueryExecutorTests
{
    /// <summary>
    /// Verifies that scalar keys use a compact Contains expression.
    /// </summary>
    [Fact]
    public void BuildContainsPredicate_ScalarKeys_UsesContainsExpression()
    {
        // Arrange
        using var context = CreateSqlServerContext();
        var metadata = new EfCoreKeyMetadata<ProductEntity, Guid>(
            context.Model,
            entity => entity.Id);
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();

        // Act
        var predicate = metadata.BuildContainsPredicate([firstId, secondId]);
        var sql = context.Products.Where(predicate).ToQueryString();

        // Assert
        predicate.Body.Should().BeAssignableTo<MethodCallExpression>()
            .Which.Method.Name.Should().Be(nameof(Enumerable.Contains));
        sql.Should().Contain(" IN ");
    }

    /// <summary>
    /// Verifies that an empty key set produces a predicate that never matches.
    /// </summary>
    [Fact]
    public void BuildContainsPredicate_EmptyKeys_ReturnsFalsePredicate()
    {
        // Arrange
        using var context = CreateSqlServerContext();
        var metadata = new EfCoreKeyMetadata<ProductEntity, Guid>(
            context.Model,
            entity => entity.Id);
        var entity = new ProductEntity { Id = Guid.NewGuid() };

        // Act
        var matches = metadata.BuildContainsPredicate([]).Compile()(entity);

        // Assert
        matches.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that composite-key predicates have logarithmic expression depth.
    /// </summary>
    [Fact]
    public void BuildContainsPredicate_CompositeKeys_BuildsBalancedOrTree()
    {
        // Arrange
        using var context = CreateSqlServerContext();
        var metadata = new EfCoreKeyMetadata<
            TenantProductEntity,
            RestLibCompositeKey<Guid, string>>(context.Model, keySelector: null);
        var tenantId = Guid.NewGuid();
        var keys = Enumerable.Range(0, 256)
            .Select(index => new RestLibCompositeKey<Guid, string>(tenantId, $"SKU-{index:D4}"))
            .ToList();

        // Act
        var predicate = metadata.BuildContainsPredicate(keys);
        var depth = GetOrElseDepth(predicate.Body);
        var sql = context.TenantProducts.Where(predicate).ToQueryString();

        // Assert
        depth.Should().BeLessThan(16);
        sql.Should().Contain("WHERE");
    }

    /// <summary>
    /// Verifies scalar-key deduplication and bounded sequential query execution.
    /// </summary>
    [Fact]
    public async Task FetchAsync_ScalarKeysBeyondChunkWithDuplicates_ReturnsUniqueMatchesInBoundedQueries()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var commandCounter = new ReaderCommandCounter();
        await using var context = CreateSqliteContext(connection, commandCounter);
        await context.Database.EnsureCreatedAsync();
        var entities = Enumerable.Range(0, 520)
            .Select(CreateProduct)
            .ToList();
        context.Products.AddRange(entities);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        commandCounter.Reset();

        var metadata = new EfCoreKeyMetadata<ProductEntity, Guid>(
            context.Model,
            entity => entity.Id);
        var executor = new EfCoreBatchKeyQueryExecutor<ProductEntity, Guid>(metadata);
        var missingId = Guid.NewGuid();
        var keys = entities.Select(entity => entity.Id)
            .Concat([entities[0].Id, entities[^1].Id, missingId])
            .ToList();

        // Act
        var result = await executor.FetchAsync(context.Products.AsNoTracking(), keys);

        // Assert
        result.Select(entity => entity.Id).Should().BeEquivalentTo(entities.Select(entity => entity.Id));
        commandCounter.ReaderCommandCount.Should().Be(2);
        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that large composite-key lookups are split before provider expression limits.
    /// </summary>
    [Fact]
    public async Task FetchAsync_CompositeKeysBeyondExpressionLimit_ReturnsUniqueMatchesInBoundedQueries()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var commandCounter = new ReaderCommandCounter();
        await using var context = CreateSqliteContext(connection, commandCounter);
        await context.Database.EnsureCreatedAsync();
        var tenantId = Guid.NewGuid();
        var entities = Enumerable.Range(0, 1030)
            .Select(index => CreateTenantProduct(tenantId, index))
            .ToList();
        context.TenantProducts.AddRange(entities);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        commandCounter.Reset();

        var metadata = new EfCoreKeyMetadata<
            TenantProductEntity,
            RestLibCompositeKey<Guid, string>>(context.Model, keySelector: null);
        var executor = new EfCoreBatchKeyQueryExecutor<
            TenantProductEntity,
            RestLibCompositeKey<Guid, string>>(metadata);
        var missingKey = new RestLibCompositeKey<Guid, string>(tenantId, "MISSING");
        var keys = entities
            .Select(entity => new RestLibCompositeKey<Guid, string>(entity.TenantId, entity.Sku))
            .Concat([new(entities[0].TenantId, entities[0].Sku), missingKey])
            .ToList();

        // Act
        var result = await executor.FetchAsync(context.TenantProducts.AsNoTracking(), keys);

        // Assert
        result.Select(entity => entity.Sku).Should().BeEquivalentTo(entities.Select(entity => entity.Sku));
        commandCounter.ReaderCommandCount.Should().Be(5);
        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that the public repository keyed-read path uses bounded queries and preserves no-tracking.
    /// </summary>
    [Fact]
    public async Task GetByIdsAsync_ScalarKeysBeyondChunk_UsesBoundedRepositoryQueries()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var commandCounter = new ReaderCommandCounter();
        await using var context = CreateSqliteContext(connection, commandCounter);
        await context.Database.EnsureCreatedAsync();
        var entities = Enumerable.Range(0, EfCoreBatchKeyQueryExecutor<ProductEntity, Guid>.ParameterBudget + 8)
            .Select(CreateProduct)
            .ToList();
        context.Products.AddRange(entities);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        commandCounter.Reset();
        var repository = new EfCoreRepository<TestDbContext, ProductEntity, Guid>(
            context,
            new EfCoreRepositoryOptions<ProductEntity, Guid>
            {
                KeySelector = entity => entity.Id,
                UseAsNoTracking = true
            });
        var keys = entities.Select(entity => entity.Id).ToList();

        // Act
        var result = await repository.GetByIdsAsync(keys);

        // Assert
        result.Keys.Should().BeEquivalentTo(keys);
        commandCounter.ReaderCommandCount.Should().Be(2);
        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that a composite-key mutating repository path fetches bounded, deduplicated chunks.
    /// </summary>
    [Fact]
    public async Task DeleteManyAsync_CompositeKeysBeyondChunk_UsesBoundedRepositoryQueries()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var commandCounter = new ReaderCommandCounter();
        await using var context = CreateSqliteContext(connection, commandCounter);
        await context.Database.EnsureCreatedAsync();
        var tenantId = Guid.NewGuid();
        var entityCount = (EfCoreBatchKeyQueryExecutor<
            TenantProductEntity,
            RestLibCompositeKey<Guid, string>>.ParameterBudget / 2) + 1;
        var entities = Enumerable.Range(0, entityCount)
            .Select(index => CreateTenantProduct(tenantId, index))
            .ToList();
        context.TenantProducts.AddRange(entities);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        commandCounter.Reset();
        var repository = new EfCoreRepository<
            TestDbContext,
            TenantProductEntity,
            RestLibCompositeKey<Guid, string>>(
                context,
                new EfCoreRepositoryOptions<
                    TenantProductEntity,
                    RestLibCompositeKey<Guid, string>>());
        var keys = entities
            .Select(entity => new RestLibCompositeKey<Guid, string>(entity.TenantId, entity.Sku))
            .Append(new RestLibCompositeKey<Guid, string>(entities[0].TenantId, entities[0].Sku))
            .ToList();

        // Act
        var deleted = await repository.DeleteManyAsync(keys);

        // Assert
        deleted.Should().Be(entityCount);
        commandCounter.ReaderCommandCount.Should().Be(2);
        context.TenantProducts.Local.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that cancellation is observed before the first chunk is submitted.
    /// </summary>
    [Fact]
    public async Task FetchAsync_CancelledBeforeFirstChunk_ThrowsWithoutExecutingQuery()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var commandCounter = new ReaderCommandCounter();
        await using var context = CreateSqliteContext(connection, commandCounter);
        var metadata = new EfCoreKeyMetadata<ProductEntity, Guid>(
            context.Model,
            entity => entity.Id);
        var executor = new EfCoreBatchKeyQueryExecutor<ProductEntity, Guid>(metadata);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // Act
        var act = () => executor.FetchAsync(
            context.Products,
            [Guid.NewGuid()],
            cancellation.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        commandCounter.ReaderCommandCount.Should().Be(0);
    }

    private static TestDbContext CreateSqlServerContext()
    {
        const string connectionString =
            "Server=localhost;Database=RestLibBatchKeyQueryTests;"
            + "Integrated Security=True;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new TestDbContext(options);
    }

    private static TestDbContext CreateSqliteContext(
        SqliteConnection connection,
        ReaderCommandCounter commandCounter)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(commandCounter)
            .Options;

        return new TestDbContext(options);
    }

    private static ProductEntity CreateProduct(int index)
    {
        return new ProductEntity
        {
            Id = Guid.NewGuid(),
            ProductName = $"Product {index}",
            UnitPrice = index,
            StockQuantity = index,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };
    }

    private static TenantProductEntity CreateTenantProduct(Guid tenantId, int index)
    {
        return new TenantProductEntity
        {
            TenantId = tenantId,
            Sku = $"SKU-{index:D4}",
            ProductName = $"Product {index}",
            UnitPrice = index,
            StockQuantity = index,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };
    }

    private static int GetOrElseDepth(Expression expression)
    {
        if (expression is not BinaryExpression { NodeType: ExpressionType.OrElse } binary)
        {
            return 0;
        }

        return 1 + Math.Max(GetOrElseDepth(binary.Left), GetOrElseDepth(binary.Right));
    }

    private sealed class ReaderCommandCounter : DbCommandInterceptor
    {
        internal int ReaderCommandCount { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                ReaderCommandCount++;
            }

            return ValueTask.FromResult(result);
        }

        internal void Reset()
        {
            ReaderCommandCount = 0;
        }
    }
}
