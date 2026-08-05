# ADR-013: Filter Operators Beyond Equality

**Status:** Accepted
**Date:** 2026-04-04
**Supersedes:** Partial — extends [ADR-011](011-filtering.md) decision #3 (equality-only operators)

## Context

ADR-011 established query parameter filtering with equality-only semantics. While equality covers the majority of use cases, real-world APIs frequently need:

- **Range queries** — "products priced between $20 and $100"
- **Partial matches** — "products whose name contains 'widget'"
- **Exclusion** — "orders whose status is not 'cancelled'"
- **Set membership** — "products in categories A, B, or C"

Without these, clients must over-fetch and filter locally, which defeats the purpose of server-side filtering and is incompatible with cursor pagination.

## Options Considered

### Query Syntax

| Option | Example | Pros | Cons |
| --- | --- | --- | --- |
| **A. Bracket syntax** | `?price[gte]=10&price[lte]=100` | Widely used (Stripe, Shopify); clear operator-per-parameter; backward compatible with bare `?price=42` | Brackets need URL encoding or curl `-g` flag; slightly longer URLs |
| B. Colon suffix | `?price:gte=10&price:lte=100` | Compact | Colons are less conventional for filter operators; conflict risk with sort syntax |
| C. LHS dot notation | `?price.gte=10&price.lte=100` | Simple | Dots conflict with nested property paths |
| D. RHS value syntax | `?price=gte:10` | Single parameter per field | Cannot combine operators on same field; value parsing ambiguous for strings containing colons |
| E. OData `$filter` | `?$filter=price ge 10 and price le 100` | Industry standard; very expressive | Heavy parser; large attack surface; overkill for simple filtering |

### Operator Set

| Option | Operators | Pros | Cons |
| --- | --- | --- | --- |
| Minimal | eq, neq, gt, lt, gte, lte | Covers ranges and exclusion | No string search |
| **Selected** | eq, neq, gt, lt, gte, lte, contains, starts_with, ends_with, in | Covers ranges, exclusion, string search, set membership | Slightly larger API surface |
| Full | Above + between, regex, not_in, is_null | Maximum flexibility | Large attack surface; regex is dangerous; complex to validate |

## Decision

### 1. Bracket syntax (Option A)

Filter operators are expressed using bracket notation appended to the property name:

```
GET /api/products?price[gte]=10&price[lte]=100&name[contains]=widget
```

Bare equality (`?price=42`) remains valid as shorthand for `?price[eq]=42`, preserving full backward compatibility with ADR-011.

### 2. Ten operators

| Operator | Meaning | Type Constraint | Example |
| --- | --- | --- | --- |
| `eq` | Equals | Any | `?status[eq]=active` or `?status=active` |
| `neq` | Not equals | Any | `?status[neq]=cancelled` |
| `gt` | Greater than | Portable numeric types and `DateTime` | `?price[gt]=50` |
| `lt` | Less than | Portable numeric types and `DateTime` | `?price[lt]=100` |
| `gte` | Greater than or equal | Portable numeric types and `DateTime` | `?price[gte]=50` |
| `lte` | Less than or equal | Portable numeric types and `DateTime` | `?price[lte]=100` |
| `contains` | Literal substring match (case-insensitive) | `string` | `?name[contains]=widget` |
| `starts_with` | Literal prefix match (case-insensitive) | `string` | `?name[starts_with]=wire` |
| `ends_with` | Literal suffix match (case-insensitive) | `string` | `?name[ends_with]=phone` |
| `in` | Set membership | Any | `?status[in]=active,pending` |

### 3. Per-property operator allow-list

Each filterable property declares which operators it supports. `Eq` is always implicitly included:

```csharp
config.AllowFiltering(p => p.Price, FilterOperators.Comparison);
config.AllowFiltering(p => p.Name, FilterOperators.String);
config.AllowFiltering(p => p.CategoryId); // eq only (default)
```

Preset arrays are provided for common combinations:

- `FilterOperators.Equality` — `Eq`, `Neq`
- `FilterOperators.Comparison` — `Eq`, `Neq`, `Gt`, `Lt`, `Gte`, `Lte`
- `FilterOperators.String` — `Eq`, `Neq`, `Contains`, `StartsWith`, `EndsWith`
- `FilterOperators.All` — all ten operators

Using an operator not in the property's allow-list returns a 400 Problem Details response.

### 4. Type-safe validation

