using Microsoft.EntityFrameworkCore;

namespace RestLib.EntityFrameworkCore.Tests.Fakes;

/// <summary>
/// Test DbContext with a configured foreign key relationship between products and categories.
/// </summary>
public class ForeignKeyTestDbContext : DbContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ForeignKeyTestDbContext"/> class.
    /// </summary>
    /// <param name="options">The DbContext options.</param>
    public ForeignKeyTestDbContext(DbContextOptions<ForeignKeyTestDbContext> options)
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

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProductEntity>()
            .HasOne<CategoryEntity>()
            .WithMany()
            .HasForeignKey(product => product.CategoryId);
    }
}
