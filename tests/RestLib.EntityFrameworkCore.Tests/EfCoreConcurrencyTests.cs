using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RestLib.EntityFrameworkCore.Tests.Fakes;
using Xunit;

namespace RestLib.EntityFrameworkCore.Tests;

/// <summary>
/// Tests EF Core concurrency exception handling in the repository.
/// </summary>
[Trait("Category", "Story3.3.1")]
public class EfCoreConcurrencyTests
{
    [Fact]
    public async Task UpdateAsync_ConcurrencyException_ReturnsNull()
    {
        // Arrange
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        using var context = CreateContext(connection);
        var repository = CreateRepository(context);

        var product = SeedProduct(context);
        context.ThrowConcurrencyOnNextSave = true;

        var updatedProduct = new ProductEntity
        {
            Id = product.Id,
            ProductName = "Updated Product",
            UnitPrice = 99.99m,
            StockQuantity = 42,
            CreatedAt = product.CreatedAt,
            IsActive = false,
            OptionalDescription = "Updated desc",
            Status = "Updated"
        };

        // Act
        var result = await repository.UpdateAsync(product.Id, updatedProduct);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task PatchAsync_ConcurrencyException_ReturnsNull()
    {
        // Arrange
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        using var context = CreateContext(connection);
        var repository = CreateRepository(context);

        var product = SeedProduct(context);
        context.ThrowConcurrencyOnNextSave = true;
        using var patchDocument = JsonDocument.Parse("{\"product_name\":\"Patched\"}");

        // Act
        var result = await repository.PatchAsync(product.Id, patchDocument.RootElement);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_ConcurrencyException_ReturnsFalse()
    {
        // Arrange
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        using var context = CreateContext(connection);
        var repository = CreateRepository(context);

        var product = SeedProduct(context);
        context.ThrowConcurrencyOnNextSave = true;

        // Act
        var result = await repository.DeleteAsync(product.Id);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task CreateAsync_ConcurrencyException_Throws()
    {
        // Arrange
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        using var context = CreateContext(connection);
        var repository = CreateRepository(context);
        var product = new ProductEntity
        {
            ProductName = "New Product",
            UnitPrice = 10m,
            StockQuantity = 1,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };
        context.ThrowConcurrencyOnNextSave = true;

        // Act
        var act = () => repository.CreateAsync(product);

        // Assert
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    private static ConcurrencyTestDbContext CreateContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<ConcurrencyTestDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new ConcurrencyTestDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    private static EfCoreRepository<ConcurrencyTestDbContext, ProductEntity, Guid> CreateRepository(
        ConcurrencyTestDbContext context)
    {
        var options = new EfCoreRepositoryOptions<ProductEntity, Guid>
        {
            KeySelector = product => product.Id
        };

        return new EfCoreRepository<ConcurrencyTestDbContext, ProductEntity, Guid>(context, options);
    }

    private static ProductEntity SeedProduct(ConcurrencyTestDbContext context)
    {
        var product = new ProductEntity
        {
            Id = Guid.NewGuid(),
            ProductName = "Original Product",
            UnitPrice = 10m,
            StockQuantity = 5,
            CreatedAt = DateTime.UtcNow,
            IsActive = true,
            OptionalDescription = "Original desc",
            Status = "Active"
        };

        context.Products.Add(product);
        context.SaveChanges();

        return product;
    }
}
