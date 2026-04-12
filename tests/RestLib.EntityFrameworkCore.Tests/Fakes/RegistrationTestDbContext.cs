using Microsoft.EntityFrameworkCore;

namespace RestLib.EntityFrameworkCore.Tests.Fakes;

/// <summary>
/// Entity type used for EF Core service registration tests.
/// </summary>
public class RegistrationTestEntity
{
    /// <summary>
    /// Gets or sets the entity identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the entity name.
    /// </summary>
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Entity type that is intentionally not exposed by the test DbContext.
/// </summary>
public class OrphanEntity
{
    /// <summary>
    /// Gets or sets the entity identifier.
    /// </summary>
    public Guid Id { get; set; }
}

/// <summary>
/// Entity type with a composite primary key for key detection tests.
/// </summary>
public class CompositeKeyEntity
{
    /// <summary>
    /// Gets or sets the first key value.
    /// </summary>
    public Guid Key1 { get; set; }

    /// <summary>
    /// Gets or sets the second key value.
    /// </summary>
    public int Key2 { get; set; }

    /// <summary>
    /// Gets or sets the entity name.
    /// </summary>
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Entity type with an integer primary key for key type mismatch tests.
/// </summary>
public class IntKeyEntity
{
    /// <summary>
    /// Gets or sets the entity identifier.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the entity name.
    /// </summary>
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Minimal DbContext used for EF Core service registration tests.
/// </summary>
public class RegistrationTestDbContext : DbContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RegistrationTestDbContext"/> class.
    /// </summary>
    /// <param name="options">The DbContext options.</param>
    public RegistrationTestDbContext(DbContextOptions<RegistrationTestDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// Gets or sets the registration test entities.
    /// </summary>
    public DbSet<RegistrationTestEntity> Entities { get; set; } = null!;
}

/// <summary>
/// DbContext used for EF Core key detection tests.
/// </summary>
public class KeyDetectionTestDbContext : DbContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KeyDetectionTestDbContext"/> class.
    /// </summary>
    /// <param name="options">The DbContext options.</param>
    public KeyDetectionTestDbContext(DbContextOptions<KeyDetectionTestDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// Gets or sets the registration test entities.
    /// </summary>
    public DbSet<RegistrationTestEntity> Entities { get; set; } = null!;

    /// <summary>
    /// Gets or sets the composite-key test entities.
    /// </summary>
    public DbSet<CompositeKeyEntity> CompositeKeyEntities { get; set; } = null!;

    /// <summary>
    /// Gets or sets the integer-key test entities.
    /// </summary>
    public DbSet<IntKeyEntity> IntKeyEntities { get; set; } = null!;

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CompositeKeyEntity>()
            .HasKey(entity => new { entity.Key1, entity.Key2 });
    }
}
