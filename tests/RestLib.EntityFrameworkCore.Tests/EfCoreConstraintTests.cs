using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RestLib.EntityFrameworkCore.Tests.Fakes;
using Xunit;

namespace RestLib.EntityFrameworkCore.Tests;

/// <summary>
/// Tests EF Core constraint violation handling in the repository.
/// </summary>
[Trait("Category", "Story3.3.2")]
public class EfCoreConstraintTests
{
    [Fact]
    public async Task CreateAsync_DuplicateKey_ThrowsConstraintViolationWithUniqueType()
    {
        // Arrange
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        using var context = CreateProductContext(connection);
        var repository = CreateProductRepository(context);

        var existingProduct = new ProductEntity
        {
            Id = Guid.NewGuid(),
            ProductName = "Existing Product",
            UnitPrice = 10m,
            StockQuantity = 1,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        context.Products.Add(existingProduct);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var duplicateProduct = new ProductEntity
        {
            Id = existingProduct.Id,
            ProductName = "Duplicate Product",
            UnitPrice = 20m,
            StockQuantity = 2,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        // Act
        var act = () => repository.CreateAsync(duplicateProduct);

        // Assert
        var exception = await act.Should().ThrowAsync<EfCoreConstraintViolationException>();
        exception.Which.ConstraintType.Should().Be(EfCoreConstraintType.UniqueConstraint);
        exception.Which.InnerException.Should().BeOfType<DbUpdateException>();
    }

    [Fact]
    public async Task CreateAsync_InvalidForeignKey_ThrowsConstraintViolationWithForeignKeyType()
    {
        // Arrange
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        using (var pragmaCommand = connection.CreateCommand())
        {
            pragmaCommand.CommandText = "PRAGMA foreign_keys = ON;";
            pragmaCommand.ExecuteNonQuery();
        }

        using var context = CreateForeignKeyContext(connection);
        var repository = CreateForeignKeyRepository(context);

        var product = new ProductEntity
        {
            Id = Guid.NewGuid(),
            ProductName = "FK Product",
            UnitPrice = 10m,
            StockQuantity = 1,
            CreatedAt = DateTime.UtcNow,
            IsActive = true,
            CategoryId = Guid.NewGuid()
        };

        // Act
        var act = () => repository.CreateAsync(product);

        // Assert
        var exception = await act.Should().ThrowAsync<EfCoreConstraintViolationException>();
        exception.Which.ConstraintType.Should().Be(EfCoreConstraintType.ForeignKeyConstraint);
        exception.Which.InnerException.Should().BeOfType<DbUpdateException>();
    }

    private static TestDbContext CreateProductContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new TestDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    private static ForeignKeyTestDbContext CreateForeignKeyContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<ForeignKeyTestDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new ForeignKeyTestDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    private static EfCoreRepository<TestDbContext, ProductEntity, Guid> CreateProductRepository(TestDbContext context)
    {
        var options = new EfCoreRepositoryOptions<ProductEntity, Guid>
        {
            KeySelector = product => product.Id
        };

        return new EfCoreRepository<TestDbContext, ProductEntity, Guid>(context, options);
    }

    private static EfCoreRepository<ForeignKeyTestDbContext, ProductEntity, Guid> CreateForeignKeyRepository(
        ForeignKeyTestDbContext context)
    {
        var options = new EfCoreRepositoryOptions<ProductEntity, Guid>
        {
            KeySelector = product => product.Id
        };

        return new EfCoreRepository<ForeignKeyTestDbContext, ProductEntity, Guid>(context, options);
    }
}
