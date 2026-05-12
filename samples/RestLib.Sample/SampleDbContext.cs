using Microsoft.EntityFrameworkCore;
using RestLib.Sample.Models;

namespace RestLib.Sample;

/// <summary>
/// EF Core DbContext for the sample application.
/// </summary>
public class SampleDbContext : DbContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SampleDbContext"/> class.
    /// </summary>
    /// <param name="options">The DbContext options.</param>
    public SampleDbContext(DbContextOptions<SampleDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// Gets the customers set.
    /// </summary>
    public DbSet<Customer> Customers => Set<Customer>();

    /// <inheritdoc />
    public override int SaveChanges()
    {
        PreserveCustomerCreatedAt();
        return base.SaveChanges();
    }

    /// <inheritdoc />
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        PreserveCustomerCreatedAt();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <inheritdoc />
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        PreserveCustomerCreatedAt();
        return base.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        PreserveCustomerCreatedAt();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void PreserveCustomerCreatedAt()
    {
        foreach (var entry in ChangeTracker.Entries<Customer>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.CreatedAt == default)
                {
                    entry.Entity.CreatedAt = DateTime.UtcNow;
                }

                continue;
            }

            if (entry.State == EntityState.Modified)
            {
                entry.Property(customer => customer.CreatedAt).IsModified = false;
            }
        }
    }
}
