# ADR-024: Two-model resources and the mapping seam

**Status:** Accepted
**Date:** 2026-05-08

## Context

RestLib currently assumes one CLR type plays both roles for a resource:

- the API contract exposed at the HTTP boundary, and
- the persistence model stored through `IRepository<TEntity, TKey>`.

Epic B adds API/DB model separation as an optional capability. Teams need to expose DTOs that omit persistence-only fields, keep EF Core entity shape out of wire contracts, or apply different validation and response semantics at the API layer without replacing RestLib's repository abstractions.

The design must preserve these constraints:

- existing single-model resources keep working unchanged,
- `IRepository<TEntity, TKey>` and `IBatchRepository<TEntity, TKey>` remain DB-model oriented,
- the core library stays adapter-neutral,
- filtering, sorting, field selection, validation, ETags, HATEOAS, hooks, and batch behavior remain explicit about which model type they operate on,
- JSON resources and folder loading continue to translate into the same fluent configuration path.

## Decision

### Public mapper contract

RestLib introduces one adapter-neutral mapping seam:

```csharp
namespace RestLib.Abstractions;

public interface IRestLibMapper<TApiModel, TDbModel>
    where TApiModel : class
    where TDbModel : class
{
    TApiModel ToApi(TDbModel dbModel);

    TDbModel ToDb(TApiModel apiModel);
}
```

`IRestLibMapper<TApiModel, TDbModel>` maps object values only. It does not include query-property translation, async methods, partial-document mapping, or persistence concerns.

`IdentityMapper<TModel>` is the public default mapper for the single-model case. Existing `MapRestLib<TEntity, TKey>` registrations continue to behave as identity-mapped resources and remain source-compatible.

### Fluent C# registration and endpoint behavior

Two-model fluent resources use:

```csharp
builder.Services.AddRestLibMapper<ProductDto, ProductEntity, ProductMapper>();

app.MapRestLib<ProductDto, ProductEntity, Guid>("/api/products", cfg =>
{
    cfg.AllowAnonymous();
});
```

Endpoints deserialize and serialize `TApiModel`. Repositories continue to resolve as `IRepository<TDbModel, TKey>` and optional `IBatchRepository<TDbModel, TKey>`.

Mapping occurs at the endpoint boundary:

| Operation | Request body type | Repository type | Response body type | Mapping sequence |
| --- | --- | --- | --- | --- |
| GetAll | none | `TDbModel` | `TApiModel` | query validated on API model, repository returns DB models, each result maps to API model before field selection / HATEOAS / response |
| GetById | none | `TDbModel` | `TApiModel` | repository fetches DB model, result maps to API model before ETag / HATEOAS / response |
| Create | `TApiModel` | `TDbModel` | `TApiModel` | request body validates as API model, maps to DB model before persistence, persisted DB model maps back to API model for response |
| Update | `TApiModel` | `TDbModel` | `TApiModel` | request body validates as API model, maps to DB model before persistence, updated DB model maps back to API model for response |
| Patch | merge-patch over `TApiModel` | `TDbModel` | `TApiModel` | fetch DB model, map to API model, preview patch on API model, validate preview, map patched API model to DB model, persist, map persisted DB model back to API model |
| Delete | none | `TDbModel` | none | delete persists through DB repository; when hooks need an entity, RestLib uses the configured hook model |
| Batch | API DTOs in envelope | `TDbModel` | API DTOs in envelope | per-item mapping follows the same rules as single-item operations |

### Validation, query, field selection, ETags, HATEOAS, and hooks

- Data Annotation validation and JSON validation run on `TApiModel`.
- Field selection is applied to `TApiModel` after mapping from DB results.
- ETag generation hashes `TApiModel`, not `TDbModel`, so persistence-only fields do not leak into cache validators.
- HATEOAS uses `TApiModel` and `IHateoasLinkProvider<TApiModel, TKey>`.
- Filtering and sorting are configured and validated against `TApiModel`.

Sprint 002 does not add query-property translation metadata to the mapper. For two-model resources, RestLib only supports repository query pushdown when each configured filter or sort property exists on both the API model and DB model with the same CLR property name and a compatible CLR type. Field selection remains API-only and does not require the selected property to exist on `TDbModel` because it runs after mapping.

Hooks run on API models by default. DB-model hooks are an explicit opt-in:

- C#: `cfg.UseDbModelHooks(...)` or `cfg.UseDbModelHooks()`
- JSON: `"Mapping": { "HookModel": "Db" }`

Only one hook model is active for a two-model resource. RestLib does not run both API hooks and DB hooks for the same resource registration.

### JSON declaration shape

For JSON resources, `EntityType` remains the API model type. Resources without `Mapping` stay single-model.

Two-model JSON resources use:

```json
{
  "EntityType": "MyApp.Api.ProductDto, MyApp",
  "Mapping": {
    "DbType": "MyApp.Persistence.ProductEntity, MyApp",
    "Mapper": "ProductMapper"
  }
}
```

Strict auto-mapping is available for trivial same-name, same-type models only:

```json
"Mapping": {
  "DbType": "MyApp.Persistence.ProductEntity, MyApp",
  "Auto": true
}
```

`"HookModel": "Db"` is the JSON opt-in for DB-model hooks. When `Mapping` is absent, `EntityType` continues to be the single model used for both API and persistence behavior.

### PATCH and batch semantics

PATCH and batch PATCH validate the API-model preview before persistence hooks fire. Because the mapper contract does not translate partial JSON documents, mapped PATCH uses the API preview as the source of truth, then maps the patched API model to a full DB model before persistence. RestLib keeps the existing same-name patch-document path only for single-model resources.

## Alternatives Considered

### Replace `IRepository<TEntity, TKey>` with a two-model repository abstraction

Rejected because it would force every adapter and custom repository to change at once, even when a resource only needs a single model. Keeping repositories DB-model oriented preserves backward compatibility and adapter neutrality.

### Add projector-style services instead of a mapper

Rejected because RestLib needs both directions, not just DB-to-API projection. Create, update, and patch all require API-to-DB conversion as well.

### Take a dependency on AutoMapper

Rejected because it adds an external runtime dependency, pushes core behavior into convention-heavy configuration outside RestLib's control, and weakens the explicit type-safe seam required by the product goal.

### Generate mapping code from JSON configuration

Rejected because mapping logic is imperative behavior, not declarative resource configuration. JSON remains the place to select a mapper or opt into strict trivial auto-mapping, not to define transformation rules.

### Add query-property translation metadata to `IRestLibMapper`

Rejected for Sprint 002 because it expands the seam beyond object mapping and would force premature design decisions about filtering, sorting, and provider-specific translation. Sprint 002 instead limits mapped query pushdown to same-name, compatible properties on both models.

## Consequences

- API/DB model separation becomes an additive capability instead of a breaking architectural rewrite.
- Single-model resources remain the identity-mapped default and do not require explicit mapper registration.
- Validation, ETags, field selection, HATEOAS, and default hooks become clearly API-model concerns.
- Repository adapters remain DB-model oriented and adapter-neutral.
- JSON resources gain a declarative `Mapping` section, but non-trivial mapping behavior stays in C#.
- Query pushdown for mapped resources is intentionally limited in Sprint 002 to same-name, compatible properties.
- PATCH for mapped resources uses full-model mapping after API preview validation rather than partial DB patch translation.

## Out Of Scope

- Query-property renaming or translation metadata in the mapper contract.
- Partial-document mapper APIs.
- Nested, renamed, computed, conditional, or collection-aware auto-mapping rules.
- Automatic source generation for mapping.
- Any EF Core-specific behavior in the core abstraction.
