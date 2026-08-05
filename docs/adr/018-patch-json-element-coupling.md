# ADR-018: PatchAsync Accepts System.Text.Json.JsonElement

**Status:** Amended
**Date:** 2026-04-07 (amended 2026-08-05)

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

Interpret PATCH members through the effective `JsonTypeInfo` produced by RestLib's canonical
ASP.NET Core `JsonOptions.SerializerOptions` instance. Canonical JSON member names are
authoritative. Legacy CLR, snake_case, and camelCase aliases remain accepted for backward
compatibility only while `PropertyNameCaseInsensitive` is enabled; disabling it makes PATCH
member resolution exact just as it does for normal request deserialization.

## Rationale

1. **RestLib is JSON-native:** The library mandates `snake_case` JSON, uses `System.Text.Json`
   throughout serialization, and targets ASP.NET Core Minimal APIs which parse request bodies
   as JSON. The `JsonElement` type is a natural fit.
2. **Zero-copy performance:** ASP.NET Core's `JsonElement` binding reads directly from the
   request body buffer without intermediate allocations. Converting to `IDictionary` or raw
   bytes would add overhead with no benefit for the primary use case.
3. **InMemoryRepository simplicity:** The provided `InMemoryRepository<TEntity, TKey>`
   delegates the original serialized representation and patch document to the shared
   RFC 7396 merge engine. Keeping values as `JsonElement` preserves nested structure
   and exact JSON number tokens without an intermediate object graph.
4. **One effective contract:** Request binding, core preview, standard InMemory persistence,
   EF Core patch planning, response writing, and ETag generation resolve the same serializer
   metadata. Naming policies, `[JsonPropertyName]`, metadata resolvers, ignored members,
   member converters, and number-handling rules no longer need adapter-specific emulation.
5. **Breaking change cost:** Changing the signature would break `IRepository`, `IBatchRepository`,
   all handler/helper classes, the `InMemoryRepository` implementation, and every consumer's
   repository. This is disproportionate to the benefit for v1.x.

## Consequences

- Repository implementations **must reference** `System.Text.Json` (included in the
  `Microsoft.AspNetCore.App` shared framework, so no extra NuGet dependency in practice).
- Non-JSON backends (e.g., MongoDB) need to convert `JsonElement` to their native format
  inside `PatchAsync`. This is a localized conversion cost.
- Core preview validation, InMemory persistence, and EF Core property persistence use
  the same recursive merge algorithm. Objects merge recursively, `null` removes a
  member, and arrays or non-object values replace the previous value.
- Merge input and output use ASP.NET Core's canonical `JsonOptions.SerializerOptions`,
  including naming and case policies, metadata resolvers, ignore rules, converters, and
  number handling. Member-level converter and number metadata is retained when EF Core
  applies a mapped property independently. Typed RestLib resources still require a JSON
  object at the root of an HTTP PATCH document.
- Standard InMemory registrations resolve that canonical serializer lazily, regardless of
  whether they are registered before or after `AddRestLib`. Explicit `WithOptions`
  registration overloads remain caller-owned repository-local overrides.
- PATCH metadata caches are keyed by exact serializer-options identity. Separate applications
  or explicit options instances cannot share a contract merely because their naming policies
  have the same CLR type.
- Legacy CLR/snake/camel aliases are a compatibility behavior tied to
  `PropertyNameCaseInsensitive = true`. With case-insensitive names disabled, only exact
  canonical JSON member names are recognized.
- **v2 consideration:** If a future major version introduces pluggable serializers or
  non-JSON transport, this decision should be revisited. The `IDictionary<string, object?>`
  or generic `TPatch` approach would then merit the breaking change cost.

## References

- [RFC 7396 — JSON Merge Patch](https://www.rfc-editor.org/rfc/rfc7396)
- [System.Text.Json — JsonElement](https://learn.microsoft.com/en-us/dotnet/api/system.text.json.jsonelement)
