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
}
