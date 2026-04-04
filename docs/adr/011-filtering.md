# ADR-011: Query Parameter Filtering

**Status:** Accepted
**Date:** 2026-03-30

## Context

REST APIs that return collections need server-side filtering so clients can retrieve only the records they care about. Without filtering, clients must fetch entire collections and filter locally, which wastes bandwidth, increases latency, and is incompatible with cursor-based pagination (where only a page of results is available at a time).

Three design decisions arise:

1. **Which properties can be filtered on** — open (any property) or restricted (allow-list)?
2. **Query parameter syntax** — how clients express filter criteria.
3. **What filter operators to support** — equality only, or comparison operators (greater-than, less-than, contains, etc.)?

## Options Considered

### Access Control

| Option | Pros | Cons |
| --- | --- | --- |
| Open filtering (any property) | Zero configuration | Exposes internal property names; may allow filtering on unindexed columns causing performance issues; security risk if sensitive fields are filterable |
| Deny-list (block specific properties) | Low configuration for simple models | Easy to forget to block new properties; unsafe by default |
| Allow-list (explicit opt-in) | Explicit control; only exposes intended properties; safe by default | Requires configuration per resource |

### Query Parameter Syntax

| Option | Pros | Cons |
| --- | --- | --- |
| Dedicated filter parameter (`filter[field]=value`) | Clear separation from other query params | Bracket syntax is uncommon in .NET; more complex to parse |
| Direct query parameters (`?category_id=5&is_active=true`) | Simple; idiomatic for REST APIs; no special syntax to learn | Filter params share namespace with other query params (e.g., `sort`, `fields`, `page_size`) |
| OData `$filter` expression (`$filter=price gt 100`) | Very powerful; supports complex expressions | Heavy dependency; large attack surface; overkill for simple filtering |

### Filter Operators

| Option | Pros | Cons |
| --- | --- | --- |
| Equality only (`?status=active`) | Simple to parse; small attack surface; covers the majority of use cases | Cannot express ranges or partial matches |
| Comparison operators (`?price[gt]=100`) | More flexible | Significantly more complex to parse and validate; larger attack surface; repository implementations must handle each operator |
| Full expression language (OData, RSQL) | Arbitrarily complex queries | Very large attack surface; complex to implement securely; requires expression-to-query translation |

## Decision

### 1. Allow-list based filtering

Only properties explicitly registered via `AllowFiltering()` can be used as filters. Unknown query parameters that do not match a configured filter property are silently ignored (they may be intended for other purposes). Property names are automatically converted to `snake_case` using `JsonNamingPolicy.SnakeCaseLower`, consistent with the project's JSON naming convention (ADR-004).

```csharp
config.AllowFiltering(p => p.CategoryId, p => p.IsActive);
```

A string-based overload is also available for JSON-configured resources:

```csharp
config.AllowFiltering("CategoryId", "IsActive");
```

### 2. Direct query parameters with equality semantics

Filter values are passed as standard query parameters using the snake_case property name:

```
GET /api/products?category_id=5&is_active=true
```

- Property names use `snake_case` (matching JSON property names).
- Values are parsed to the target property's CLR type. Type mismatches return a 400 Problem Details response with type `/problems/invalid-filter`.
- Multiple filter parameters are combined with AND semantics.

### 3. Equality-only operators

Filters support only exact equality matching. No comparison operators, partial matches, or range queries are provided.

### 4. Validation before query

Filter parameters are parsed and validated **before** `repository.GetAllAsync()` is called, consistent with the validation-first pattern used by sorting (ADR-009) and field selection (ADR-007). Invalid filter values return immediately with a Problem Details response.

### 5. Repository-agnostic filter contract

Parsed filter values are passed to the repository via `PaginationRequest.Filters` (`IReadOnlyList<FilterValue>`). Each `FilterValue` carries the C# `PropertyName`, the `QueryParameterName` (snake_case), and the parsed `Value` (as `object`). The repository implementation decides how to apply them (e.g., LINQ `Where` for in-memory, SQL `WHERE` for database-backed).

### 6. Silently ignored when unconfigured

If `AllowFiltering()` is not called for a resource, any query parameters that would otherwise be filter properties are silently ignored. This avoids breaking clients that send filter parameters to endpoints that do not support filtering.

## Rationale

1. **Allow-list prevents security and performance surprises.** Filtering on a sensitive field (e.g., `password_hash`) or an unindexed column could leak data or cause full table scans. The allow-list ensures only properties the developer has explicitly considered are filterable.
2. **Direct query parameters are simple and idiomatic.** Most REST APIs (Stripe, GitHub, many internal APIs) use direct query parameters for equality filters. No special syntax to learn.
3. **Equality-only keeps the attack surface small.** Comparison operators and expression languages add significant complexity to parsing, validation, and repository implementation. Equality covers the vast majority of collection filtering use cases. More complex operators can be added in a future version if demand materializes.
4. **Consistent with sorting and field selection.** The allow-list approach, snake_case naming, validation-before-query pattern, and JSON configuration support all follow the same design established by ADR-007 (field selection) and ADR-009 (sorting).
5. **Filter values preserved in pagination links.** Filter parameters are included in `self`, `first`, and `next` links, ensuring clients can follow pagination links without manually re-appending filter parameters.

## Consequences

- Filtering only applies to GetAll endpoints. Other operations are unaffected.
- Users must configure each filterable property explicitly. There is no shortcut to make all properties filterable.
- No support for range filters, partial matches, or complex expressions. This is by design — it keeps the implementation simple and the attack surface small.
- Unknown query parameters are silently ignored, which means a typo in a filter property name will not produce an error. This trade-off favors forward compatibility over strict validation.
- Repository implementations must handle `Filters` in `PaginationRequest`. An empty list means no filters were requested.

### Repository contract enforcement

RestLib validates and parses filter (and sort) query parameters before calling
`GetAllAsync()`, but it does **not** verify that the repository actually applied
them. A repository that ignores `PaginationRequest.Filters` or
`PaginationRequest.SortFields` will return unfiltered/unsorted data silently.

This is by design — RestLib does not impose a particular data-access technology
and therefore cannot inspect or modify the query the repository builds.
Repository authors should:

1. **Verify filter/sort support in integration tests.** Seed known data, apply
   filters via the HTTP client, and assert that only matching items are returned.
2. **Return the full `PaginationRequest` fields list** to `InMemoryRepository`
   or similar test doubles that manually implement filtering logic.

The same consideration applies to sorting (ADR-009).

## JSON Configuration

Filtering can also be configured declaratively via `appsettings.json`:

```json
{
  "Filtering": ["CategoryId", "IsActive"]
}
```

Property names in the `Filtering` array use PascalCase (C# property names) and are converted to snake_case automatically.
