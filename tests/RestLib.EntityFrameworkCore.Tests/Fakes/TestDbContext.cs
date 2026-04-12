using Microsoft.EntityFrameworkCore;

namespace RestLib.EntityFrameworkCore.Tests.Fakes;

/// <summary>
/// Shared DbContext for EF Core integration tests.
/// Uses SQLite in-memory provider with per-test database creation.
/// </summary>
public class TestDbContext : DbContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TestDbContext"/> class.
    /// </summary>
    /// <param name="options">The DbContext options.</param>
    public TestDbContext(DbContextOptions<TestDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// Gets or sets the products table.
    /// </summary>
    public DbSet<ProductEntity> Products { get; set; } = null!;

    /// <summary>
    /// Gets or sets the categories table.
    /// </summary>
    public DbSet<CategoryEntity> Categories { get; set; } = null!;
}
