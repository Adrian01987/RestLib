using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RestLib.EntityFrameworkCore;
using RestLib.FieldSelection;
using RestLib.Filtering;
using RestLib.Serialization;
using RestLib.Sorting;

namespace RestLib.Benchmarks;

/// <summary>
/// Measures recurring EF Core repository construction and projection planning.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class EfCorePlanningBenchmarks
{
    private SqliteConnection _connection = null!;
    private EfCoreBenchmarkDbContext _context = null!;
    private EfCoreRepositoryOptions<EfCoreScalarBenchmarkEntity, Guid> _options = null!;
    private JsonSerializerOptions _jsonOptions = null!;
    private EfCoreProjectionPlanner<EfCoreScalarBenchmarkEntity> _cachedProjectionPlanner = null!;
    private IReadOnlyList<string> _keyPropertyNames = null!;
    private IReadOnlyList<SelectedField> _selectedFields = null!;

    /// <summary>
    /// Creates the SQLite model and warms the stable planning identity.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var dbOptions = new DbContextOptionsBuilder<EfCoreBenchmarkDbContext>()
            .UseSqlite(_connection)
            .Options;
        _context = new EfCoreBenchmarkDbContext(dbOptions);
        _context.Database.EnsureCreated();
        _options = new EfCoreRepositoryOptions<EfCoreScalarBenchmarkEntity, Guid>
        {
            KeySelector = entity => entity.Id,
            EnableProjectionPushdown = true,
            UseAsNoTracking = true
        };
        _jsonOptions = RestLibJsonOptions.CreateDefault();
        _selectedFields =
        [
            new SelectedField
            {
                PropertyName = nameof(EfCoreScalarBenchmarkEntity.Name),
                QueryParameterName = "name"
            },
            new SelectedField
            {
                PropertyName = nameof(EfCoreScalarBenchmarkEntity.Price),
                QueryParameterName = "price"
            }
        ];

        var planningBundle = EfCoreRepositoryPlanCache<EfCoreScalarBenchmarkEntity, Guid>
            .GetOrCreate(_context.Model, _options);
        _keyPropertyNames = planningBundle.KeyMetadata.PropertyNames;
        _cachedProjectionPlanner = new EfCoreProjectionPlanner<EfCoreScalarBenchmarkEntity>(
            static () => true,
            _keyPropertyNames,
            planningBundle.ProjectionPlanningCache);
        _cachedProjectionPlanner.TryBuild(
            _selectedFields,
            Array.Empty<FilterValue>(),
            Array.Empty<SortField>(),
            search: null,
            out _);
    }

    /// <summary>
    /// Disposes benchmark database resources.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    /// <summary>
    /// Constructs a repository with a new options and key-selector identity.
    /// </summary>
    /// <returns>The constructed repository.</returns>
    [BenchmarkCategory("RepositoryConstruction")]
    [Benchmark(Baseline = true, Description = "EF repository: fresh planning identity")]
    public object RepositoryConstructionWithFreshPlanningIdentity()
    {
        var options = new EfCoreRepositoryOptions<EfCoreScalarBenchmarkEntity, Guid>
        {
            KeySelector = entity => entity.Id,
            EnableProjectionPushdown = true,
            UseAsNoTracking = true
        };

        return new EfCoreRepository<
            EfCoreBenchmarkDbContext,
            EfCoreScalarBenchmarkEntity,
            Guid>(_context, options, _jsonOptions);
    }

    /// <summary>
    /// Constructs a repository with the stable options identity used by normal DI registration.
    /// </summary>
    /// <returns>The constructed repository.</returns>
    [BenchmarkCategory("RepositoryConstruction")]
    [Benchmark(Description = "EF repository: cached planning identity")]
    public object RepositoryConstructionWithCachedPlanningIdentity()
    {
        return new EfCoreRepository<
            EfCoreBenchmarkDbContext,
            EfCoreScalarBenchmarkEntity,
            Guid>(_context, _options, _jsonOptions);
    }

    /// <summary>
    /// Resolves a projection through a fresh planner and cache.
    /// </summary>
    /// <returns>The resolved projection plan.</returns>
    [BenchmarkCategory("ProjectionPlanning")]
    [Benchmark(Baseline = true, Description = "EF projection: fresh shape planning")]
    public object ProjectionPlanningWithFreshCache()
    {
        var planner = new EfCoreProjectionPlanner<EfCoreScalarBenchmarkEntity>(
            static () => true,
            _keyPropertyNames);
        planner.TryBuild(
            _selectedFields,
            Array.Empty<FilterValue>(),
            Array.Empty<SortField>(),
            search: null,
            out var plan);

        return plan!;
    }

    /// <summary>
    /// Resolves a recurring normalized projection shape through the shared bounded cache.
    /// </summary>
    /// <returns>The resolved projection plan.</returns>
    [BenchmarkCategory("ProjectionPlanning")]
    [Benchmark(Description = "EF projection: cached normalized shape")]
    public object ProjectionPlanningWithCachedShape()
    {
        _cachedProjectionPlanner.TryBuild(
            _selectedFields,
            Array.Empty<FilterValue>(),
            Array.Empty<SortField>(),
            search: null,
            out var plan);

        return plan!;
    }
}

