# ADR-007: Field Selection / Sparse Fieldsets

**Status:** Amended
**Date:** 2026-03-30 (amended 2026-04-03, 2026-05-10, 2026-08-05)

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
| **Hybrid (reflection + serialize-then-pick)** | Best-of-both: fast reflection for sparse selections, correct serialize-then-pick for dense selections and edge cases | Slightly more code; two extraction paths must share one shaping contract |

### Nested Property Support

| Option | Pros | Cons |
| --- | --- | --- |
| Support dotted paths (`address.city`) with a flat allow-list of validated scalar paths | More flexible for nested reference data while preserving explicit opt-in | Must reject collection-valued paths and define output shape clearly |
| Top-level properties only | Simple, predictable, easy to secure | Cannot select within nested objects |

## Decision

### 1. ~~Serialize-then-pick projection~~ → Hybrid projection (amended)

**Original decision:** Serialize the full entity to JSON, parse as `JsonDocument`, cherry-pick requested fields.

**Amended decision:** Use a JSON-contract-aware hybrid strategy that selects the fastest
approach based on the ratio of selected fields to total properties:

- **Sparse selections (≤50% of properties):** Read each selected member through its effective `JsonPropertyInfo`/CLR accessor and serialize it individually with the member's effective converter and number-handling metadata.
- **Dense selections (>50% of properties):** Serialize-then-pick. Serialize the entire entity once, parse, and cherry-pick — cheaper than serializing each property individually when most properties are selected.
- **Converter-owned representation fallback:** When `JsonTypeInfo` reports a converter-owned object representation, or a converter owns an intermediate selected path, serialize-then-pick remains authoritative because CLR traversal cannot reproduce that JSON shape.

The threshold is controlled by the `SerializeThresholdRatio` constant (currently `0.5`) in `FieldProjector.cs`.
Both strategies first produce the same flat query-path/value map. A single final shape builder
then applies `FieldSelectionResponseShape`, so the threshold cannot change the public JSON
schema. When dense whole-object serialization omits an explicitly selected member, the dense
path recovers that value through the contract accessor; converter-owned representations
remain authoritative.

```csharp
// Sparse: effective member accessor and member-level serializer metadata
var value = accessor.GetValue(entity);
var element = JsonSerializer.SerializeToElement(
    value,
    accessor.PropertyType,
    accessor.ValueSerializerOptions);

// Dense: serialize whole entity, pick fields from parsed JSON
using var doc = JsonDocument.Parse(JsonSerializer.Serialize(entity, jsonOptions));
```

The accessor cache is built per exact `JsonSerializerOptions` instance and entity type. It is
derived from the effective `JsonTypeInfo` contract and includes:

- Canonical JSON names from naming policies, `[JsonPropertyName]`, and metadata resolvers
- Serializer-provided getters/setters plus CLR access for explicitly selected ignored members
- Member-level converters and number handling
- Converter-owned representation detection, including converters registered in options

Exact serializer-instance identity is part of the cache boundary. Stateful policies or
resolvers of the same CLR type, and distinct converter collections, cannot contaminate one
another's projection metadata.

### 2. Dotted nested reference-property paths with configurable output shape

Nested property paths like `?fields=address.city` are supported when they are explicitly
registered and every intermediate segment is a reference property. Collection-valued paths
such as `items.name` are rejected at configuration time.

Configured query names use `snake_case` per segment joined with dots. For example,
`Customer.Email` becomes `customer.email`.

Query names and response names serve different contracts. The configured query alias remains
what a client supplies in `fields`, but the returned key/path is the canonical name from the
effective JSON member contract. A `[JsonPropertyName]` attribute or metadata resolver can
therefore change the response path without silently changing the configured query vocabulary.

By default, nested selections use dotted keys instead of rebuilding nested objects:

```json
{
  "customer.email": "customer@example.com"
}
```

If an intermediate reference is `null`, the dotted field is returned as JSON `null`.

The field-selection allow-list is an explicit exposure decision. Once a CLR path is
allow-listed and requested, RestLib returns it even if normal full-object serialization would
omit the member because of `[JsonIgnore]`, `DefaultIgnoreCondition`, a conditional ignore rule,
or a null/default value. This keeps sparse and dense selection behavior identical and keeps an
explicitly selected null visible. Applications must not allow-list sensitive members merely
because another serializer rule normally hides them. The exception is a converter-owned
representation: its emitted JSON shape is authoritative, so RestLib does not synthesize a CLR
member that the converter omitted.

Applications can opt field-selection responses into rebuilt nested objects with
`FieldSelectionResponseShape.Nested`. Nested projection builds one mutable JSON tree for the
entire selected field set. Paths that share prefixes are merged into that tree, so selecting
siblings such as `customer.profile.handle` and `customer.profile.display_name` preserves both
values regardless of their order in the request. The selected shape is applied after field
extraction and is therefore identical for sparse, dense, and class-converter projection paths.

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

### Why dotted nested paths were added

1. **Common practical need.** Clients often need a small field from a related reference object such as `customer.email` without wanting the full nested object.
2. **Still explicit and safe.** RestLib keeps a flat allow-list of validated scalar paths rather than exposing arbitrary traversal. Unsupported collection-valued paths fail at configuration time.
3. **Explicit response contract.** Dotted keys remain the backward-compatible default, while applications can opt into rebuilt nested objects. Shared prefixes in nested output are merged without making the response depend on selection order.

## Consequences

- **Field projection uses a hybrid strategy.** Sparse selections use effective contract accessors and member serializer metadata; dense selections serialize the full entity. Both feed one final response-shape builder, so the 50% threshold may be tuned without changing the public schema.
- **Accessor caches are serializer-identity scoped.** Each canonical serializer instance owns entries per projected entity type. The cache is held weakly by options identity, preventing cross-configuration reuse while allowing unused option graphs to be collected.
- **Converter-owned representations use serialize-then-pick.** This includes attribute-based and option-registered converters because the effective `JsonTypeInfo`, rather than attribute reflection alone, decides whether independently addressable members exist.
- **Explicit selection overrides ordinary omission.** An allow-listed selected ignored, default, or null member is emitted; converter-owned representations remain authoritative.
- **Clients can select nested reference-property paths.** If an entity has an `Address` property, clients can request `Address.City` when it is explicitly allow-listed. Responses use the dotted key `address.city` by default or a rebuilt nested object when explicitly configured.
- **Collection-valued paths remain unsupported.** A path such as `Items.Name` is rejected during configuration rather than deferred to request time.
- **ETag is computed from the full entity before projection.** Two requests with different `?fields=` values for the same entity return the same ETag, which is correct — the ETag represents the resource state, not the representation.
- **Write operations are unaffected.** Create, Update, Patch, and Delete always return the full entity (or appropriate status code). Field selection applies only to GetAll and GetById.
