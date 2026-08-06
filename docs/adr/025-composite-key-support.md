# ADR-025: Two-part composite key support

**Status:** Amended
**Date:** 2026-05-09 (amended 2026-08-06)

## Context

RestLib originally assumed every resource key could be represented as one scalar `TKey` value exposed through a single `{id}` route segment. That worked for the common case, but it excluded APIs whose resource identity is naturally compound, such as tenant-scoped products (`tenantId` + `sku`) or locale-scoped content (`culture` + `slug`).

Epic C requires composite-key support without replacing RestLib's existing repository abstractions, endpoint registration model, JSON configuration story, or adapter-neutral core design.

The design had to preserve these constraints:

- existing single-key resources remain source-compatible,
- `IRepository<TEntity, TKey>` and `IBatchRepository<TEntity, TKey>` stay unchanged,
- the core package remains persistence-agnostic,
- routes stay explicit and predictable,
- JSON-backed resources continue to translate into the same fluent configuration path,
- Sprint 003 only requires ordered two-part composite keys.

## Decision

### Public key shape

RestLib introduces one strongly typed composite-key value:

```csharp
public readonly record struct RestLibCompositeKey<TFirst, TSecond>(TFirst First, TSecond Second)
    where TFirst : notnull
    where TSecond : notnull;
```

This keeps the existing `IRepository<TEntity, TKey>` surface intact. Composite-key resources still use the same repository contracts; they simply choose `RestLibCompositeKey<TFirst, TSecond>` as `TKey`.

### Explicit ordered route metadata

Composite-key routes are configured explicitly through fluent API or JSON configuration. Fluent resources use:

```csharp
app.MapRestLib<TenantProduct, RestLibCompositeKey<Guid, string>>("/api/tenant-products", config =>
{
    config.UseCompositeKey(p => p.TenantId, "tenantId", p => p.Sku, "sku");
});
```

This configures both the key selector and the route template suffix, producing item routes like:

```text
/api/tenant-products/{tenantId}/{sku}
```

Route parameter names are validated up front and must be unique within the key.

### JSON declaration shape

JSON-backed resources add a `Key` object for two-part keys:

```json
"Key": {
  "Properties": ["TenantId", "Sku"],
  "RouteParameters": ["tenantId", "sku"]
}
```

`KeyProperty` remains the single-key path. A resource may configure either `KeyProperty` or `Key`, but not both.

Folder-based loading resolves the composite key CLR type from the declared key properties and constructs `RestLibCompositeKey<TFirst, TSecond>` automatically.

### Binding, URLs, HATEOAS, OpenAPI, and Problem Details

- Minimal API route binding uses endpoint metadata so composite keys bind from ordered route parts instead of from a single scalar route value.
- RestLib-owned composite route conversion uses `InvariantCulture`. Malformed, unsupported, overflowing, and undefined enum values fail binding with HTTP 400 before hooks or repository access.
- Enum key values must resolve to a declared member. `[Flags]` keys may combine declared bits, but values containing unknown bits are rejected. Scalar enum keys receive the same membership check after ASP.NET route binding.
- Location headers and HATEOAS links render both key segments in configured order.
- OpenAPI documents one required path parameter per key part.
- Not-found Problem Details format composite keys using configured route parameter names rather than internal `First` / `Second` labels.

### Validation and update semantics

The route or batch-item envelope is authoritative for resource identity. For full replacement updates, RestLib copies every route key part onto the entity before validation and again before persistence. A key omitted from the body is therefore supplied by the route, while a conflicting body key is overwritten consistently instead of allowing the representation and repository lookup key to diverge.

The same rule applies to batch update and mapped batch update flows.

Resource keys are immutable under JSON Merge Patch. Single and batch PATCH requests that name any configured key property are rejected with a client error before hooks or persistence run. Matching follows the resource JSON naming policy, so a CLR property such as `TenantId` is protected when supplied as either `TenantId` or `tenant_id`. The InMemory and EF Core adapters enforce the same invariant when their repository patch methods are called directly.

### EF Core adapter behavior

The EF Core adapter now supports EF Core entities whose primary key has exactly two properties when the registration uses `RestLibCompositeKey<TFirst, TSecond>`.

The adapter still resolves primary-key metadata from the EF Core model. It supports:

- one-part keys mapped to scalar `TKey`, and
- two-part keys mapped to `RestLibCompositeKey<TFirst, TSecond>`.

Keys with more than two parts remain unsupported.

## Alternatives Considered

### Keep rejecting composite keys in all adapters

Rejected because it would continue to exclude a common API identity shape and force users into custom repositories or synthetic surrogate keys even when the domain already has a stable natural key.

### Replace repository abstractions with a dedicated composite-key contract

Rejected because it would be a breaking architectural change across the core library, in-memory adapter, EF Core adapter, and user-defined repositories. Reusing `IRepository<TEntity, TKey>` with a strongly typed composite `TKey` preserves compatibility.

### Support arbitrary-length composite keys immediately

Rejected for Sprint 003 because it expands the routing, binding, OpenAPI, JSON schema, and adapter complexity substantially. The current product requirement is only ordered two-part keys.

### Infer route parameter names automatically from property names

Rejected because route identity is part of the public HTTP contract. Requiring explicit route parameter names keeps URLs intentional, stable, and reviewable.

## Consequences

- Composite-key resources become an additive capability rather than a breaking redesign.
- Single-key resources continue to use the existing default `{id}` route shape and `KeyProperty` configuration.
- Composite-key resources require explicit route metadata, either through `UseCompositeKey(...)` or the JSON `Key` object.
- OpenAPI, HATEOAS, batch parsing, and Problem Details now depend on ordered key-route metadata instead of assuming one scalar key.
- Route-key interpretation no longer changes with server culture, and invalid or overflowing route segments are client errors rather than repository lookups or unhandled conversion failures.
- PUT and batch update treat route/envelope keys as authoritative; PATCH and batch patch reject attempts to modify any key part.
- The EF Core adapter supports two-part composite keys but still rejects keys with more than two parts.
- JSON schema, docs, and examples must keep both single-key and composite-key paths documented clearly.