/// <summary>
/// Measures bounded large batch-key queries through the EF Core repository.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class EfCoreBatchKeyBenchmarks
{
    private SqliteConnection _connection = null!;
    private EfCoreBenchmarkDbContext _context = null!;
    private EfCoreRepository<
        EfCoreBenchmarkDbContext,
        EfCoreScalarBenchmarkEntity,
        Guid> _scalarRepository = null!;
    private EfCoreRepository<
        EfCoreBenchmarkDbContext,
        EfCoreCompositeBenchmarkEntity,
        RestLibCompositeKey<Guid, int>> _compositeRepository = null!;
    private IReadOnlyList<Guid> _scalarKeys = null!;
    private IReadOnlyList<RestLibCompositeKey<Guid, int>> _compositeKeys = null!;

    /// <summary>
    /// Gets or sets the number of distinct keys submitted to each benchmark query.
    /// </summary>
    [Params(512, 2048)]
    public int BatchSize { get; set; }

    /// <summary>
    /// Creates and seeds the SQLite database and warms both repository plans.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var dbOptions = new DbContextOptionsBuilder<EfCoreBenchmarkDbContext>()
            .UseSqlite(_connection)
            .Options;
        _context = new EfCoreBenchmarkDbContext(dbOptions);
        _context.Database.EnsureCreated();

        var scalarEntities = Enumerable.Range(0, BatchSize)
            .Select(index => new EfCoreScalarBenchmarkEntity
            {
                Id = Guid.NewGuid(),
                Name = $"Scalar {index}",
                Price = index
            })
            .ToList();
        var tenantId = Guid.NewGuid();
        var compositeEntities = Enumerable.Range(0, BatchSize)
            .Select(index => new EfCoreCompositeBenchmarkEntity
            {
                TenantId = tenantId,
                Sequence = index,
                Name = $"Composite {index}"
            })
            .ToList();
        _context.ScalarEntities.AddRange(scalarEntities);
        _context.CompositeEntities.AddRange(compositeEntities);
        _context.SaveChanges();
        _context.ChangeTracker.Clear();

        var jsonOptions = RestLibJsonOptions.CreateDefault();
        _scalarRepository = new EfCoreRepository<
            EfCoreBenchmarkDbContext,
            EfCoreScalarBenchmarkEntity,
            Guid>(
                _context,
                new EfCoreRepositoryOptions<EfCoreScalarBenchmarkEntity, Guid>
                {
                    KeySelector = entity => entity.Id,
                    UseAsNoTracking = true
                },
                jsonOptions);
        _compositeRepository = new EfCoreRepository<
            EfCoreBenchmarkDbContext,
            EfCoreCompositeBenchmarkEntity,
            RestLibCompositeKey<Guid, int>>(
                _context,
                new EfCoreRepositoryOptions<
                    EfCoreCompositeBenchmarkEntity,
                    RestLibCompositeKey<Guid, int>>
                {
                    UseAsNoTracking = true
                },
                jsonOptions);

        _scalarKeys = scalarEntities
            .Select(entity => entity.Id)
            .Concat(scalarEntities.Take(BatchSize / 4).Select(entity => entity.Id))
            .ToList();
        _compositeKeys = compositeEntities
            .Select(entity => new RestLibCompositeKey<Guid, int>(entity.TenantId, entity.Sequence))
            .Concat(
                compositeEntities.Take(BatchSize / 4)
                    .Select(entity => new RestLibCompositeKey<Guid, int>(entity.TenantId, entity.Sequence)))
            .ToList();
    }

    /// <summary>
    /// Disposes benchmark database resources.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    /// <summary>
    /// Fetches a large scalar-key set through bounded Contains queries.
    /// </summary>
    /// <returns>The number of distinct entities found.</returns>
    [BenchmarkCategory("ScalarBatchKeyLookup")]
    [Benchmark(Description = "EF batch keys: scalar Contains")]
    public async Task<int> ScalarKeyLookup()
    {
        var result = await _scalarRepository.GetByIdsAsync(_scalarKeys);
        return result.Count;
    }

    /// <summary>
    /// Fetches a large composite-key set through bounded balanced predicates.
    /// </summary>
    /// <returns>The number of distinct entities found.</returns>
    [BenchmarkCategory("CompositeBatchKeyLookup")]
    [Benchmark(Description = "EF batch keys: balanced composite predicates")]
    public async Task<int> CompositeKeyLookup()
    {
        var result = await _compositeRepository.GetByIdsAsync(_compositeKeys);
        return result.Count;
    }
}

/// <summary>
/// SQLite context used by EF Core planning and batch-key benchmarks.
/// </summary>
internal sealed class EfCoreBenchmarkDbContext : DbContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EfCoreBenchmarkDbContext"/> class.
    /// </summary>
    /// <param name="options">The context options.</param>
    internal EfCoreBenchmarkDbContext(DbContextOptions<EfCoreBenchmarkDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// Gets the scalar-key entities.
    /// </summary>
    internal DbSet<EfCoreScalarBenchmarkEntity> ScalarEntities => Set<EfCoreScalarBenchmarkEntity>();

    /// <summary>
    /// Gets the composite-key entities.
    /// </summary>
    internal DbSet<EfCoreCompositeBenchmarkEntity> CompositeEntities => Set<EfCoreCompositeBenchmarkEntity>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EfCoreScalarBenchmarkEntity>()
            .HasKey(entity => entity.Id);
        modelBuilder.Entity<EfCoreCompositeBenchmarkEntity>()
            .HasKey(entity => new { entity.TenantId, entity.Sequence });
    }
}

/// <summary>
/// Scalar-key entity used by EF Core benchmarks.
/// </summary>
internal sealed class EfCoreScalarBenchmarkEntity
{
    /// <summary>Gets or sets the identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the price.</summary>
    public decimal Price { get; set; }
}

/// <summary>
/// Composite-key entity used by EF Core benchmarks.
/// </summary>
internal sealed class EfCoreCompositeBenchmarkEntity
{
    /// <summary>Gets or sets the tenant identifier.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Gets or sets the tenant-local sequence.</summary>
    public int Sequence { get; set; }

    /// <summary>Gets or sets the name.</summary>
    public string Name { get; set; } = string.Empty;
}
