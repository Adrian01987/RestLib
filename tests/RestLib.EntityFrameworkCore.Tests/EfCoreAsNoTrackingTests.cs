using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RestLib.Abstractions;
using RestLib.EntityFrameworkCore.Tests.Fakes;
using RestLib.Pagination;
using Xunit;

namespace RestLib.EntityFrameworkCore.Tests;

/// <summary>
/// Verifies that read operations use <c>AsNoTracking</c> by default and that
/// write operations always track entities regardless of the <c>UseAsNoTracking</c>
/// setting.
/// </summary>
[Trait("Category", "Story10.1")]
public class EfCoreAsNoTrackingTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private TestDbContext _context = null!;
    private EfCoreRepository<TestDbContext, ProductEntity, Guid> _trackingRepo = null!;
    private EfCoreRepository<TestDbContext, ProductEntity, Guid> _noTrackingRepo = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new TestDbContext(options);
        await _context.Database.EnsureCreatedAsync();

        var noTrackingOptions = new EfCoreRepositoryOptions<ProductEntity, Guid>
        {
            KeySelector = entity => entity.Id,
            UseAsNoTracking = true
        };
        _noTrackingRepo = new EfCoreRepository<TestDbContext, ProductEntity, Guid>(_context, noTrackingOptions);

        var trackingOptions = new EfCoreRepositoryOptions<ProductEntity, Guid>
        {
            KeySelector = entity => entity.Id,
            UseAsNoTracking = false
        };
        _trackingRepo = new EfCoreRepository<TestDbContext, ProductEntity, Guid>(_context, trackingOptions);
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task GetByIdAsync_WithAsNoTracking_DoesNotTrackEntity()
    {
        // Arrange
        var product = CreateProduct(name: "Untracked");
        await SeedProductsAsync(product);
        _context.ChangeTracker.Clear();

        // Act
        var result = await _noTrackingRepo.GetByIdAsync(product.Id);

        // Assert
        result.Should().NotBeNull();
        result!.ProductName.Should().Be("Untracked");
        _context.ChangeTracker.Entries().Should().BeEmpty(
            "GetByIdAsync with UseAsNoTracking=true should not add entities to the change tracker");
    }

    [Fact]
    public async Task GetAllAsync_WithAsNoTracking_DoesNotTrackEntities()
    {
        // Arrange
        await SeedProductsAsync(
            CreateProduct(name: "Product 1"),
            CreateProduct(name: "Product 2"),
            CreateProduct(name: "Product 3"));
        _context.ChangeTracker.Clear();

        var request = CreatePaginationRequest();

        // Act
        var result = await _noTrackingRepo.GetAllAsync(request);

        // Assert
        result.Items.Should().HaveCount(3);
        _context.ChangeTracker.Entries().Should().BeEmpty(
            "GetAllAsync with UseAsNoTracking=true should not add entities to the change tracker");
    }

    [Fact]
    public async Task GetByIdsAsync_WithAsNoTracking_DoesNotTrackEntities()
    {
        // Arrange
        var product1 = CreateProduct(name: "Product 1");
        var product2 = CreateProduct(name: "Product 2");
        await SeedProductsAsync(product1, product2);
        _context.ChangeTracker.Clear();

        var batchRepo = (IBatchRepository<ProductEntity, Guid>)_noTrackingRepo;

        // Act
        var result = await batchRepo.GetByIdsAsync([product1.Id, product2.Id]);

        // Assert
        result.Should().HaveCount(2);
        _context.ChangeTracker.Entries().Should().BeEmpty(
            "GetByIdsAsync with UseAsNoTracking=true should not add entities to the change tracker");
    }

    [Fact]
    public async Task GetByIdAsync_WithTrackingEnabled_TracksEntity()
    {
        // Arrange
        var product = CreateProduct(name: "Tracked");
        await SeedProductsAsync(product);
        _context.ChangeTracker.Clear();

        // Act
        var result = await _trackingRepo.GetByIdAsync(product.Id);

        // Assert
        result.Should().NotBeNull();
        _context.ChangeTracker.Entries<ProductEntity>().Should().HaveCount(1,
            "GetByIdAsync with UseAsNoTracking=false should track the returned entity");
    }

    [Fact]
    public async Task CreateAsync_AlwaysTracksEntity()
    {
        // Arrange
        _context.ChangeTracker.Clear();
        var product = CreateProduct(name: "New Product");

        // Act
        var result = await _noTrackingRepo.CreateAsync(product);

        // Assert
        result.Should().NotBeNull();
        result.ProductName.Should().Be("New Product");
        _context.ChangeTracker.Entries<ProductEntity>().Should().NotBeEmpty(
            "CreateAsync should always track the entity regardless of UseAsNoTracking setting");
    }

    [Fact]
    public async Task UpdateAsync_AlwaysTracksEntity()
    {
        // Arrange
        var product = CreateProduct(name: "Original");
        await SeedProductsAsync(product);
        _context.ChangeTracker.Clear();

        var updatedProduct = CreateProduct(name: "Updated");
        updatedProduct.Id = product.Id;
        updatedProduct.CreatedAt = product.CreatedAt;

        // Act
        var result = await _noTrackingRepo.UpdateAsync(product.Id, updatedProduct);

        // Assert
        result.Should().NotBeNull();
        result!.ProductName.Should().Be("Updated");
        _context.ChangeTracker.Entries<ProductEntity>().Should().NotBeEmpty(
            "UpdateAsync should always track the entity regardless of UseAsNoTracking setting");
    }

    [Fact]
    public async Task DeleteAsync_AlwaysTracksEntity()
    {
        // Arrange
        var product = CreateProduct(name: "ToDelete");
        await SeedProductsAsync(product);
        _context.ChangeTracker.Clear();

        // Act
        var result = await _noTrackingRepo.DeleteAsync(product.Id);

        // Assert
        result.Should().BeTrue();
        _context.ChangeTracker.Clear();
        var deleted = await _context.Products.FindAsync(product.Id);
        deleted.Should().BeNull("the entity should be deleted from the database");
    }

    /// <summary>
    /// Creates a pagination request with default values for repository tests.
    /// </summary>
    /// <returns>A default pagination request.</returns>
    private static PaginationRequest CreatePaginationRequest()
    {
        return new PaginationRequest
        {
            Filters = [],
            SortFields = [],
            Limit = 100,
            Cursor = string.Empty
        };
    }

    /// <summary>
    /// Creates a test product entity.
    /// </summary>
    /// <param name="name">The product name.</param>
    /// <param name="unitPrice">The unit price.</param>
    /// <param name="stockQuantity">The stock quantity.</param>
    /// <param name="isActive">Whether the product is active.</param>
    /// <returns>A configured product entity.</returns>
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

    /// <summary>
    /// Seeds products directly via the context, then clears the change tracker.
    /// </summary>
    /// <param name="products">The products to seed.</param>
    /// <returns>A task that completes when seeding is done.</returns>
    private async Task SeedProductsAsync(params ProductEntity[] products)
    {
        _context.Products.RemoveRange(_context.Products);
        await _context.SaveChangesAsync();
        _context.Products.AddRange(products);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
    }
}
