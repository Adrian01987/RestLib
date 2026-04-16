using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RestLib.Abstractions;
using RestLib.EntityFrameworkCore.Tests.Fakes;
using Xunit;

namespace RestLib.EntityFrameworkCore.Tests;

/// <summary>
/// Integration tests for the EF Core adapter's IBatchRepository methods.
/// </summary>
[Trait("Category", "Story8.1")]
[Trait("Type", "Integration")]
public class EfCoreBatchTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private TestDbContext _db = null!;
    private IBatchRepository<ProductEntity, Guid> _batchRepository = null!;

    public async Task InitializeAsync()
    {
        (_host, _client, _db) = await new EfCoreTestHostBuilder<ProductEntity, Guid>("/api/products")
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.EnableBatch();
            })
            .BuildAsync();

        _batchRepository = _host.Services.CreateScope().ServiceProvider
            .GetRequiredService<IBatchRepository<ProductEntity, Guid>>();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    [Fact]
    public async Task GetByIdsAsync_WithExistingIds_ReturnsAllEntities()
    {
        // Arrange
        var product1 = CreateProduct(name: "Product 1", unitPrice: 10m, stockQuantity: 1);
        var product2 = CreateProduct(name: "Product 2", unitPrice: 20m, stockQuantity: 2);
        var product3 = CreateProduct(name: "Product 3", unitPrice: 30m, stockQuantity: 3);

        await SeedProductsAsync(product1, product2, product3);

        // Act
        var result = await _batchRepository.GetByIdsAsync([product1.Id, product2.Id, product3.Id]);

        // Assert
        result.Should().HaveCount(3);
        result.Keys.Should().BeEquivalentTo([product1.Id, product2.Id, product3.Id]);
        result[product1.Id].ProductName.Should().Be("Product 1");
        result[product2.Id].UnitPrice.Should().Be(20m);
        result[product3.Id].StockQuantity.Should().Be(3);
    }

    [Fact]
    public async Task GetByIdsAsync_WithMixOfExistingAndMissing_ReturnsOnlyExisting()
    {
        // Arrange
        var product1 = CreateProduct(name: "Product 1", unitPrice: 10m, stockQuantity: 1);
        var product2 = CreateProduct(name: "Product 2", unitPrice: 20m, stockQuantity: 2);
        var missingId = Guid.NewGuid();

        await SeedProductsAsync(product1, product2);

        // Act
        var result = await _batchRepository.GetByIdsAsync([product1.Id, missingId, product2.Id]);

        // Assert
        result.Should().HaveCount(2);
        result.Should().ContainKey(product1.Id);
        result.Should().ContainKey(product2.Id);
        result.Should().NotContainKey(missingId);
    }

    [Fact]
    public async Task GetByIdsAsync_WithEmptyList_ReturnsEmptyDictionary()
    {
        // Arrange

        // Act
        var result = await _batchRepository.GetByIdsAsync([]);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateManyAsync_WithValidEntities_PersistsAll()
    {
        // Arrange
        var products = new[]
        {
            CreateProduct(name: "Product 1", unitPrice: 10m, stockQuantity: 1),
            CreateProduct(name: "Product 2", unitPrice: 20m, stockQuantity: 2),
            CreateProduct(name: "Product 3", unitPrice: 30m, stockQuantity: 3),
            CreateProduct(name: "Product 4", unitPrice: 40m, stockQuantity: 4),
            CreateProduct(name: "Product 5", unitPrice: 50m, stockQuantity: 5)
        };

        // Act
        var result = await _batchRepository.CreateManyAsync(products);

        // Assert
        result.Should().HaveCount(5);
        result.Select(product => product.Id).Should().BeEquivalentTo(products.Select(product => product.Id));
        _db.Products.Should().HaveCount(5);

        var persistedProducts = _db.Products.ToList();
        persistedProducts.Select(product => product.ProductName).Should().BeEquivalentTo(products.Select(product => product.ProductName));
        persistedProducts.Select(product => product.UnitPrice).Should().BeEquivalentTo(products.Select(product => product.UnitPrice));
    }

    [Fact]
    public async Task CreateManyAsync_WithEmptyList_ReturnsEmptyList()
    {
        // Arrange

        // Act
        var result = await _batchRepository.CreateManyAsync([]);

        // Assert
        result.Should().BeEmpty();
        _db.Products.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateManyAsync_WithExistingEntities_UpdatesAll()
    {
        // Arrange
        var product1 = CreateProduct(name: "Original 1", unitPrice: 10m, stockQuantity: 1);
        var product2 = CreateProduct(name: "Original 2", unitPrice: 20m, stockQuantity: 2);
        var product3 = CreateProduct(name: "Original 3", unitPrice: 30m, stockQuantity: 3);

        await SeedProductsAsync(product1, product2, product3);

        var updated1 = CreateUpdatedProduct(product1, name: "Updated 1", unitPrice: 100m, stockQuantity: 10);
        var updated2 = CreateUpdatedProduct(product2, name: "Updated 2", unitPrice: 200m, stockQuantity: 20);
        var updated3 = CreateUpdatedProduct(product3, name: "Updated 3", unitPrice: 300m, stockQuantity: 30);

        // Act
        var result = await _batchRepository.UpdateManyAsync([updated1, updated2, updated3]);

        // Assert
        result.Should().HaveCount(3);
        result.Select(product => product.ProductName).Should().Equal("Updated 1", "Updated 2", "Updated 3");
        result.Select(product => product.UnitPrice).Should().Equal(100m, 200m, 300m);

        _db.ChangeTracker.Clear();

        var persisted1 = await _db.Products.FindAsync(product1.Id);
        var persisted2 = await _db.Products.FindAsync(product2.Id);
        var persisted3 = await _db.Products.FindAsync(product3.Id);

        persisted1!.ProductName.Should().Be("Updated 1");
        persisted1.UnitPrice.Should().Be(100m);
        persisted2!.ProductName.Should().Be("Updated 2");
        persisted2.UnitPrice.Should().Be(200m);
        persisted3!.ProductName.Should().Be("Updated 3");
        persisted3.UnitPrice.Should().Be(300m);
    }

    [Fact]
    public async Task UpdateManyAsync_WithMixOfExistingAndNonExisting_SkipsNonExisting()
    {
        // Arrange
        var product1 = CreateProduct(name: "Original 1", unitPrice: 10m, stockQuantity: 1);
        var product2 = CreateProduct(name: "Original 2", unitPrice: 20m, stockQuantity: 2);

        await SeedProductsAsync(product1, product2);

        var missingId = Guid.NewGuid();
        var updated1 = CreateUpdatedProduct(product1, name: "Updated 1", unitPrice: 100m, stockQuantity: 10);
        var missing = new ProductEntity
        {
            Id = missingId,
            ProductName = "Missing",
            UnitPrice = 999m,
            StockQuantity = 99,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };
        var updated2 = CreateUpdatedProduct(product2, name: "Updated 2", unitPrice: 200m, stockQuantity: 20);

        // Act
        var result = await _batchRepository.UpdateManyAsync([updated1, missing, updated2]);

        // Assert
        result.Should().HaveCount(2);
        result.Select(product => product.Id).Should().BeEquivalentTo([product1.Id, product2.Id]);

        _db.ChangeTracker.Clear();

        var persisted1 = await _db.Products.FindAsync(product1.Id);
        var persisted2 = await _db.Products.FindAsync(product2.Id);
        var persistedMissing = await _db.Products.FindAsync(missingId);

        persisted1!.ProductName.Should().Be("Updated 1");
        persisted2!.ProductName.Should().Be("Updated 2");
        persistedMissing.Should().BeNull();
        _db.Products.Should().HaveCount(2);
    }

    [Fact]
    public async Task PatchManyAsync_WithDifferentPatchDocuments_PatchesAll()
    {
        // Arrange
        var product1 = CreateProduct(name: "Original 1", unitPrice: 10m, stockQuantity: 1, isActive: true);
        var product2 = CreateProduct(name: "Original 2", unitPrice: 20m, stockQuantity: 2, isActive: true);
        var product3 = CreateProduct(name: "Original 3", unitPrice: 30m, stockQuantity: 3, isActive: true);

        await SeedProductsAsync(product1, product2, product3);

        var patch1 = DeserializeJsonElement("""
            {"product_name":"New Name 1"}
            """);
        var patch2 = DeserializeJsonElement("""
            {"unit_price":99.99}
            """);
        var patch3 = DeserializeJsonElement("""
            {"is_active":false,"stock_quantity":0}
            """);

        // Act
        var result = await _batchRepository.PatchManyAsync(
            [(product1.Id, patch1), (product2.Id, patch2), (product3.Id, patch3)]);

        // Assert
        result.Should().HaveCount(3);

        var patched1 = result.Single(product => product.Id == product1.Id);
        var patched2 = result.Single(product => product.Id == product2.Id);
        var patched3 = result.Single(product => product.Id == product3.Id);

        patched1.ProductName.Should().Be("New Name 1");
        patched1.UnitPrice.Should().Be(10m);
        patched2.ProductName.Should().Be("Original 2");
        patched2.UnitPrice.Should().Be(99.99m);
        patched3.ProductName.Should().Be("Original 3");
        patched3.IsActive.Should().BeFalse();
        patched3.StockQuantity.Should().Be(0);

        _db.ChangeTracker.Clear();

        var persisted1 = await _db.Products.FindAsync(product1.Id);
        var persisted2 = await _db.Products.FindAsync(product2.Id);
        var persisted3 = await _db.Products.FindAsync(product3.Id);

        persisted1!.ProductName.Should().Be("New Name 1");
        persisted1.UnitPrice.Should().Be(10m);
        persisted2!.ProductName.Should().Be("Original 2");
        persisted2.UnitPrice.Should().Be(99.99m);
        persisted3!.ProductName.Should().Be("Original 3");
        persisted3.IsActive.Should().BeFalse();
        persisted3.StockQuantity.Should().Be(0);
    }

    [Fact]
    public async Task PatchManyAsync_WithMixOfExistingAndNonExisting_SkipsNonExisting()
    {
        // Arrange
        var product1 = CreateProduct(name: "Original 1", unitPrice: 10m, stockQuantity: 1, isActive: true);
        var product2 = CreateProduct(name: "Original 2", unitPrice: 20m, stockQuantity: 2, isActive: true);
        var missingId = Guid.NewGuid();

        await SeedProductsAsync(product1, product2);

        var patch1 = DeserializeJsonElement("""
            {"product_name":"Patched 1"}
            """);
        var missingPatch = DeserializeJsonElement("""
            {"product_name":"Missing"}
            """);
        var patch2 = DeserializeJsonElement("""
            {"unit_price":77.77}
            """);

        // Act
        var result = await _batchRepository.PatchManyAsync(
            [(product1.Id, patch1), (missingId, missingPatch), (product2.Id, patch2)]);

        // Assert
        result.Should().HaveCount(2);
        result.Select(product => product.Id).Should().BeEquivalentTo([product1.Id, product2.Id]);

        _db.ChangeTracker.Clear();

        var persisted1 = await _db.Products.FindAsync(product1.Id);
        var persisted2 = await _db.Products.FindAsync(product2.Id);
        var persistedMissing = await _db.Products.FindAsync(missingId);

        persisted1!.ProductName.Should().Be("Patched 1");
        persisted2!.UnitPrice.Should().Be(77.77m);
        persistedMissing.Should().BeNull();
        _db.Products.Should().HaveCount(2);
    }

    [Fact]
    public async Task PatchManyAsync_WithEmptyPatchDocuments_PreservesAllFields()
    {
        // Arrange
        var product = CreateProduct(name: "Original", unitPrice: 10m, stockQuantity: 5, isActive: true);
        product.OptionalDescription = "Description";
        product.Status = "active";

        await SeedProductsAsync(product);

        var emptyPatch = DeserializeJsonElement("{}");

        // Act
        var result = await _batchRepository.PatchManyAsync([(product.Id, emptyPatch)]);

        // Assert
        result.Should().HaveCount(1);
        var patched = result.Single();
        patched.ProductName.Should().Be("Original");
        patched.UnitPrice.Should().Be(10m);
        patched.StockQuantity.Should().Be(5);
        patched.IsActive.Should().BeTrue();
        patched.OptionalDescription.Should().Be("Description");
        patched.Status.Should().Be("active");

        _db.ChangeTracker.Clear();

        var persisted = await _db.Products.FindAsync(product.Id);
        persisted.Should().NotBeNull();
        persisted!.ProductName.Should().Be("Original");
        persisted.UnitPrice.Should().Be(10m);
        persisted.StockQuantity.Should().Be(5);
        persisted.IsActive.Should().BeTrue();
        persisted.OptionalDescription.Should().Be("Description");
        persisted.Status.Should().Be("active");
    }

    [Fact]
    public async Task PatchManyAsync_StrictUnknownField_ThrowsAndDoesNotPersistAnyChanges()
    {
        // Arrange
        var options = new EfCoreRepositoryOptions<ProductEntity, Guid>
        {
            KeySelector = product => product.Id,
            PatchUnknownFieldBehavior = EfCorePatchUnknownFieldBehavior.Strict
        };
        var repository = new EfCoreRepository<TestDbContext, ProductEntity, Guid>(_db, options);

        var product1 = CreateProduct(name: "Original 1", unitPrice: 10m, stockQuantity: 1, isActive: true);
        var product2 = CreateProduct(name: "Original 2", unitPrice: 20m, stockQuantity: 2, isActive: true);
        await SeedProductsAsync(product1, product2);

        var validPatch = DeserializeJsonElement("""
            {"product_name":"Updated 1"}
            """);
        var invalidPatch = DeserializeJsonElement("""
            {"unknown_field":"nope"}
            """);

        // Act
        var act = () => repository.PatchManyAsync([(product1.Id, validPatch), (product2.Id, invalidPatch)]);

        // Assert
        var exception = await act.Should().ThrowAsync<EfCorePatchValidationException>();
        exception.Which.Message.Should().Contain("unknown_field");

        _db.ChangeTracker.Clear();
        var persisted1 = await _db.Products.FindAsync(product1.Id);
        var persisted2 = await _db.Products.FindAsync(product2.Id);
        persisted1.Should().NotBeNull();
        persisted2.Should().NotBeNull();
        persisted1!.ProductName.Should().Be("Original 1");
        persisted2!.ProductName.Should().Be("Original 2");
    }

    [Fact]
    public async Task DeleteManyAsync_WithExistingKeys_DeletesAllAndReturnsCount()
    {
        // Arrange
        var product1 = CreateProduct(name: "Product 1", unitPrice: 10m, stockQuantity: 1);
        var product2 = CreateProduct(name: "Product 2", unitPrice: 20m, stockQuantity: 2);
        var product3 = CreateProduct(name: "Product 3", unitPrice: 30m, stockQuantity: 3);

        await SeedProductsAsync(product1, product2, product3);

        // Act
        var result = await _batchRepository.DeleteManyAsync([product1.Id, product2.Id, product3.Id]);

        // Assert
        result.Should().Be(3);

        _db.ChangeTracker.Clear();

        _db.Products.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteManyAsync_WithNonExistentKeys_ReturnsCorrectCount()
    {
        // Arrange
        var product1 = CreateProduct(name: "Product 1", unitPrice: 10m, stockQuantity: 1);
        var product2 = CreateProduct(name: "Product 2", unitPrice: 20m, stockQuantity: 2);
        var missing1 = Guid.NewGuid();
        var missing2 = Guid.NewGuid();

        await SeedProductsAsync(product1, product2);

        // Act
        var result = await _batchRepository.DeleteManyAsync([product1.Id, missing1, product2.Id, missing2]);

        // Assert
        result.Should().Be(2);

        _db.ChangeTracker.Clear();

        _db.Products.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateManyAsync_WhenSaveChangesThrowsConcurrencyException_ThrowsAndDoesNotPersist()
    {
        // Arrange
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = CreateConcurrencyContext(connection);
        var repository = CreateConcurrencyBatchRepository(context);

        var product1 = SeedConcurrencyProduct(context, name: "Original 1", unitPrice: 10m, stockQuantity: 1);
        var product2 = SeedConcurrencyProduct(context, name: "Original 2", unitPrice: 20m, stockQuantity: 2);
        var updated1 = CreateUpdatedProduct(product1, name: "Updated 1", unitPrice: 100m, stockQuantity: 10);
        var updated2 = CreateUpdatedProduct(product2, name: "Updated 2", unitPrice: 200m, stockQuantity: 20);
        context.ThrowConcurrencyOnNextSave = true;

        // Act
        var act = () => repository.UpdateManyAsync([updated1, updated2]);

        // Assert
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();

        context.ChangeTracker.Clear();
        var persisted1 = await context.Products.FindAsync(product1.Id);
        var persisted2 = await context.Products.FindAsync(product2.Id);
        persisted1!.ProductName.Should().Be("Original 1");
        persisted1.UnitPrice.Should().Be(10m);
        persisted2!.ProductName.Should().Be("Original 2");
        persisted2.UnitPrice.Should().Be(20m);
    }

    [Fact]
    public async Task PatchManyAsync_WhenSaveChangesThrowsConcurrencyException_ThrowsAndDoesNotPersist()
    {
        // Arrange
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = CreateConcurrencyContext(connection);
        var repository = CreateConcurrencyBatchRepository(context);

        var product1 = SeedConcurrencyProduct(context, name: "Original 1", unitPrice: 10m, stockQuantity: 1);
        var product2 = SeedConcurrencyProduct(context, name: "Original 2", unitPrice: 20m, stockQuantity: 2);
        var patch1 = DeserializeJsonElement("""
            {"product_name":"Patched 1"}
            """);
        var patch2 = DeserializeJsonElement("""
            {"unit_price":77.77}
            """);
        context.ThrowConcurrencyOnNextSave = true;

        // Act
        var act = () => repository.PatchManyAsync([(product1.Id, patch1), (product2.Id, patch2)]);

        // Assert
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();

        context.ChangeTracker.Clear();
        var persisted1 = await context.Products.FindAsync(product1.Id);
        var persisted2 = await context.Products.FindAsync(product2.Id);
        persisted1!.ProductName.Should().Be("Original 1");
        persisted2!.UnitPrice.Should().Be(20m);
    }

    private static ProductEntity CreateProduct(
        string name = "Test Product",
        decimal unitPrice = 10.00m,
        int stockQuantity = 5,
        bool isActive = true)
    {
        return new ProductEntity
        {
            Id = Guid.NewGuid(),
            ProductName = name,
            UnitPrice = unitPrice,
            StockQuantity = stockQuantity,
            CreatedAt = DateTime.UtcNow,
            IsActive = isActive
        };
    }

    private static ProductEntity CreateUpdatedProduct(
        ProductEntity original,
        string name,
        decimal unitPrice,
        int stockQuantity)
    {
        return new ProductEntity
        {
            Id = original.Id,
            ProductName = name,
            UnitPrice = unitPrice,
            StockQuantity = stockQuantity,
            CreatedAt = original.CreatedAt,
            IsActive = original.IsActive,
            LastModifiedAt = original.LastModifiedAt,
            OptionalDescription = original.OptionalDescription,
            CategoryId = original.CategoryId,
            Status = original.Status
        };
    }

    private static JsonElement DeserializeJsonElement(string json)
    {
        return JsonSerializer.Deserialize<JsonElement>(json);
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

    private static IBatchRepository<ProductEntity, Guid> CreateConcurrencyBatchRepository(ConcurrencyTestDbContext context)
    {
        var options = new EfCoreRepositoryOptions<ProductEntity, Guid>
        {
            KeySelector = product => product.Id
        };

        return new EfCoreRepository<ConcurrencyTestDbContext, ProductEntity, Guid>(context, options);
    }

    private static ProductEntity SeedConcurrencyProduct(
        ConcurrencyTestDbContext context,
        string name,
        decimal unitPrice,
        int stockQuantity)
    {
        var product = CreateProduct(name, unitPrice, stockQuantity);
        context.Products.Add(product);
        context.SaveChanges();
        return product;
    }

    private async Task<ProductEntity[]> SeedProductsAsync(params ProductEntity[] products)
    {
        _db.Products.AddRange(products);
        await _db.SaveChangesAsync();
        return products;
    }
}
