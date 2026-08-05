using System.Data;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using RestLib.Abstractions;
using RestLib.EntityFrameworkCore.Tests.Fakes;
using Xunit;

namespace RestLib.EntityFrameworkCore.Tests;

/// <summary>
/// Tests EF Core atomic conditional-write behavior.
/// </summary>
[Trait("Type", "Integration")]
[Trait("Feature", "ConditionalRequests")]
public class EfCoreConditionalWriteTests
{
    [Fact]
    public async Task ConditionalUpdate_PredicateRunsInsideSerializableTransaction()
    {
        // Arrange
        using var connection = await OpenConnectionAsync();
        await using var context = CreateContext(connection);
        var repository = CreateRepository(context);
        var product = SeedProduct(context);
        var predicateSawSerializableTransaction = false;

        // Act
        var result = await repository.UpdateConditionallyAsync(
            product.Id,
            CreateProduct(product.Id, "Updated"),
            current =>
            {
                var transaction = context.Database.CurrentTransaction;
                predicateSawSerializableTransaction = transaction is not null &&
                    transaction.GetDbTransaction().IsolationLevel == IsolationLevel.Serializable;
                return current.ProductName == "Original";
            });

        // Assert
        result.Status.Should().Be(ConditionalWriteStatus.Succeeded);
        predicateSawSerializableTransaction.Should().BeTrue();
        context.ChangeTracker.Clear();
        var persisted = await context.Products.FindAsync(product.Id);
        persisted!.ProductName.Should().Be("Updated");
    }

    [Fact]
    public async Task ConditionalMutations_FailedPreconditions_DoNotChangePersistence()
    {
        // Arrange
        using var connection = await OpenConnectionAsync();
        await using var context = CreateContext(connection);
        var repository = CreateRepository(context);
        var product = SeedProduct(context);
        using var patch = JsonDocument.Parse("{\"product_name\":\"Patched\"}");

        // Act
        var update = await repository.UpdateConditionallyAsync(
            product.Id,
            CreateProduct(product.Id, "Updated"),
            _ => false);
        var patchResult = await repository.PatchConditionallyAsync(
            product.Id,
            patch.RootElement,
            _ => false);
        var delete = await repository.DeleteConditionallyAsync(product.Id, _ => false);

        // Assert
        update.Status.Should().Be(ConditionalWriteStatus.PreconditionFailed);
        patchResult.Status.Should().Be(ConditionalWriteStatus.PreconditionFailed);
        delete.Status.Should().Be(ConditionalWriteStatus.PreconditionFailed);
        context.ChangeTracker.Clear();
        var persisted = await context.Products.FindAsync(product.Id);
        persisted!.ProductName.Should().Be("Original");
    }

    [Fact]
    public async Task ConditionalUpdatePatchDelete_MatchingCurrentState_PersistEachMutation()
    {
        // Arrange
        using var connection = await OpenConnectionAsync();
        await using var context = CreateContext(connection);
        var repository = CreateRepository(context);
        var product = SeedProduct(context);
        using var patch = JsonDocument.Parse("{\"product_name\":\"Patched\"}");

        // Act
        var update = await repository.UpdateConditionallyAsync(
            product.Id,
            CreateProduct(product.Id, "Updated"),
            current => current.ProductName == "Original");
        var patchResult = await repository.PatchConditionallyAsync(
            product.Id,
            patch.RootElement,
            current => current.ProductName == "Updated");
        var delete = await repository.DeleteConditionallyAsync(
            product.Id,
            current => current.ProductName == "Patched");

        // Assert
        update.Status.Should().Be(ConditionalWriteStatus.Succeeded);
        patchResult.Status.Should().Be(ConditionalWriteStatus.Succeeded);
        delete.Status.Should().Be(ConditionalWriteStatus.Succeeded);
        context.ChangeTracker.Clear();
        (await context.Products.FindAsync(product.Id)).Should().BeNull();
    }

    [Fact]
    public async Task ConditionalUpdate_TwoContextsUsingSameExpectedState_AllowsOnlyFirstWriter()
    {
        // Arrange
        using var connection = await OpenConnectionAsync();
        await using var firstContext = CreateContext(connection);
        var product = SeedProduct(firstContext);
        await using var secondContext = CreateContext(connection);
        var firstRepository = CreateRepository(firstContext);
        var secondRepository = CreateRepository(secondContext);

        // Act
        var first = await firstRepository.UpdateConditionallyAsync(
            product.Id,
            CreateProduct(product.Id, "First"),
            current => current.ProductName == "Original");
        var second = await secondRepository.UpdateConditionallyAsync(
            product.Id,
            CreateProduct(product.Id, "Second"),
            current => current.ProductName == "Original");

        // Assert
        first.Status.Should().Be(ConditionalWriteStatus.Succeeded);
        second.Status.Should().Be(ConditionalWriteStatus.PreconditionFailed);
        secondContext.ChangeTracker.Clear();
        var persisted = await secondContext.Products.FindAsync(product.Id);
        persisted!.ProductName.Should().Be("First");
    }

    [Fact]
    public async Task ConditionalUpdate_ConcurrencyException_ReturnsPreconditionFailedWithoutTrackedMutation()
    {
        // Arrange
        using var connection = await OpenConnectionAsync();
        await using var context = CreateConcurrencyContext(connection);
        var repository = CreateConcurrencyRepository(context);
        var product = SeedProduct(context);
        context.ThrowConcurrencyOnNextSave = true;

        // Act
        var result = await repository.UpdateConditionallyAsync(
            product.Id,
            CreateProduct(product.Id, "Updated"),
            current => current.ProductName == "Original");

        // Assert
        result.Status.Should().Be(ConditionalWriteStatus.PreconditionFailed);
        context.Entry(product).State.Should().Be(EntityState.Unchanged);
        product.ProductName.Should().Be("Original");
        context.ChangeTracker.Clear();
        var persisted = await context.Products.FindAsync(product.Id);
        persisted!.ProductName.Should().Be("Original");
    }

    private static async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static TestDbContext CreateContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(connection)
            .Options;
        var context = new TestDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    private static ConcurrencyTestDbContext CreateConcurrencyContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<ConcurrencyTestDbContext>()
            .UseSqlite(connection)
            .Options;
        var context = new ConcurrencyTestDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    private static EfCoreRepository<TestDbContext, ProductEntity, Guid> CreateRepository(TestDbContext context)
    {
        return new EfCoreRepository<TestDbContext, ProductEntity, Guid>(
            context,
            new EfCoreRepositoryOptions<ProductEntity, Guid> { KeySelector = product => product.Id });
    }

    private static EfCoreRepository<ConcurrencyTestDbContext, ProductEntity, Guid> CreateConcurrencyRepository(
        ConcurrencyTestDbContext context)
    {
        return new EfCoreRepository<ConcurrencyTestDbContext, ProductEntity, Guid>(
            context,
            new EfCoreRepositoryOptions<ProductEntity, Guid> { KeySelector = product => product.Id });
    }

    private static ProductEntity SeedProduct(DbContext context)
    {
        var product = CreateProduct(Guid.NewGuid(), "Original");
        context.Set<ProductEntity>().Add(product);
        context.SaveChanges();
        return product;
    }

    private static ProductEntity CreateProduct(Guid id, string name)
    {
        return new ProductEntity
        {
            Id = id,
            ProductName = name,
            UnitPrice = 10m,
            StockQuantity = 5,
            CreatedAt = new DateTime(2026, 8, 5, 0, 0, 0, DateTimeKind.Utc),
            IsActive = true,
            OptionalDescription = "Description",
            Status = "Active"
        };
    }
}
