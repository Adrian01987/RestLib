# ADR-019: HATEOAS Hypermedia Links

**Status:** Accepted
**Date:** 2026-04-10

## Context
REST APIs at Richardson Maturity Model Level 3 include hypermedia controls
that allow clients to discover available actions and navigate between
resources without hardcoding URLs. RestLib generates CRUD endpoints
automatically, which makes it a natural place to also generate the
hypermedia links that describe those endpoints.

## Decision

### Opt-in via global option
HATEOAS is enabled by setting `RestLibOptions.EnableHateoas = true`
(default `false`). This avoids breaking existing consumers whose clients
do not expect the extra `_links` property in responses. A per-endpoint
override was considered but cancelled because `config.IsOperationEnabled()`
already controls which links appear per endpoint — the global toggle is
sufficient.

### HAL-style flat `_links` injection
Each entity response includes a `_links` property alongside the entity's
own properties (flat injection). This follows the HAL convention and is
compatible with RestLib's existing `snake_case` JSON policy. The
underscore prefix distinguishes metadata from entity data.

The alternative — wrapping each entity in an envelope
(`{ "data": {...}, "_links": {...} }`) — was rejected because it would
change the response shape for all consumers and complicate field
selection projection.

### Standard link relations
The following links are generated per entity, conditioned on
`config.IsOperationEnabled()`:

| Relation     | Method  | Condition                   |
|-------------|--------|-----------------------------|
| `self`       | (GET)  | Always present               |
| `collection` | (GET)  | `GetAll` is enabled          |
| `update`     | PUT    | `Update` is enabled          |
| `patch`      | PATCH  | `Patch` is enabled           |
| `delete`     | DELETE | `Delete` is enabled          |

GET links omit the `method` property (GET is the default assumption in
HAL). Non-GET links include an explicit `method` to guide clients.

### Custom link extensibility
`IHateoasLinkProvider<TEntity, TKey>` allows users to inject additional
links (e.g., related resources, sub-collections). Custom links are
appended after the standard links. If a custom link uses the same
relation name as a standard link, the custom link takes precedence,
enabling full control over the link set.

Registration uses a dedicated extension method:
```csharp
services.AddHateoasLinkProvider<Product, Guid, ProductLinkProvider>();
```

### All entity-returning endpoints
Links are injected into every endpoint that returns an entity body:
GetById, GetAll (per item), Create, Update, Patch, and Batch operations
(create, update, patch items). Delete returns 204 No Content and has no
links.

### Field selection compatibility
When `?fields=` is active, entities are projected to
`Dictionary<string, JsonElement>`. The `_links` property is injected
into the projected dictionary after projection, so it always appears
regardless of which fields were selected. The entity key is extracted
from the original (non-projected) entity for link construction.

### Batch integration
Each successful batch item's entity includes `_links`. The collection
path is derived from the batch endpoint path by stripping the `/batch`
suffix (e.g., `/api/products/batch` → `/api/products`). This ensures
links reference the correct collection URL, not the batch endpoint.

### URL construction
Links use absolute URLs following the same pattern as pagination links:
`{scheme}://{host}{path}`. The scheme and host are taken from the
current `HttpRequest`, matching the existing `PaginationHelper` approach.

### Implementation architecture
HATEOAS logic is isolated in `src/RestLib/Hypermedia/`:

- `HateoasLink` — Link model with `href` and optional `method`
- `HateoasLinkBuilder` — Builds per-entity link dictionaries
- `HateoasHelper` — Injection helpers for entities and projected dicts
- `IHateoasLinkProvider<TEntity, TKey>` — Custom link interface

Each handler checks `options.EnableHateoas` at request time (not at
registration time), keeping map-time logic unchanged. The link injection
is a final step before serialization, after hooks and field projection.

## Consequences
- New `RestLibOptions.EnableHateoas` property (additive, no breaking change)
- New `IHateoasLinkProvider<TEntity, TKey>` public interface
- New `AddHateoasLinkProvider` DI extension method
- Four new internal classes in `src/RestLib/Hypermedia/`
- Entity responses gain a `_links` property when enabled — clients must
  tolerate unknown properties (standard JSON practice)
- Slight serialization overhead per entity (serialize → dictionary → add
  links → serialize) when enabled; negligible for typical response sizes

## Known Limitations

### No link templating
Links are fully resolved URLs. RFC 6570 URI templates (e.g.,
`/api/items{?fields,sort}`) are not supported. This keeps the
implementation simple and avoids requiring clients to implement template
expansion.

### Entity key extraction dependency
Link construction requires extracting the entity key via
`EntityKeyHelper.GetEntityKey`, which uses the configured `KeySelector`
or falls back to reflection on an `Id` property. Entities without a
discoverable key will not receive HATEOAS links in batch responses
(the entity is returned without `_links` rather than failing).
