# RestLib

> **3 lines to a consistent CRUD REST API**

[![Build](https://github.com/Adrian01987/RestLib/actions/workflows/ci.yml/badge.svg)](https://github.com/Adrian01987/RestLib/actions/workflows/ci.yml)
[![Coverage](https://codecov.io/gh/Adrian01987/RestLib/branch/main/graph/badge.svg)](https://codecov.io/gh/Adrian01987/RestLib)
[![NuGet](https://img.shields.io/nuget/v/RestLib.svg)](https://www.nuget.org/packages/RestLib/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/Adrian01987/RestLib/blob/main/LICENSE)

RestLib is a .NET 10 library for ASP.NET Core Minimal APIs that generates CRUD endpoints from your model and repository. It bakes in secure defaults, cursor-based pagination APIs, filtering, sorting, field selection, HATEOAS hypermedia links, OpenAPI metadata, and RFC 9457 Problem Details so you can ship consistent APIs faster. Some capabilities depend on the repository adapter and may have implementation-specific limits.

## Table of Contents

- [Install](#install)
- [Quick Start](#quick-start)
- [Why RestLib](#why-restlib)
- [Choosing RestLib](#choosing-restlib)
- [Features](#features)
- [Performance](#performance)
- [Learn More](#learn-more)
- [Guides](#guides)
- [Architecture Decisions](#architecture-decisions)
- [Packages](#packages)
- [Requirements](#requirements)
- [Known Limitations](#known-limitations)
- [Contributing](#contributing)
- [License](#license)

## Install

Install the core package:

```bash
dotnet add package RestLib
```

For demos, tests, and quick prototypes, add the optional in-memory adapter:

```bash
dotnet add package RestLib.InMemory
```

For production use with Entity Framework Core:

```bash
dotnet add package RestLib.EntityFrameworkCore
```

## Quick Start

Create a new app:

```bash
dotnet new web -n MyApi
cd MyApi
```

Define a model:

```csharp
public class Product
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public decimal Price { get; set; }
}
```

Configure RestLib:

```csharp
using RestLib;
using RestLib.InMemory;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRestLib();
builder.Services.AddRestLibInMemory<Product, Guid>(p => p.Id, Guid.NewGuid);
builder.Services.AddOpenApi();

var app = builder.Build();

app.MapOpenApi();
app.MapScalarApiReference();

app.MapRestLib<Product, Guid>("/api/products", config =>
{
    config.AllowAnonymous();
});

app.Run();
```

Run the app and open the API reference at `/scalar`:

```bash
dotnet run
```

That gives you:

- `GET /api/products` - list all with cursor pagination
- `GET /api/products/{id}` - fetch a single resource
- `POST /api/products` - create
- `PUT /api/products/{id}` - replace
- `PATCH /api/products/{id}` - partially update
- `DELETE /api/products/{id}` - delete

### Quick Start (folder convention)

For JSON-driven resources, the recommended path is one file per resource under `Models/`.

`Program.cs`:

```csharp
using RestLib;
using RestLib.InMemory;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRestLib();
builder.Services.AddRestLibInMemory<Product, Guid>(p => p.Id, Guid.NewGuid);
builder.Services.AddRestLibFromFolder("Models");

var app = builder.Build();
app.MapJsonResources();
app.Run();
```

Single-file variant:

```csharp
builder.Services.AddJsonResourceFromFile<Product, Guid>("Models/Products.json");
```

Two-model single-file variant:

```csharp
builder.Services.AddJsonResourceFromFile<ProductDto, ProductEntity, Guid>("Models/Products.json");
```

`Models/Products.json`:

```json
{
  "$schema": "https://raw.githubusercontent.com/Adrian01987/RestLib/main/schemas/restlib-resource.schema.json",
  "EntityType": "Product, MyApi",
  "Name": "products",
  "Route": "/api/products",
  "AllowAnonymousAll": true,
  "Validation": {
    "Name": { "Required": true },
    "Price": { "Min": 0.01 }
  }
}
```

See [docs/guides/json-resources.md](docs/guides/json-resources.md) for the full walkthrough.

### Separate API and DB models

When you want to expose a DTO but persist a different model, register a mapper and use the three-type overload:

```csharp
builder.Services.AddRestLibMapper<ProductDto, ProductEntity, ProductMapper>();

app.MapRestLib<ProductDto, ProductEntity, Guid>("/api/products", config =>
{
    config.AllowAnonymous();
    config.AllowFiltering(p => p.CategoryId);
    config.AllowSorting(p => p.Name, p => p.Price);
    config.AllowFieldSelection(p => p.Id, p => p.Name, p => p.Price);
    config.AllowSearch(p => p.Name, p => p.Description);
});
```

JSON resources can declare the same pattern with a `Mapping` section:

```json
{
  "EntityType": "ProductDto, MyApi",
  "Name": "products",
  "Route": "/api/products",
  "Mapping": {
    "DbType": "ProductEntity, MyApi",
    "Mapper": "ProductMapper"
  },
  "Filtering": ["CategoryId"],
  "Sorting": ["Name", "Price"],
  "FieldSelection": ["Id", "Name", "Price"],
  "Search": ["Name", "Description"]
}
```

### Collection Search

RestLib can opt resources into a simple OR-of-contains search across configured string
properties. The default query parameter is `q`, and JSON resources support the same
feature with configurable search options.

Search is intentionally limited to lightweight collection queries rather than full-text
indexing, ranking, or fuzzy matching. For full fluent and JSON examples, nested-path
usage, and the strict `Mapping.Auto` shortcut, see
[docs/guides/query-features.md](docs/guides/query-features.md).

### Composite keys

RestLib supports ordered two-part composite keys through `RestLibCompositeKey<TFirst, TSecond>`
for both fluent and JSON-backed resources. Item routes use two path segments such as
`/api/tenant-products/{tenantId}/{sku}`.

For complete fluent setup, JSON `Key` examples, and related query behavior, see
[docs/guides/query-features.md](docs/guides/query-features.md).

### EF Core Quick Start

For a database-backed path, register your `DbContext`, call
`AddRestLibEfCore<AppDbContext, TEntity, TKey>()`, and map the same RestLib endpoints over
your EF Core model. RestLib handles endpoint wiring and repository behavior, while your
application continues to own schema creation and migrations.

The sample app uses `EnsureCreated()` as a zero-setup demo path, but production EF Core
apps should use normal migrations and `Database.Migrate()`. For the full setup and migration
workflow, see [docs/guides/ef-core-migrations.md](docs/guides/ef-core-migrations.md).

## Why RestLib

Every backend project starts the same way: define a model, write CRUD endpoints, add validation, handle errors, set up pagination, wire Swagger, and repeat for every entity.

RestLib removes that repetition while keeping the parts that matter explicit:

- Proper REST semantics inspired by the Zalando REST API Guidelines
- Secure-by-default endpoints with per-operation opt-out
- Machine-readable RFC 9457 Problem Details responses
- Opt-in HATEOAS hypermedia links (Richardson Maturity Model Level 3)
- Hook-based extensibility instead of controller inheritance
- OpenAPI metadata and package-ready defaults out of the box

## Choosing RestLib

RestLib is a good fit when you want many resource-oriented endpoints to share one
well-documented API contract without hand-writing the same CRUD, pagination, validation,
OpenAPI, and error-handling code repeatedly.

| Scenario | Recommended path |
| --- | --- |
| Prototype, demo, or test fixture | `RestLib.InMemory` with fluent endpoint configuration |
| Typical database-backed API | `RestLib.EntityFrameworkCore` over your existing `DbContext` |
| Configuration-heavy resource catalog | JSON resources loaded from `Models/` or `appsettings.json` |
| Public DTO differs from persistence model | Two-model resources plus an explicit `IRestLibMapper` |
| Workflow, orchestration, or cross-resource transaction | Custom Minimal API endpoint beside generated RestLib resources |
| Provider-specific search, deep authorization scoping, or complex query semantics | Custom repository or custom endpoint |

Prefer custom endpoints for commands that are not naturally resource CRUD: checkout,
payment capture, state-machine transitions, report generation, and other workflows where
the route should express a business action instead of exposing a generic persistence
operation. Generated RestLib endpoints can still coexist with those custom routes.

## Features

### Secure by Default

All endpoints require authorization unless you opt out per operation:

```csharp
app.MapRestLib<Product, Guid>("/api/products", config =>
{
    config.AllowAnonymous(RestLibOperation.GetAll, RestLibOperation.GetById);
    config.RequirePolicy(RestLibOperation.Delete, "AdminOnly");
});
```

### Standards-Compliant Responses

RestLib follows the [Zalando REST API Guidelines](https://opensource.zalando.com/restful-api-guidelines/) and uses RFC 9457 Problem Details for error payloads.

- `snake_case` JSON properties
- Cursor-based pagination API (forward-only by design; the EF Core adapter uses keyset cursors for supported stable sorts and falls back to encoded offsets otherwise — see [ADR-001](docs/adr/001-cursor-pagination.md))
- Structured validation and error responses
- Consistent HTTP status codes

```json
{
  "type": "/problems/not-found",
  "title": "Resource Not Found",
  "status": 404,
  "detail": "Product with ID '999' does not exist.",
  "instance": "/api/products/999"
}
```

### Advanced Filtering

RestLib supports allow-listed query-string filtering for scalar properties and nested
reference-property paths. Equality filters use direct query parameters, while range,
string, and membership operators use bracket syntax such as `price[gte]` and
`name[contains]`.

For complete fluent setup, operator coverage, nested-path examples, and JSON equivalents,
see [docs/guides/query-features.md](docs/guides/query-features.md).

### Sorting

Sorting uses an allow-list of sortable properties with optional default ordering.
Query names use snake_case, directions are `asc` or `desc`, and nested reference-property
paths use dotted names such as `customer.name`.

For fluent examples, multi-sort patterns, nested-path rules, and JSON configuration,
see [docs/guides/query-features.md](docs/guides/query-features.md).

### Rate Limiting

Integrate with ASP.NET Core's rate limiting middleware to throttle requests per operation:

```csharp
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("read-policy", o => { o.PermitLimit = 100; o.Window = TimeSpan.FromMinutes(1); });
    options.AddFixedWindowLimiter("write-policy", o => { o.PermitLimit = 20; o.Window = TimeSpan.FromMinutes(1); });
});

app.UseRateLimiter();

app.MapRestLib<Product, Guid>("/api/products", config =>
{
    config.UseRateLimiting("read-policy", RestLibOperation.GetAll, RestLibOperation.GetById);
    config.UseRateLimiting("write-policy", RestLibOperation.Create, RestLibOperation.Update);
});
```

Set a default policy and exempt specific operations:

```csharp
config.UseRateLimiting("default-policy");
config.DisableRateLimiting(RestLibOperation.GetById);
```

Rate limiting is opt-in. RestLib applies the named policy to endpoints;
the application defines and registers the actual policies.

### Field Selection

Field selection lets clients request sparse fieldsets such as `?fields=id,name,price`
instead of returning the full entity every time. Unknown or disallowed fields return a
400 Problem Details response, and nested reference-property paths are supported with
snake_case dotted query names.

For full examples, JSON configuration, nested sparse output behavior, and interactions
with filtering/sorting/pagination, see
[docs/guides/query-features.md](docs/guides/query-features.md).

#### Nested object responses (opt-in)

Sparse field selection defaults to flat dotted keys for backward compatibility, but
resources can opt into rebuilt nested objects when that response shape is a better fit
for clients. The opt-in applies only to sparse responses; dense fallback projection
continues to use flat output.

For the fluent and JSON opt-in shapes plus concrete examples of both response modes,
see [docs/guides/query-features.md](docs/guides/query-features.md#nested-object-responses-opt-in).

### Batch Operations

Batch endpoints support create, update, patch, and delete actions over multiple
resources in one request. Responses report per-item status, returning 200 when all
items succeed and 207 Multi-Status when results are mixed.

For request examples, batch limits, and hook/validation behavior, see
[docs/guides/extensibility-and-operations.md](docs/guides/extensibility-and-operations.md).

### HATEOAS Hypermedia Links

Enable HATEOAS to add CRUD-aware `_links` metadata to entity responses. RestLib can also
merge in custom link relations through `IHateoasLinkProvider<TEntity, TKey>` when you want
to point clients at related resources.

For full response examples and custom link-provider registration, see
[docs/guides/extensibility-and-operations.md](docs/guides/extensibility-and-operations.md).

### Select Operations

RestLib lets you expose only the CRUD operations you want and combine generated endpoints
with custom routes when needed. The same resource surface can be declared fluently, loaded
from JSON files, or bound from `appsettings.json`.

For full examples covering operation allow-lists, JSON registration, the new
`UnifiedTypeResolver`, and two-model folder loading, see
[docs/guides/extensibility-and-operations.md](docs/guides/extensibility-and-operations.md).

### Extensible via Hooks

Hooks provide strongly typed pipeline customization without controller inheritance or
framework subclassing. JSON can select named hooks per operation while C# keeps the actual
behavior implementation and test surface.

For fluent and named-hook examples, see
[docs/guides/extensibility-and-operations.md](docs/guides/extensibility-and-operations.md).

### Persistence-Agnostic

RestLib stays persistence-agnostic: use the in-memory adapter, the EF Core adapter, or
your own repository implementation behind the same endpoint surface.

For a custom repository example and guidance on where the EF Core adapter fits, see
[docs/guides/extensibility-and-operations.md](docs/guides/extensibility-and-operations.md).

### EF Core Adapter

The official EF Core adapter wires RestLib over a `DbContext` without changing your EF Core
model ownership. It supports filtering, sorting, counting, pagination, batch operations,
and conditional field-selection projection pushdown, with some adapter-specific limits.

For registration examples and the broader adapter overview, see
[docs/guides/extensibility-and-operations.md](docs/guides/extensibility-and-operations.md).
For production migration workflow, see
[docs/guides/ef-core-migrations.md](docs/guides/ef-core-migrations.md).

#### Current EF Core Adapter Limitations

- **At most two key parts** - the adapter supports scalar keys and ordered two-part composite keys via `RestLibCompositeKey<TFirst, TSecond>`. Keys with more than two parts are not supported.
- **Keyset pagination with offset fallback** - the EF Core adapter uses last-seen sort values plus the key for supported stable sorts, but falls back to encoded offset cursors for unsupported sort shapes.
- **Projection pushdown is opt-in and conditional** - when enabled, EF Core pushes down direct scalar field selections and still includes key/filter/sort columns in SQL. Nested field selections currently fall back to post-fetch projection, and any non-projectable selection still falls back when HATEOAS, ETag, or hooks are active.
- **Nested query paths are reference-only** - filtering, sorting, and field selection support dot-separated nested reference-property paths such as `customer.email`. Collection-valued paths are not supported, nested sparse responses use dotted output keys, and PATCH handling still operates on direct entity properties.
- **Constraint mapping is provider-limited** - database constraint classification still relies primarily on exception-message inspection and is not yet specialized per provider.

Use the adapter when you want the standard RestLib endpoint surface over a typical
EF Core model, including two-part composite-key resources. Expect to write a custom
repository if you need keys with more than two parts, deep/navigational query semantics,
or SQL-level projection beyond direct scalar property selection.

### Versioning

RestLib integrates with common ASP.NET Core versioning patterns via route groups, including
URL prefixes and `Asp.Versioning.Http` when you need header, query-string, or media-type
versioning.

For complete examples of URL-prefix versioning, prefix-less overloads, and
`Asp.Versioning.Http` integration, see
[docs/guides/extensibility-and-operations.md](docs/guides/extensibility-and-operations.md).

#### URL prefix versioning

#### Prefix-less overload on a route group

#### With Asp.Versioning.Http

## Performance

RestLib adds minimal overhead compared to hand-written Minimal APIs. In some cases, it is faster due to optimized code paths.

| Operation | Raw API  | RestLib  | Overhead | Memory |
| --------- | -------- | -------- | -------- | ------ |
| GET by ID | 170.5 us | 217.4 us | +27%     | +4%    |
| GET all   | 313.4 us | 271.7 us | -13%     | +14%   |
| POST      | 265.5 us | 384.8 us | +45%     | +13%   |
| PUT       | 232.1 us | 282.0 us | +22%     | +13%   |

Benchmarks were run on .NET 10.0.5 with 100 seeded items on Linux (Ubuntu 24.04).
Absolute times are higher than typical due to running without process priority
elevation — focus on the **relative overhead** (Ratio column) rather than raw
microseconds. Re-run with `cd benchmarks/RestLib.Benchmarks && dotnet run -c Release`
to get numbers for your hardware.

<details>
<summary>Full benchmark results</summary>

```
BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Core i3-8130U CPU 2.20GHz (Kaby Lake), 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.201
  [Host]     : .NET 10.0.5 (10.0.5, 10.0.526.15411), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.5 (10.0.5, 10.0.526.15411), X64 RyuJIT x86-64-v3

| Method                                      | Categories     | Mean     | Error    | StdDev    | Median   | Ratio | RatioSD | Allocated | Alloc Ratio |
|-------------------------------------------- |--------------- |---------:|---------:|----------:|---------:|------:|--------:|----------:|------------:|
| 'Raw Minimal API - POST'                    | Create         | 265.5 us | 30.76 us |  89.74 us | 260.1 us |  1.12 |    0.56 |  12.46 KB |        1.00 |
| 'RestLib - POST'                            | Create         | 384.8 us | 71.82 us | 203.73 us | 326.7 us |  1.63 |    1.07 |  14.10 KB |        1.13 |
|                                             |                |          |          |           |          |       |         |           |             |
| 'Raw Minimal API - GET all'                 | GetAll         | 313.4 us | 39.61 us | 111.73 us | 285.8 us |  1.12 |    0.54 |  16.74 KB |        1.00 |
| 'RestLib - GET all'                         | GetAll         | 271.7 us | 31.49 us |  92.86 us | 246.1 us |  0.97 |    0.46 |  19.05 KB |        1.14 |
|                                             |                |          |          |           |          |       |         |           |             |
| 'RestLib - GET all (no fields)'             | GetAll_Fields  | 289.6 us | 41.97 us | 121.78 us | 261.4 us |  1.18 |    0.70 |  19.07 KB |        1.00 |
| 'RestLib - GET all (?fields=id,name)'       | GetAll_Fields  | 475.3 us | 51.86 us | 149.62 us | 448.8 us |  1.93 |    0.99 |  39.98 KB |        2.10 |
| 'RestLib - GET all (?fields=id,name,price)' | GetAll_Fields  | 563.9 us | 88.23 us | 255.96 us | 498.5 us |  2.29 |    1.42 |  43.43 KB |        2.28 |
|                                             |                |          |          |           |          |       |         |           |             |
| 'Raw Minimal API - GET by ID'               | GetById        | 170.5 us | 26.94 us |  79.43 us | 149.6 us |  1.26 |    0.93 |   9.76 KB |        1.00 |
| 'RestLib - GET by ID'                       | GetById        | 217.4 us | 29.76 us |  82.47 us | 203.9 us |  1.61 |    1.07 |  10.13 KB |        1.04 |
|                                             |                |          |          |           |          |       |         |           |             |
| 'RestLib - GET by ID (no fields)'           | GetById_Fields | 124.3 us | 14.85 us |  42.84 us | 112.1 us |  1.12 |    0.55 |  10.21 KB |        1.00 |
| 'RestLib - GET by ID (?fields=id,name)'     | GetById_Fields | 162.7 us | 16.38 us |  46.47 us | 149.7 us |  1.46 |    0.65 |  12.22 KB |        1.20 |
|                                             |                |          |          |           |          |       |         |           |             |
| 'Raw Minimal API - PUT'                     | Update         | 232.1 us | 24.69 us |  72.81 us | 221.1 us |  1.10 |    0.51 |  12.78 KB |        1.00 |
| 'RestLib - PUT'                             | Update         | 282.0 us | 37.88 us | 108.06 us | 258.5 us |  1.34 |    0.69 |  14.44 KB |        1.13 |
```

</details>

## Learn More

- [Minimal sample app](https://github.com/Adrian01987/RestLib/blob/main/samples/RestLib.Sample/README.md)
- [Ecommerce reference sample](https://github.com/Adrian01987/RestLib/blob/main/samples/RestLib.Sample.Ecommerce/README.md)
- [EF Core migrations guide](https://github.com/Adrian01987/RestLib/blob/main/docs/guides/ef-core-migrations.md)
- [JSON resources guide](https://github.com/Adrian01987/RestLib/blob/main/docs/guides/json-resources.md)
- [Architecture decisions](https://github.com/Adrian01987/RestLib/tree/main/docs/adr)
- [Benchmarks](https://github.com/Adrian01987/RestLib/blob/main/benchmarks/RestLib.Benchmarks/README.md)
- [Changelog](https://github.com/Adrian01987/RestLib/blob/main/CHANGELOG.md)
- [Contributing guide](https://github.com/Adrian01987/RestLib/blob/main/CONTRIBUTING.md)

## Guides

- [JSON resources guide](docs/guides/json-resources.md) - folder-based JSON resource setup, two-model JSON resources, and configuration troubleshooting
- [Query features guide](docs/guides/query-features.md) - filtering, sorting, field selection, nested paths, search, and composite keys
- [Extensibility and operations guide](docs/guides/extensibility-and-operations.md) - batch operations, hooks, operation selection, adapters, and versioning
- [EF Core migrations guide](docs/guides/ef-core-migrations.md) - production EF Core schema and migration workflow

## Architecture Decisions

Key decisions are documented as Architecture Decision Records:

| ADR | Decision |
| --- | -------- |
| [ADR-001](https://github.com/Adrian01987/RestLib/blob/main/docs/adr/001-cursor-pagination.md) | Cursor-based pagination over offset |
| [ADR-002](https://github.com/Adrian01987/RestLib/blob/main/docs/adr/002-secure-by-default.md) | Authorization required by default |
| [ADR-003](https://github.com/Adrian01987/RestLib/blob/main/docs/adr/003-minimal-apis.md) | Minimal APIs over controllers |
| [ADR-004](https://github.com/Adrian01987/RestLib/blob/main/docs/adr/004-snake-case-json.md) | `snake_case` JSON naming |
| [ADR-005](https://github.com/Adrian01987/RestLib/blob/main/docs/adr/005-problem-details.md) | RFC 9457 Problem Details for errors |
| [ADR-006](https://github.com/Adrian01987/RestLib/blob/main/docs/adr/006-operation-selection.md) | Operation allowlists and denylists |
| [ADR-007](https://github.com/Adrian01987/RestLib/blob/main/docs/adr/007-field-selection.md) | Hybrid field projection strategy |
| [ADR-008](https://github.com/Adrian01987/RestLib/blob/main/docs/adr/008-batch-operations.md) | Batch operations with partial success |
| [ADR-009](https://github.com/Adrian01987/RestLib/blob/main/docs/adr/009-sorting.md) | Allow-list sorting with default sort |
| [ADR-010](https://github.com/Adrian01987/RestLib/blob/main/docs/adr/010-versioning.md) | API versioning via route groups |
| [ADR-011](https://github.com/Adrian01987/RestLib/blob/main/docs/adr/011-filtering.md) | Query parameter filtering |
| [ADR-012](https://github.com/Adrian01987/RestLib/blob/main/docs/adr/012-hook-pipeline.md) | Hook pipeline for extensibility |
| [ADR-013](https://github.com/Adrian01987/RestLib/blob/main/docs/adr/013-filter-operators.md) | Filter operators beyond equality |
| [ADR-014](https://github.com/Adrian01987/RestLib/blob/main/docs/adr/014-etag-support.md) | ETag support for caching and concurrency |
| [ADR-015](https://github.com/Adrian01987/RestLib/blob/main/docs/adr/015-data-annotation-validation.md) | Data Annotation validation |
| [ADR-016](https://github.com/Adrian01987/RestLib/blob/main/docs/adr/016-json-resource-configuration.md) | JSON resource configuration |
| [ADR-017](https://github.com/Adrian01987/RestLib/blob/main/docs/adr/017-rate-limiting.md) | Rate limiting integration |
| [ADR-018](https://github.com/Adrian01987/RestLib/blob/main/docs/adr/018-patch-json-element-coupling.md) | PATCH JsonElement coupling acknowledgement |
| [ADR-019](https://github.com/Adrian01987/RestLib/blob/main/docs/adr/019-hateoas.md) | HATEOAS hypermedia links |
| [ADR-020](https://github.com/Adrian01987/RestLib/blob/main/docs/adr/020-structured-logging.md) | Structured logging |
| [ADR-021](https://github.com/Adrian01987/RestLib/blob/main/docs/adr/021-ef-core-adapter.md) | EF Core repository adapter |
| [ADR-022](https://github.com/Adrian01987/RestLib/blob/main/docs/adr/022-per-file-json-resources.md) | Per-file JSON resources |
| [ADR-023](https://github.com/Adrian01987/RestLib/blob/main/docs/adr/023-json-validation-rules.md) | JSON validation rules |
| [ADR-024](https://github.com/Adrian01987/RestLib/blob/main/docs/adr/024-two-model-resources-and-the-mapping-seam.md) | Two-model resources and the mapping seam |
| [ADR-025](https://github.com/Adrian01987/RestLib/blob/main/docs/adr/025-composite-key-support.md) | Two-part composite key support |
| [ADR-026](https://github.com/Adrian01987/RestLib/blob/main/docs/adr/026-projection-pushdown.md) | EF Core projection pushdown |
| [ADR-027](https://github.com/Adrian01987/RestLib/blob/main/docs/adr/027-row-level-security-application-owned.md) | Row-level security is application-owned |
| [ADR-028](https://github.com/Adrian01987/RestLib/blob/main/docs/adr/028-unit-of-work-application-owned.md) | Unit of work is application-owned |

## Packages

| Package | Description | NuGet |
| ------- | ----------- | ----- |
| `RestLib` | Core library | [RestLib](https://www.nuget.org/packages/RestLib/) |
| `RestLib.InMemory` | In-memory repository for testing and prototyping | [RestLib.InMemory](https://www.nuget.org/packages/RestLib.InMemory/) |
| `RestLib.EntityFrameworkCore` | EF Core repository adapter for production databases | [RestLib.EntityFrameworkCore](https://www.nuget.org/packages/RestLib.EntityFrameworkCore/) |

## Requirements

- .NET 10.0 or later
- ASP.NET Core Minimal APIs

## Known Limitations

- **Forward-only cursor pagination** — cursors support forward traversal only; there is no backward/previous-page navigation.
- **Cursor contract, adapter-specific implementation** — RestLib exposes an opaque cursor API. The InMemory adapter still uses encoded offsets/indexes, while the EF Core adapter uses keyset cursors for supported stable sorts and falls back to offsets otherwise.
- **Field selection pushdown is adapter-dependent** — the core endpoint layer can project after retrieval for any repository. The EF Core adapter can opt into conditional SQL projection pushdown for direct scalar fields via `EnableProjectionPushdown`; nested selections and requests using HATEOAS, ETags, or hooks fall back to materialized projection.
- **Nested query paths are reference-only** — filtering, sorting, and field selection support dotted nested reference-property paths such as `customer.email`, but collection-valued paths are not supported and sparse nested responses use dotted output keys rather than nested objects.
- **Built-in search is intentionally limited** — RestLib supports configured OR-of-contains search across string fields, but it does not provide full-text indexing, ranking, fuzzy matching, or provider-specific search features.
- **Row-level scoping is application-owned** — apply tenant/user scoping in EF Core global query filters, database policies, or custom repositories before generated endpoints query data.
- **Cross-resource transactions are application-owned** — use custom endpoints or transactional repositories for workflows such as checkout, payment capture, or multi-resource state changes.
- **No CORS configuration** — RestLib does not configure CORS. If your API is consumed by browsers, add ASP.NET Core's built-in CORS middleware:

  ```csharp
  builder.Services.AddCors(options =>
  {
      options.AddDefaultPolicy(policy => policy
          .AllowAnyOrigin()
          .AllowAnyMethod()
          .AllowAnyHeader());
  });
  app.UseCors();
  ```

  See the [ASP.NET Core CORS documentation](https://learn.microsoft.com/en-us/aspnet/core/security/cors) for production-ready policies.

## Contributing

Contributions are welcome. Read the [contributing guide](https://github.com/Adrian01987/RestLib/blob/main/CONTRIBUTING.md).

## License

This project is licensed under the MIT License. See the [license](https://github.com/Adrian01987/RestLib/blob/main/LICENSE).

## Acknowledgments

- [Zalando RESTful API Guidelines](https://opensource.zalando.com/restful-api-guidelines/)
- [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457)
- [FastEndpoints](https://fast-endpoints.com/)
