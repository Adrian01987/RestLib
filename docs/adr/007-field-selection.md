# ADR-007: Field Selection / Sparse Fieldsets

**Status:** Accepted
**Date:** 2026-03-30

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

### Nested Property Support

| Option | Pros | Cons |
| --- | --- | --- |
| Support dotted paths (`address.city`) | More flexible for deeply nested models | Significant parser complexity; security risk (traversal into unintended structures); allow-list becomes a tree instead of a flat list |
| Top-level properties only | Simple, predictable, easy to secure | Cannot select within nested objects |

## Decision

### 1. Serialize-then-pick projection

We serialize the full entity to JSON using the configured `JsonSerializerOptions` (which includes `SnakeCaseLower` naming policy, `WhenWritingNull` ignore rules, and any custom converters), parse the result as a `JsonDocument`, then copy only the requested fields into a `Dictionary<string, JsonElement>`.

```csharp
using var doc = JsonDocument.Parse(JsonSerializer.Serialize(entity, jsonOptions));
foreach (var field in selectedFields)
{
    if (doc.RootElement.TryGetProperty(field.QueryFieldName, out var value))
    {
        result[field.QueryFieldName] = value.Clone();
    }
}
```

`value.Clone()` is required because the `JsonDocument` is disposed after the block.

### 2. Top-level properties only

Nested property paths like `?fields=address.city` are not supported. Requesting such a path returns a 400 Problem Details response because `address.city` will not match any entry in the flat allow-list.

## Rationale

### Serialize-then-pick

1. **Correctness by construction.** The JSON output already reflects the exact naming policy, ignore rules, and custom converters configured in `RestLibJsonOptions`. Reflection-based approaches would need to independently replicate all of these behaviors, creating a maintenance burden and risk of drift.
2. **Simplicity.** The implementation is under 30 lines of code in `FieldProjector.cs`. No reflection caching, no expression tree compilation, no naming policy re-implementation.
3. **Acceptable performance.** For typical REST API page sizes (20-100 items, 10-30 properties per entity), the extra serialize-parse cycle adds sub-millisecond overhead. The `InMemoryRepository` benchmarks show RestLib's GetAll is already faster than hand-written Minimal APIs, providing headroom.
4. **Future-proof.** If `System.Text.Json` adds new features (e.g., new converters, new ignore conditions), the projection automatically respects them without code changes.

### Top-level only

1. **Security.** A flat allow-list is easy to reason about and audit. Nested paths would require tree-structured allow-lists with potential for traversal into unintended object graphs.
2. **Simplicity.** The parser, validator, and projector all operate on a simple string list. No recursive descent, no path splitting, no ambiguity about array indexing.
3. **Sufficient for common use cases.** Most REST API field selection (JSON:API, Google API, Stripe) uses top-level fields. Nested selection can be added in a future version if demand materializes.

## Consequences

- **Field projection adds one serialize + parse cycle per entity.** Profiling should guide optimization if this becomes a bottleneck for large payloads.
- **Clients cannot select within nested objects.** If an entity has an `Address` property, clients can include or exclude the entire `Address` object but cannot request only `Address.City`.
- **ETag is computed from the full entity before projection.** Two requests with different `?fields=` values for the same entity return the same ETag, which is correct — the ETag represents the resource state, not the representation.
- **Write operations are unaffected.** Create, Update, Patch, and Delete always return the full entity (or appropriate status code). Field selection applies only to GetAll and GetById.