The parser validates operator compatibility at request time:
- Comparison operators (`gt`, `lt`, `gte`, `lte`) use a portable baseline shared by the built-in adapters: `byte`, `short`, `int`, `long`, `float`, `double`, `decimal`, and `DateTime`, including nullable forms.
- `string`, `bool`, enums, `Guid`, custom `IComparable` types, unsigned integral types other than `byte`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, and `TimeSpan` can still use `eq`, `neq`, and `in`, but not relational operators.
- String operators (`contains`, `starts_with`, `ends_with`) require the property's CLR type to be `string`.
- Type mismatches return a 400 Problem Details response with type `/problems/invalid-filter`.

Validation occurs before request-validated hooks and before repository query/count calls. This prevents an operator accepted by the HTTP layer from failing while an adapter constructs its query. A null property value is unordered and never satisfies `gt`, `lt`, `gte`, or `lte`.

### 5. `in` operator constraints

The `in` operator accepts a comma-separated list of values. A maximum list size of 50 items is enforced to prevent abuse. Empty lists are rejected. Values are parsed to the property's CLR type individually.

### 6. Multiple operators per property

A single request can apply multiple operators to the same property (e.g., `?price[gte]=10&price[lte]=100`), combined with AND semantics. Duplicate operators on the same property are rejected with a 400 error.

### 7. JSON configuration

Operators can be configured declaratively alongside filter properties:

```json
{
  "Filtering": ["CategoryId", "IsActive"],
  "FilteringOperators": {
    "Price": ["comparison"],
    "Name": ["contains", "starts_with", "ends_with"]
  }
}
```

Properties in `FilteringOperators` are automatically added to the filtering allow-list. Individual operator names (e.g., `contains`) and preset names (e.g., `comparison`) are both accepted.

### 8. Literal and case semantics

`contains`, `starts_with`, and `ends_with` always treat the request value as text. Characters used by SQL `LIKE` implementations, including `%`, `_`, brackets, carets, and the escape character, do not acquire wildcard meaning. Null property values do not match.

The InMemory adapter uses ordinal case-insensitive matching. The EF Core adapter normalizes both operands before matching and escapes SQL patterns explicitly. This gives the built-in adapters the same literal, case-insensitive behavior for ordinary text, while Unicode case mapping remains database-provider dependent.

Collection search follows the same literal-input rule. With `CaseSensitive = false`, RestLib folds case; with `CaseSensitive = true`, RestLib disables folding. EF Core cannot select a universal case-sensitive collation because collation names and behavior are database-specific, so database-backed case-sensitive search still requires an appropriately configured database or column collation. String `eq`, `neq`, and `in` likewise use the adapter's native equality semantics and the configured database collation.

## Rationale

1. **Bracket syntax is the de facto standard.** Stripe, Shopify, and many other APIs use `field[operator]=value`. It is well-understood by API consumers and works cleanly with multiple operators on the same field.
2. **Backward compatible.** Bare `?field=value` continues to work as equality, so existing clients require no changes.
3. **Per-property allow-lists maintain the safe-by-default principle.** Properties only support operators the developer explicitly enables, limiting the attack surface to intentional capabilities.
4. **Type-safe validation prevents runtime errors.** Rejecting `contains` on an `int` property at parse time provides clear error messages rather than obscure runtime failures.
5. **`in` operator size limit prevents abuse.** Without a cap, a client could send thousands of values, causing performance issues in repository implementations.
6. **A portable relational baseline is safer than `IComparable`.** Several CLR types implement `IComparable` without defining expression-tree relational operators, and database providers do not translate every CLR comparison uniformly.
7. **Literal string operands are part of the HTTP contract.** SQL wildcard syntax is an implementation detail and must not change the meaning of user input.

## Consequences

- Repository implementations must handle all ten operators in their filter logic. The `InMemoryRepository` reference implementation is updated accordingly.
- The `FilterValue` model now carries an `Operator` property (`FilterOperator` enum) and a `TypedValues` list (for `in` operator support), in addition to the existing `Value`.
- The bracket pattern is parsed using a `GeneratedRegex` for performance.
- Curl clients must use the `-g` (globoff) flag or URL-encode brackets (`%5B`, `%5D`) when testing bracket syntax from the command line.
- E2E test infrastructure (`e2e-lib.sh`) includes the `-g` flag to support bracket syntax in test URLs.
- EF Core case folding is performed in the query and can prevent ordinary indexes from being used. Applications with large text datasets should select provider-appropriate collations and indexes, then verify query plans.
- Exact Unicode, accent, and culture behavior for EF Core string equality, filtering, and search follows the configured database provider and collation; RestLib does not silently switch to client-side evaluation.
- Portable EF query expressions are covered by SQLite behavioral tests and SQL Server provider-translation smoke tests. The SQL Server tests do not claim identical collation behavior or require a running SQL Server instance.
