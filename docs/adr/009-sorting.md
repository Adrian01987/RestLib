# ADR-009: Allow-List Sorting with Default Sort

**Status:** Accepted
**Date:** 2026-03-28

## Context

REST APIs that return collections need a way for clients to control result ordering. Without server-side sorting, clients must fetch all data and sort locally, which is wasteful and incompatible with cursor-based pagination (where only a page of results is returned at a time).

Three design decisions arise:

1. **Which properties can be sorted on** — open (any property) or restricted (allow-list)?
2. **Query parameter syntax** — how clients express sort field and direction.
3. **What happens when no sort is requested** — undefined order, or a configurable default?

## Options Considered

### Access Control

| Option | Pros | Cons |
| --- | --- | --- |
| Open sorting (any property) | Zero configuration | Exposes internal property names; may allow sorting on unindexed or computed columns causing performance issues |
| Allow-list | Explicit control over what can be sorted; only exposes intended properties | Requires configuration per resource |

### Query Parameter Syntax

| Option | Pros | Cons |
| --- | --- | --- |
| Multiple parameters (`sort_by=price&sort_dir=asc`) | Familiar from simple APIs | Cannot express multi-field sorts cleanly |
| Comma-separated with colon direction (`sort=price:asc,name:desc`) | Compact; supports multi-field; widely used (Stripe, Zalando) | Slightly more complex to parse |
| JSON body parameter | Arbitrarily complex | Not idiomatic for GET query parameters |

### Default Sort Behavior

| Option | Pros | Cons |
| --- | --- | --- |
| No default (undefined order) | Simple | Results are non-deterministic; cursor pagination may skip or duplicate items |
| Implicit sort by primary key | Deterministic | May not match user expectations (e.g., newest first) |
| Configurable default sort | Deterministic; matches domain expectations | One more configuration option |

## Decision

### 1. Allow-list based sorting

Only properties explicitly registered via `AllowSorting()` can be sorted on. Unknown fields return a 400 Problem Details response with type `/problems/invalid-sort`. Property names are automatically converted to `snake_case` using `JsonNamingPolicy.SnakeCaseLower`, consistent with the project's JSON naming convention (ADR-004).

```csharp
config.AllowSorting(p => p.Price, p => p.Name, p => p.CreatedAt);
```

A string-based overload is also available for JSON-configured resources:

```csharp
config.AllowSorting("Price", "Name", "CreatedAt");
```

### 2. Comma-separated colon syntax

The query parameter is `sort` with the format `field:direction,field:direction`:

```
GET /api/products?sort=price:asc,name:desc
```

- Field names use `snake_case` (matching JSON property names).
- Direction is `asc` or `desc` (case-insensitive). If omitted, defaults to `asc`.
- Multiple fields are separated by commas and applied in order.
- Duplicate fields in the same request return a 400 error.

### 3. Configurable default sort

An optional default sort expression can be set via `DefaultSort()`. It is validated at configuration time using the same `SortParser` that validates client input, ensuring the default is always valid.

```csharp
config.AllowSorting(p => p.Name, p => p.Price);
config.DefaultSort("name:asc");
```

When the client provides an explicit `sort` parameter, the default is ignored. When no `sort` parameter is sent and a default is configured, the default is applied. When neither is present, sort fields are empty and the repository determines the order.

### 4. Validation before query

Sort parameters are parsed and validated **before** `repository.GetAllAsync()` is called, consistent with the validation-first pattern used by filtering (ADR-005) and field selection (ADR-007).

### 5. Repository-agnostic sort contract

Parsed sort fields are passed to the repository via `PaginationRequest.SortFields` (`IReadOnlyList<SortField>`). Each `SortField` carries the C# `PropertyName`, the `QueryParameterName` (snake_case), and the `Direction`. The repository implementation decides how to apply them (e.g., LINQ `OrderBy` for in-memory, SQL `ORDER BY` for database-backed).

The `InMemoryRepository` additionally appends the entity key as a tie-breaker to ensure stable ordering for cursor pagination.

### 6. Silently ignored when unconfigured

If `AllowSorting()` is not called for a resource, the `sort` query parameter is silently ignored. This matches the behavior of filtering (unknown filter properties are ignored) and avoids breaking clients that send `sort` to endpoints that do not support it.

## Rationale

1. **Allow-list prevents performance surprises.** Sorting on an unindexed column in a database-backed repository could cause full table scans. The allow-list ensures only properties the developer has considered are sortable.
2. **Colon syntax is compact and widely adopted.** The `field:direction` format is used by Stripe, Zalando, and many other production APIs. It naturally extends to multi-field sorts without additional parameters.
3. **Default sort ensures deterministic pagination.** Without a stable sort order, cursor-based pagination can skip or duplicate items when new data is inserted. A configurable default lets the developer choose the right default for their domain (e.g., `created_at:desc` for newest-first).
4. **Configuration-time validation of defaults catches errors early.** If the default sort references a field that is not in the allow-list, the application fails at startup rather than at runtime.
5. **Sort preserved in pagination links.** The `sort` parameter is included in `self`, `first`, and `next` links, ensuring clients can follow pagination links without manually re-appending sort parameters.

## Consequences

- Sorting only applies to GetAll endpoints. Other operations are unaffected.
- The `sort` query parameter is reserved. Resources that configure sorting cannot use `sort` as a filter property name.
- Repository implementations must handle `SortFields` in `PaginationRequest`. An empty list means no explicit sort was requested.
- Multi-field sorts are applied in order: the first field is the primary sort, subsequent fields break ties.
- Sort direction defaults to ascending, matching SQL convention and user expectations.

## JSON Configuration

Sorting can also be configured declaratively via `appsettings.json`:

```json
{
  "Sorting": ["Price", "Name", "CreatedAt"],
  "DefaultSort": "name:asc"
}
```

Property names in the `Sorting` array use PascalCase (C# property names) and are converted to snake_case automatically.
