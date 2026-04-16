using Microsoft.EntityFrameworkCore;

namespace RestLib.EntityFrameworkCore.Tests.Fakes;

/// <summary>
/// Test DbContext that can simulate EF Core concurrency exceptions on demand.
/// </summary>
public class ConcurrencyTestDbContext : DbContext
{
    /// <summary>
    /// Gets or sets a value indicating whether the next save operation in any context
    /// instance should throw a <see cref="DbUpdateConcurrencyException"/>.
    /// </summary>
    public static bool ThrowConcurrencyOnNextSaveGlobally { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConcurrencyTestDbContext"/> class.
    /// </summary>
    /// <param name="options">The DbContext options.</param>
    public ConcurrencyTestDbContext(DbContextOptions<ConcurrencyTestDbContext> options)
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

    /// <summary>
    /// Gets or sets a value indicating whether the next save operation should throw
    /// a <see cref="DbUpdateConcurrencyException"/>.
    /// </summary>
    public bool ThrowConcurrencyOnNextSave { get; set; }

    /// <inheritdoc />
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (ThrowConcurrencyOnNextSave)
        {
            ThrowConcurrencyOnNextSave = false;
            throw new DbUpdateConcurrencyException("Simulated concurrency conflict.");
        }

        if (ThrowConcurrencyOnNextSaveGlobally)
        {
            ThrowConcurrencyOnNextSaveGlobally = false;
            throw new DbUpdateConcurrencyException("Simulated concurrency conflict.");
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
