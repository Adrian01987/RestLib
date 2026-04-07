# ADR-018: PatchAsync Accepts System.Text.Json.JsonElement

**Status:** Accepted
**Date:** 2026-04-07

## Context

`IRepository<TEntity, TKey>.PatchAsync` accepts a `JsonElement patchDocument` parameter
for partial updates (JSON Merge Patch, RFC 7396). This couples every repository
implementation to `System.Text.Json`, which may be undesirable for storage backends
that have their own document representations (e.g., MongoDB with `BsonDocument`, or
EF Core repositories that might prefer `IDictionary<string, object?>`).

## Options Considered

| Option | Pros | Cons |
| --- | --- | --- |
| `JsonElement` (current) | Zero-copy from HTTP body; no conversion; `InMemoryRepository` implementation is straightforward | Couples interface to `System.Text.Json`; forces non-JSON backends to accept JSON |
| `IDictionary<string, object?>` | Backend-agnostic; easy to map to any storage | Lossy type conversion from JSON; requires materializing values; nested objects are awkward |
| Generic `TPatch` parameter | Fully flexible | Adds a third type parameter to `IRepository<TEntity, TKey, TPatch>`; ripples through DI, handlers, and all consumers |
| `ReadOnlyMemory<byte>` (raw UTF-8) | Backend can parse with any library | Defers all parsing to the repository; no compile-time type info; double-parse for JSON backends |

## Decision

Keep `JsonElement` as the patch document type in v1.x.

```csharp
Task<TEntity?> PatchAsync(TKey id, JsonElement patchDocument, CancellationToken ct = default);
```

## Rationale

1. **RestLib is JSON-native:** The library mandates `snake_case` JSON, uses `System.Text.Json`
   throughout serialization, and targets ASP.NET Core Minimal APIs which parse request bodies
   as JSON. The `JsonElement` type is a natural fit.
2. **Zero-copy performance:** ASP.NET Core's `JsonElement` binding reads directly from the
   request body buffer without intermediate allocations. Converting to `IDictionary` or raw
   bytes would add overhead with no benefit for the primary use case.
3. **InMemoryRepository simplicity:** The provided `InMemoryRepository<TEntity, TKey>`
   iterates `JsonElement` properties to apply merge-patch semantics. Using `JsonElement`
   makes this implementation concise and correct.
4. **Breaking change cost:** Changing the signature would break `IRepository`, `IBatchRepository`,
   all handler/helper classes, the `InMemoryRepository` implementation, and every consumer's
   repository. This is disproportionate to the benefit for v1.x.

## Consequences

- Repository implementations **must reference** `System.Text.Json` (included in the
  `Microsoft.AspNetCore.App` shared framework, so no extra NuGet dependency in practice).
- Non-JSON backends (e.g., MongoDB) need to convert `JsonElement` to their native format
  inside `PatchAsync`. This is a localized conversion cost.
- **v2 consideration:** If a future major version introduces pluggable serializers or
  non-JSON transport, this decision should be revisited. The `IDictionary<string, object?>`
  or generic `TPatch` approach would then merit the breaking change cost.

## References

- [RFC 7396 — JSON Merge Patch](https://www.rfc-editor.org/rfc/rfc7396)
- [System.Text.Json — JsonElement](https://learn.microsoft.com/en-us/dotnet/api/system.text.json.jsonelement)
