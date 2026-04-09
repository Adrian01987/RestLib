using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using RestLib.FieldSelection;

namespace RestLib.Benchmarks;

/// <summary>
/// Micro-benchmarks for FieldProjector comparing the old serialize-then-pick
/// approach against the current reflection-based implementation.
/// </summary>
[MemoryDiagnoser]
[MarkdownExporter]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class FieldProjectionBenchmarks
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    // Pre-built field lists for different selection sizes
    private static readonly IReadOnlyList<SelectedField> TwoFields =
    [
        new SelectedField { PropertyName = "Id", QueryParameterName = "id" },
        new SelectedField { PropertyName = "Name", QueryParameterName = "name" },
    ];

    private static readonly IReadOnlyList<SelectedField> FiveFields =
    [
        new SelectedField { PropertyName = "Id", QueryParameterName = "id" },
        new SelectedField { PropertyName = "Name", QueryParameterName = "name" },
        new SelectedField { PropertyName = "Price", QueryParameterName = "price" },
        new SelectedField { PropertyName = "Category", QueryParameterName = "category" },
        new SelectedField { PropertyName = "IsActive", QueryParameterName = "is_active" },
    ];

    private static readonly IReadOnlyList<SelectedField> AllFields =
    [
        new SelectedField { PropertyName = "Id", QueryParameterName = "id" },
        new SelectedField { PropertyName = "Name", QueryParameterName = "name" },
        new SelectedField { PropertyName = "Description", QueryParameterName = "description" },
        new SelectedField { PropertyName = "Price", QueryParameterName = "price" },
        new SelectedField { PropertyName = "Category", QueryParameterName = "category" },
        new SelectedField { PropertyName = "SubCategory", QueryParameterName = "sub_category" },
        new SelectedField { PropertyName = "IsActive", QueryParameterName = "is_active" },
        new SelectedField { PropertyName = "StockQuantity", QueryParameterName = "stock_quantity" },
        new SelectedField { PropertyName = "Rating", QueryParameterName = "rating" },
        new SelectedField { PropertyName = "CreatedAt", QueryParameterName = "created_at" },
        new SelectedField { PropertyName = "UpdatedAt", QueryParameterName = "updated_at" },
        new SelectedField { PropertyName = "Sku", QueryParameterName = "sku" },
        new SelectedField { PropertyName = "Weight", QueryParameterName = "weight" },
        new SelectedField { PropertyName = "Manufacturer", QueryParameterName = "manufacturer" },
        new SelectedField { PropertyName = "Tags", QueryParameterName = "tags" },
    ];

    private RichProduct _singleEntity = null!;
    private IReadOnlyList<RichProduct> _entities10 = null!;
    private IReadOnlyList<RichProduct> _entities100 = null!;
    private IReadOnlyList<RichProduct> _entities1000 = null!;

    [GlobalSetup]
    public void Setup()
    {
        _singleEntity = CreateProduct(0);
        _entities10 = Enumerable.Range(0, 10).Select(CreateProduct).ToList();
        _entities100 = Enumerable.Range(0, 100).Select(CreateProduct).ToList();
        _entities1000 = Enumerable.Range(0, 1000).Select(CreateProduct).ToList();
    }

    // ========== Single Entity — 2 Fields ==========

    [BenchmarkCategory("Single_2Fields")]
    [Benchmark(Description = "Old: Serialize-then-pick (2 fields)")]
    public Dictionary<string, JsonElement> SerializePick_Single_2Fields()
        => OldSerializeThenPick(_singleEntity, TwoFields);

    [BenchmarkCategory("Single_2Fields")]
    [Benchmark(Baseline = true, Description = "New: Hybrid (2 fields)")]
    public Dictionary<string, JsonElement>? Reflection_Single_2Fields()
        => FieldProjector.Project(_singleEntity, TwoFields, JsonOptions);

    // ========== Single Entity — 5 Fields ==========

    [BenchmarkCategory("Single_5Fields")]
    [Benchmark(Description = "Old: Serialize-then-pick (5 fields)")]
    public Dictionary<string, JsonElement> SerializePick_Single_5Fields()
        => OldSerializeThenPick(_singleEntity, FiveFields);

    [BenchmarkCategory("Single_5Fields")]
    [Benchmark(Baseline = true, Description = "New: Hybrid (5 fields)")]
    public Dictionary<string, JsonElement>? Reflection_Single_5Fields()
        => FieldProjector.Project(_singleEntity, FiveFields, JsonOptions);

    // ========== Single Entity — All 15 Fields ==========

    [BenchmarkCategory("Single_AllFields")]
    [Benchmark(Description = "Old: Serialize-then-pick (all 15 fields)")]
    public Dictionary<string, JsonElement> SerializePick_Single_AllFields()
        => OldSerializeThenPick(_singleEntity, AllFields);

    [BenchmarkCategory("Single_AllFields")]
    [Benchmark(Baseline = true, Description = "New: Hybrid (all 15 fields)")]
    public Dictionary<string, JsonElement>? Reflection_Single_AllFields()
        => FieldProjector.Project(_singleEntity, AllFields, JsonOptions);

    // ========== ProjectMany — 10 entities ==========

    [BenchmarkCategory("Many_10x5Fields")]
    [Benchmark(Description = "Old: Serialize-then-pick (10 entities, 5 fields)")]
    public List<Dictionary<string, JsonElement>> SerializePick_10_5Fields()
        => OldSerializeThenPickMany(_entities10, FiveFields);

    [BenchmarkCategory("Many_10x5Fields")]
    [Benchmark(Baseline = true, Description = "New: Hybrid (10 entities, 5 fields)")]
    public IReadOnlyList<Dictionary<string, JsonElement>>? Reflection_10_5Fields()
        => FieldProjector.ProjectMany(_entities10, FiveFields, JsonOptions);

    // ========== ProjectMany — 100 entities ==========

    [BenchmarkCategory("Many_100x5Fields")]
    [Benchmark(Description = "Old: Serialize-then-pick (100 entities, 5 fields)")]
    public List<Dictionary<string, JsonElement>> SerializePick_100_5Fields()
        => OldSerializeThenPickMany(_entities100, FiveFields);

    [BenchmarkCategory("Many_100x5Fields")]
    [Benchmark(Baseline = true, Description = "New: Hybrid (100 entities, 5 fields)")]
    public IReadOnlyList<Dictionary<string, JsonElement>>? Reflection_100_5Fields()
        => FieldProjector.ProjectMany(_entities100, FiveFields, JsonOptions);

    // ========== ProjectMany — 1000 entities ==========

    [BenchmarkCategory("Many_1000x5Fields")]
    [Benchmark(Description = "Old: Serialize-then-pick (1000 entities, 5 fields)")]
    public List<Dictionary<string, JsonElement>> SerializePick_1000_5Fields()
        => OldSerializeThenPickMany(_entities1000, FiveFields);

    [BenchmarkCategory("Many_1000x5Fields")]
    [Benchmark(Baseline = true, Description = "New: Hybrid (1000 entities, 5 fields)")]
    public IReadOnlyList<Dictionary<string, JsonElement>>? Reflection_1000_5Fields()
        => FieldProjector.ProjectMany(_entities1000, FiveFields, JsonOptions);

    // ========== ProjectMany — 100 entities, all fields ==========

    [BenchmarkCategory("Many_100xAllFields")]
    [Benchmark(Description = "Old: Serialize-then-pick (100 entities, all 15 fields)")]
    public List<Dictionary<string, JsonElement>> SerializePick_100_AllFields()
        => OldSerializeThenPickMany(_entities100, AllFields);

    [BenchmarkCategory("Many_100xAllFields")]
    [Benchmark(Baseline = true, Description = "New: Hybrid (100 entities, all 15 fields)")]
    public IReadOnlyList<Dictionary<string, JsonElement>>? Reflection_100_AllFields()
        => FieldProjector.ProjectMany(_entities100, AllFields, JsonOptions);

    // ========== ProjectMany — 1000 entities, all fields ==========

    [BenchmarkCategory("Many_1000xAllFields")]
    [Benchmark(Description = "Old: Serialize-then-pick (1000 entities, all 15 fields)")]
    public List<Dictionary<string, JsonElement>> SerializePick_1000_AllFields()
        => OldSerializeThenPickMany(_entities1000, AllFields);

    [BenchmarkCategory("Many_1000xAllFields")]
    [Benchmark(Baseline = true, Description = "New: Hybrid (1000 entities, all 15 fields)")]
    public IReadOnlyList<Dictionary<string, JsonElement>>? Reflection_1000_AllFields()
        => FieldProjector.ProjectMany(_entities1000, AllFields, JsonOptions);

    // ========== Old serialize-then-pick implementation (for comparison) ==========

    private static Dictionary<string, JsonElement> OldSerializeThenPick<TEntity>(
        TEntity entity,
        IReadOnlyList<SelectedField> selectedFields)
    {
        var json = JsonSerializer.Serialize(entity, JsonOptions);
        using var doc = JsonDocument.Parse(json);

        var result = new Dictionary<string, JsonElement>(selectedFields.Count);

        foreach (var field in selectedFields)
        {
            if (doc.RootElement.TryGetProperty(field.QueryParameterName, out var value))
            {
                result[field.QueryParameterName] = value.Clone();
            }
        }

        return result;
    }

    private static List<Dictionary<string, JsonElement>> OldSerializeThenPickMany<TEntity>(
        IReadOnlyList<TEntity> entities,
        IReadOnlyList<SelectedField> selectedFields)
    {
        var results = new List<Dictionary<string, JsonElement>>(entities.Count);
        foreach (var entity in entities)
        {
            results.Add(OldSerializeThenPick(entity, selectedFields));
        }

        return results;
    }

    // ========== Test data ==========

    private static RichProduct CreateProduct(int index) => new()
    {
        Id = Guid.NewGuid(),
        Name = $"Product {index}",
        Description = $"A detailed description for product {index} with enough text to be realistic.",
        Price = 10.00m + (index * 1.5m),
        Category = "Electronics",
        SubCategory = "Gadgets",
        IsActive = index % 3 != 0,
        StockQuantity = 100 + index,
        Rating = 3.5 + ((index % 10) * 0.15),
        CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(index),
        UpdatedAt = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(index),
        Sku = $"SKU-{index:D6}",
        Weight = 0.5 + (index * 0.1),
        Manufacturer = $"Manufacturer {index % 5}",
        Tags = [$"tag-{index % 3}", $"tag-{index % 7}", "common"],
    };
}

/// <summary>
/// A product entity with 15 properties to represent a realistic model.
/// </summary>
public class RichProduct
{
    /// <summary>Gets or sets the product ID.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the product name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the product description.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Gets or sets the product price.</summary>
    public decimal Price { get; set; }

    /// <summary>Gets or sets the product category.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Gets or sets the product sub-category.</summary>
    public string SubCategory { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the product is active.</summary>
    public bool IsActive { get; set; }

    /// <summary>Gets or sets the stock quantity.</summary>
    public int StockQuantity { get; set; }

    /// <summary>Gets or sets the product rating.</summary>
    public double Rating { get; set; }

    /// <summary>Gets or sets the creation timestamp.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Gets or sets the last update timestamp.</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>Gets or sets the SKU code.</summary>
    public string Sku { get; set; } = string.Empty;

    /// <summary>Gets or sets the product weight.</summary>
    public double Weight { get; set; }

    /// <summary>Gets or sets the manufacturer name.</summary>
    public string Manufacturer { get; set; } = string.Empty;

    /// <summary>Gets or sets the product tags.</summary>
    public List<string> Tags { get; set; } = [];
}
