# ADR-007: Field Selection / Sparse Fieldsets

**Status:** Amended
**Date:** 2026-03-30 (amended 2026-04-03)

## Context

Clients consuming REST APIs often need only a subset of an entity's properties. Returning all fields on every request wastes bandwidth, leaks internal data, and forces frontend teams to filter payloads on the client side. A field selection mechanism (also called sparse fieldsets) lets clients request only the properties they need via a `?fields=` query parameter.

Two key design decisions arise from this:

1. **How to project entities to a subset of fields** — at the C# object level or at the JSON level?
2. **Whether to support nested property paths** (e.g., `?fields=address.city`).

## Options Considered

### Projection Strategy

| Option | Pros | Cons |
| --- | --- | --- |
| Reflection-based (read properties via `PropertyInfo`) | No serialization overhead | Ignores JSON naming policy; must rebuild snake_case mapping; doesn't handle `[JsonIgnore]`, custom converters, or computed JSON properties |
| Expression tree compilation | Fast after initial compile | Same naming/converter mismatch problems as reflection; complex to implement |
| Serialize-then-pick (serialize full entity to JSON, parse, cherry-pick fields) | Respects all `System.Text.Json` configuration (naming policy, converters, ignore rules); simple implementation | One extra serialize + parse cycle per entity |
| **Hybrid (reflection + serialize-then-pick)** | Best-of-both: fast reflection for sparse selections, correct serialize-then-pick for dense selections and edge cases | Slightly more code; two code paths to maintain |

### Nested Property Support

| Option | Pros | Cons |
| --- | --- | --- |
| Support dotted paths (`address.city`) | More flexible for deeply nested models | Significant parser complexity; security risk (traversal into unintended structures); allow-list becomes a tree instead of a flat list |
| Top-level properties only | Simple, predictable, easy to secure | Cannot select within nested objects |

## Decision

### 1. ~~Serialize-then-pick projection~~ → Hybrid projection (amended)

**Original decision:** Serialize the full entity to JSON, parse as `JsonDocument`, cherry-pick requested fields.

**Amended decision:** Use a hybrid strategy that selects the fastest approach based on the ratio of selected fields to total properties:

- **Sparse selections (≤50% of properties):** Per-property reflection with compiled expression tree getters. Each selected property value is read via a cached compiled delegate and serialized individually with `JsonSerializer.SerializeToElement()`.
- **Dense selections (>50% of properties):** Serialize-then-pick. Serialize the entire entity once, parse, and cherry-pick — cheaper than serializing each property individually when most properties are selected.
- **Class-level `[JsonConverter]` fallback:** Types with a class-level `JsonConverterAttribute` always use serialize-then-pick, because per-property reflection cannot replicate the converter's custom serialization logic.

The threshold is controlled by the `SerializeThresholdRatio` constant (currently `0.5`) in `FieldProjector.cs`.

```csharp
// Sparse: compiled expression tree getter per property
var value = accessor.GetValue(entity);
var element = JsonSerializer.SerializeToElement(value, accessor.PropertyType, jsonOptions);

// Dense: serialize whole entity, pick fields from parsed JSON
using var doc = JsonDocument.Parse(JsonSerializer.Serialize(entity, jsonOptions));
```

The accessor cache (`ConcurrentDictionary<Type, PropertyAccessorMap>`) is built once per entity type and includes:
- Compiled `Func<object, object?>` getters via expression trees
- JSON property name resolution respecting `[JsonPropertyName]` and `JsonNamingPolicy`
- `[JsonIgnore]` filtering
- Class-level `[JsonConverter]` detection

### 2. Top-level properties only

Nested property paths like `?fields=address.city` are not supported. Requesting such a path returns a 400 Problem Details response because `address.city` will not match any entry in the flat allow-list.

## Rationale

### Why the original serialize-then-pick was amended

Benchmarking revealed that serialize-then-pick has significant overhead for sparse selections — the common case where clients request 2-5 fields out of 10-20. The cost of serializing the *entire* entity just to discard most of the output is wasteful when only a few properties are needed.

However, pure per-property reflection is slower than serialize-then-pick for dense selections (selecting most or all properties), because calling `SerializeToElement()` per property has higher per-call overhead than serializing the whole object once.

The hybrid approach delivers the best of both strategies.

### Benchmark results

Micro-benchmarks comparing old (serialize-then-pick only) vs new (hybrid) on a 15-property entity:

| Scenario | Old (serialize-then-pick) | New (hybrid) | Speedup | Memory reduction |
| --- | --- | --- | --- | --- |
| 1 entity, 2 fields | 7.7 μs | 3.0 μs | **2.6×** | 2.6× less |
| 1 entity, 5 fields (33%) | 20.5 μs | 5.2 μs | **4.0×** | 1.8× less |
| 1 entity, all 15 fields | 22.7 μs | 24.2 μs | ~same | ~same |
| 100 entities, 5 fields | 3.9 ms | 1.7 ms | **2.3×** | 1.8× less |
| 1000 entities, 5 fields | 178 ms | 34 ms | **5.2×** | 1.9× less |
| 100 entities, all fields | ~same | ~same | — | — |
| 1000 entities, all fields | ~same | ~same | — | — |

Key observations:
- Sparse selections (the common case) are **2-5× faster** with the hybrid approach
- Dense selections correctly fall back to serialize-then-pick with no regression
- The 50% threshold provides a clean crossover point between the two strategies

### Top-level only

1. **Security.** A flat allow-list is easy to reason about and audit. Nested paths would require tree-structured allow-lists with potential for traversal into unintended object graphs.
2. **Simplicity.** The parser, validator, and projector all operate on a simple string list. No recursive descent, no path splitting, no ambiguity about array indexing.
3. **Sufficient for common use cases.** Most REST API field selection (JSON:API, Google API, Stripe) uses top-level fields. Nested selection can be added in a future version if demand materializes.

## Consequences

- **Field projection uses a hybrid strategy.** Sparse selections use per-property reflection with compiled getters; dense selections serialize the full entity. The 50% threshold may be tuned based on future profiling.
- **Accessor cache grows per entity type.** Each entity type registered with field selection adds one entry to a `ConcurrentDictionary`. This is bounded by the number of entity types and is negligible in practice.
- **Types with class-level `[JsonConverter]` always use serialize-then-pick.** This is correct because the converter may produce JSON that doesn't correspond to individual properties.
- **Clients cannot select within nested objects.** If an entity has an `Address` property, clients can include or exclude the entire `Address` object but cannot request only `Address.City`.
- **ETag is computed from the full entity before projection.** Two requests with different `?fields=` values for the same entity return the same ETag, which is correct — the ETag represents the resource state, not the representation.
- **Write operations are unaffected.** Create, Update, Patch, and Delete always return the full entity (or appropriate status code). Field selection applies only to GetAll and GetById.
