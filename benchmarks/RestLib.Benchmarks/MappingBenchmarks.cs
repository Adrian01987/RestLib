using System.Reflection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using RestLib.Mapping;

namespace RestLib.Benchmarks;

/// <summary>
/// Compares the former per-property reflection mapping path with RestLib's cached compiled mapper.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class MappingBenchmarks
{
    private IReadOnlyList<BenchmarkEntity> _entities = null!;
    private LegacyReflectionMapper _legacyMapper = null!;
    private ReflectionRestLibMapper<BenchmarkDto, BenchmarkEntity> _compiledMapper = null!;

    /// <summary>
    /// Initializes benchmark inputs and warms the shared compiled mapping plan.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _entities = Enumerable.Range(0, 1000)
            .Select(index => new BenchmarkEntity
            {
                Id = Guid.NewGuid(),
                Name = $"Product {index}",
                Price = 10m + index,
                StockQuantity = index,
                IsActive = index % 2 == 0,
                CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(index),
            })
            .ToList();
        _legacyMapper = new LegacyReflectionMapper();
        _compiledMapper = ReflectionRestLibMapper<BenchmarkDto, BenchmarkEntity>.Shared;
    }

    /// <summary>
    /// Maps 1,000 models through the former PropertyInfo access path.
    /// </summary>
    /// <returns>The mapped models.</returns>
    [BenchmarkCategory("Mapping_1000")]
    [Benchmark(Baseline = true, Description = "Legacy: PropertyInfo mapping (1,000 models)")]
    public IReadOnlyList<BenchmarkDto> LegacyReflectionMapping()
    {
        return _entities.Select(_legacyMapper.ToApi).ToList();
    }

    /// <summary>
    /// Maps 1,000 models through the cached compiled mapping delegate.
    /// </summary>
    /// <returns>The mapped models.</returns>
    [BenchmarkCategory("Mapping_1000")]
    [Benchmark(Description = "RestLib: compiled mapping (1,000 models)")]
    public IReadOnlyList<BenchmarkDto> CompiledMapping()
    {
        return _entities.Select(_compiledMapper.ToApi).ToList();
    }

    /// <summary>
    /// Builds the reflection metadata used by the former mapper implementation.
    /// </summary>
    /// <returns>A newly constructed legacy mapper.</returns>
    [BenchmarkCategory("MapperConstruction")]
    [Benchmark(Baseline = true, Description = "Legacy: discover mapping metadata")]
    public object LegacyMapperConstruction()
    {
        return new LegacyReflectionMapper();
    }

    /// <summary>
    /// Resolves RestLib's shared mapper after its compiled plan has been warmed.
    /// </summary>
    /// <returns>The shared mapper.</returns>
    [BenchmarkCategory("MapperConstruction")]
    [Benchmark(Description = "RestLib: resolve cached mapper")]
    public object CachedMapperResolution()
    {
        return ReflectionRestLibMapper<BenchmarkDto, BenchmarkEntity>.Shared;
    }

    /// <summary>
    /// API model used by the mapping benchmark.
    /// </summary>
    public sealed class BenchmarkDto
    {
        /// <summary>Gets or sets the identifier.</summary>
        public Guid Id { get; set; }

        /// <summary>Gets or sets the name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets the price.</summary>
        public decimal Price { get; set; }

        /// <summary>Gets or sets the stock quantity.</summary>
        public int StockQuantity { get; set; }

        /// <summary>Gets or sets a value indicating whether the model is active.</summary>
        public bool IsActive { get; set; }

        /// <summary>Gets or sets the creation time.</summary>
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// Persistence model used by the mapping benchmark.
    /// </summary>
    public sealed class BenchmarkEntity
    {
        /// <summary>Gets or sets the identifier.</summary>
        public Guid Id { get; set; }

        /// <summary>Gets or sets the name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets the price.</summary>
        public decimal Price { get; set; }

        /// <summary>Gets or sets the stock quantity.</summary>
        public int StockQuantity { get; set; }

        /// <summary>Gets or sets a value indicating whether the model is active.</summary>
        public bool IsActive { get; set; }

        /// <summary>Gets or sets the creation time.</summary>
        public DateTime CreatedAt { get; set; }
    }

    private sealed class LegacyReflectionMapper
    {
        private readonly IReadOnlyList<(PropertyInfo Source, PropertyInfo Destination)> _mappings;

        internal LegacyReflectionMapper()
        {
            _mappings = typeof(BenchmarkDto)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .OrderBy(property => property.Name, StringComparer.Ordinal)
                .Select(destination =>
                    (typeof(BenchmarkEntity).GetProperty(destination.Name)!, destination))
                .ToList();
        }

        internal BenchmarkDto ToApi(BenchmarkEntity entity)
        {
            var dto = new BenchmarkDto();
            foreach (var mapping in _mappings)
            {
                mapping.Destination.SetValue(dto, mapping.Source.GetValue(entity));
            }

            return dto;
        }
    }
}
