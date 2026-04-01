# ADR-010: API Versioning Integration

**Status:** Accepted
**Date:** 2026-04-01

## Context

APIs evolve over time. Consumers need the ability to version their RestLib endpoints using standard ASP.NET Core patterns. The two most common approaches are:

1. **URL path segments** — `/api/v1/products`, `/api/v2/products`
2. **Asp.Versioning.Http** — query string, header, or URL segment strategies via the `Asp.Versioning.Http` NuGet package

RestLib must support both without taking a hard dependency on any versioning library.

The existing `MapRestLib<TEntity, TKey>(this IEndpointRouteBuilder, string prefix, ...)` already supports mounting endpoints on a `RouteGroupBuilder` (since `RouteGroupBuilder` implements `IEndpointRouteBuilder`), but the ergonomics are poor because the consumer must always pass a prefix string, even when the group already has the full path configured.

## Options Considered

### Dependency Strategy

| Option | Pros | Cons |
| --- | --- | --- |
| Add `Asp.Versioning.Http` dependency | Tight integration, convenience methods | Forces all consumers into a specific versioning library; increases package size |
| RestLib-owned version config (`cfg.Version(1.0)`) | Self-contained | Duplicates functionality; couples RestLib to a versioning model |
| Integrate via ASP.NET Core's `RouteGroupBuilder` | Zero new dependencies; works with any versioning approach | No built-in convenience for complex strategies |

### API Surface

| Option | Pros | Cons |
| --- | --- | --- |
| Only the existing `MapRestLib(IEndpointRouteBuilder, string)` | No new API surface | Awkward when group already has full prefix; consumers pass empty string |
| Add prefix-less `MapRestLib(RouteGroupBuilder)` overload | Natural integration point; no ambiguity with existing overload | One more public method |
| Add `MapVersioned(...)` convenience methods | Less boilerplate for common patterns | Increases API surface; may not cover all strategies |

### OpenAPI Operation ID Uniqueness

| Option | Pros | Cons |
| --- | --- | --- |
| Static counter for de-duplication | Simple implementation | Leaks state across test hosts; non-deterministic IDs |
| DI-scoped `EndpointNameRegistry` singleton | Proper test isolation; deterministic within a host | Requires `AddRestLib()` to be called; falls back to static for backwards compatibility |
| Include full route prefix in operation ID | Naturally unique when prefixes differ | Long IDs; identical prefixes across groups still collide |

## Decision

### 1. No dependency on Asp.Versioning.Http

RestLib's core NuGet package does not take a dependency on any versioning library. Versioning integration is achieved through ASP.NET Core's built-in `IEndpointRouteBuilder` / `RouteGroupBuilder` abstractions. Consumers who want `Asp.Versioning.Http` features install it themselves.

### 2. Prefix-less MapRestLib overload on RouteGroupBuilder

A new overload registers CRUD endpoints directly on an existing route group without creating a nested `MapGroup`:

```csharp
public static RouteGroupBuilder MapRestLib<TEntity, TKey>(
    this RouteGroupBuilder group,
    Action<RestLibEndpointConfiguration<TEntity, TKey>>? configure = null)
    where TEntity : class
```

Both overloads delegate to a shared private `ConfigureRestLibEndpoints` method. The only difference is how the `RouteGroupBuilder` is obtained:

- **Existing overload:** `endpoints.MapGroup(prefix)` then calls shared method with the prefix for operation ID generation.
- **New overload:** receives `group` directly, calls shared method with `routePrefix: null`.

### 3. Route prefix incorporated into OpenAPI operation IDs

When a route prefix is provided (via the existing overload), it is sanitized and combined with the entity type name to form a candidate for OpenAPI operation IDs: `{EntityName}_{sanitizedPrefix}_{Operation}` (e.g., `Product_api_products_GetAll`). This prevents collisions when the same entity is registered at different routes.

### 4. DI-scoped EndpointNameRegistry for uniqueness

An internal `EndpointNameRegistry` class tracks endpoint name usage per application host. It is registered as a singleton by `AddRestLib()` and resolved from `IServiceProvider` during endpoint configuration. When the same candidate name is used multiple times (e.g., same entity at the same sub-prefix inside different version groups), a numeric suffix is appended (e.g., `Product_products2`).

A static fallback instance is used when `AddRestLib()` has not been called, maintaining backwards compatibility with existing code that calls `MapRestLib` without `AddRestLib()`.

### 5. No version-aware configuration model

RestLib does not add version properties to `RestLibEndpointConfiguration`. Version metadata belongs on the route group (via URL segments or `HasApiVersion()`), not inside RestLib's config. This keeps RestLib version-agnostic.

### 6. JSON resource registry group overloads

`RestLibJsonResourceRegistry` gains `MapAll(RouteGroupBuilder)` and `Map(RouteGroupBuilder, string)` overloads so JSON-configured resources can be mounted inside versioned groups.

## Rationale

1. **RouteGroupBuilder is the standard composition point.** ASP.NET Core Minimal APIs use `MapGroup()` for URL prefix grouping, middleware scoping, and metadata inheritance. RestLib integrating at this level means versioning "just works" with any strategy.
2. **No dependency avoids coupling.** The versioning ecosystem has multiple libraries and approaches. By not depending on any of them, RestLib remains compatible with all of them.
3. **Prefix in operation IDs prevents OpenAPI collisions.** When the same entity type is registered at multiple routes (common in versioned APIs), operation IDs must be unique. Incorporating the route prefix into the ID is more predictable than a global counter alone.
4. **DI-scoped registry enables test isolation.** Each test host gets its own `EndpointNameRegistry` instance, preventing counter leakage between xUnit test classes that run in the same process.
5. **Authorization and rate limiting cascade from groups.** ASP.NET Core's `RouteGroupBuilder` metadata inheritance means `group.RequireAuthorization()` or `group.RequireRateLimiting()` automatically applies to all RestLib endpoints on that group. No special handling is needed in RestLib.

## Consequences

- The existing `MapRestLib(IEndpointRouteBuilder, string, ...)` overload is unchanged. No breaking changes.
- The prefix-less overload enables clean versioned API patterns without empty-string prefixes.
- Pagination links automatically include the full group prefix because `HttpRequest.Path` includes all group segments.
- Operation IDs may contain route path information (e.g., `Product_api_v1_products_GetAll`), which is longer but more descriptive.
- Consumers are responsible for configuring OpenAPI document splitting by version (e.g., Swashbuckle's `SwaggerDoc` per version).
- Calling `MapRestLib` twice on the same group with the same entity type is a consumer error and will result in ambiguous route matches at runtime.

## Two-Tier Integration Pattern

| Tier | Example | RestLib Dependency |
| --- | --- | --- |
| **Tier 1 — URL prefix grouping** | `app.MapGroup("/api/v1").MapRestLib<T,K>("/products")` | None beyond ASP.NET Core |
| **Tier 2 — Asp.Versioning.Http** | `app.NewVersionedApi().MapGroup("/api/v{version:apiVersion}/products").HasApiVersion(1.0).MapRestLib<T,K>(...)` | Consumer adds `Asp.Versioning.Http` |
