# RestLib

> **3 lines to a production-ready REST API**

[![Build](https://github.com/Adrian01987/RestLib/actions/workflows/ci.yml/badge.svg)](https://github.com/Adrian01987/RestLib/actions/workflows/ci.yml)
[![Coverage](https://codecov.io/gh/Adrian01987/RestLib/branch/main/graph/badge.svg)](https://codecov.io/gh/Adrian01987/RestLib)
[![NuGet](https://img.shields.io/nuget/v/RestLib.svg)](https://www.nuget.org/packages/RestLib/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/Adrian01987/RestLib/blob/main/LICENSE)

RestLib is a .NET 10 library for ASP.NET Core Minimal APIs that generates CRUD endpoints from your model and repository. It bakes in secure defaults, cursor-based pagination APIs, filtering, sorting, field selection, HATEOAS hypermedia links, OpenAPI metadata, and RFC 9457 Problem Details so you can ship consistent APIs faster. Some capabilities depend on the repository adapter and may have implementation-specific limits.

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

### EF Core Quick Start

For a database-backed path, define the same model plus a `DbContext`:

```csharp
using Microsoft.EntityFrameworkCore;

public class Product
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public decimal Price { get; set; }
}

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Product> Products => Set<Product>();
}
```

Register EF Core, the RestLib adapter, and ensure the database exists at startup:

```csharp
using Microsoft.EntityFrameworkCore;
using RestLib;
using RestLib.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRestLib();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite("Data Source=app.db"));
builder.Services.AddRestLibEfCore<AppDbContext, Product, Guid>();
builder.Services.AddOpenApi();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

app.MapOpenApi();
app.MapScalarApiReference();

app.MapRestLib<Product, Guid>("/api/products", config =>
{
    config.AllowAnonymous();
});

app.Run();
```

That exposes the same CRUD endpoints as the in-memory quick start, but backed by EF Core
and SQLite. If your key property follows EF Core conventions, `AddRestLibEfCore` can infer
it automatically; otherwise provide a `KeySelector` in the options callback.

## Why RestLib

Every backend project starts the same way: define a model, write CRUD endpoints, add validation, handle errors, set up pagination, wire Swagger, and repeat for every entity.

RestLib removes that repetition while keeping the parts that matter explicit:

- Proper REST semantics inspired by the Zalando REST API Guidelines
- Secure-by-default endpoints with per-operation opt-out
- Machine-readable RFC 9457 Problem Details responses
- Opt-in HATEOAS hypermedia links (Richardson Maturity Model Level 3)
- Hook-based extensibility instead of controller inheritance
- OpenAPI metadata and package-ready defaults out of the box

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

Enable query-string filtering with no custom parser code:

```csharp
app.MapRestLib<Product, Guid>("/api/products", config =>
{
    config.AllowFiltering(p => p.CategoryId, p => p.IsActive);
    config.AllowFiltering(p => p.Price, FilterOperators.Comparison);
    config.AllowFiltering(p => p.Name, FilterOperators.String);
});
```

Equality filters use direct query parameters:

```text
GET /api/products?category_id=5&is_active=true
```

Operator filters use bracket syntax for ranges, partial matches, and set membership:

```text
GET /api/products?price[gte]=20&price[lte]=100
GET /api/products?name[contains]=widget
GET /api/products?status[in]=active,pending
```

Ten operators are available: `eq`, `neq`, `gt`, `lt`, `gte`, `lte`, `contains`,
`starts_with`, `ends_with`, and `in`. Each property declares which operators it supports via
preset arrays (`FilterOperators.Comparison`, `FilterOperators.String`,
`FilterOperators.All`) or individual `FilterOperator` values. `Eq` is always
implicitly allowed. See [ADR-013](docs/adr/013-filter-operators.md) for details.

### Sorting

Control result ordering with an allow-list of sortable properties:

```csharp
app.MapRestLib<Product, Guid>("/api/products", config =>
{
    config.AllowSorting(p => p.Price, p => p.Name);
    config.DefaultSort("name:asc");
});
```

```text
GET /api/products?sort=price:asc,name:desc&limit=20
```

Sort fields use snake_case names and support `asc`/`desc` directions.
Disallowed fields return a 400 Problem Details response.

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

Return only the fields your client needs with sparse fieldsets:

```csharp
app.MapRestLib<Product, Guid>("/api/products", config =>
{
    config.AllowFieldSelection(p => p.Id, p => p.Name, p => p.Price, p => p.CategoryId);
});
```

```http
GET /api/products?fields=id,name,price
```

Only the selected fields are included in the response. Unknown or disallowed
fields return a 400 Problem Details response. If no `fields` parameter is sent,
the full entity is returned.

Field selection works with both GetAll (collection) and GetById (single entity)
endpoints, and combines with filtering, sorting, and pagination.

### Batch Operations

Create, update, patch, or delete multiple resources in a single request:

```csharp
app.MapRestLib<Product, Guid>("/api/products", config =>
{
    config.EnableBatch(BatchAction.Create, BatchAction.Delete, BatchAction.Patch);
});
```

```http
POST /api/products/batch
Content-Type: application/json

{
  "action": "create",
  "items": [
    { "name": "Keyboard", "price": 49.99 },
    { "name": "Mouse", "price": 29.99 }
  ]
}
```

The response reports per-item status. All succeeded returns 200; mixed results
return 207 Multi-Status with individual status codes per item.

Batch size is limited to 100 items by default (configurable via
`RestLibOptions.MaxBatchSize`). Hooks fire once per item, and validation runs
per item with errors reported individually.

### HATEOAS Hypermedia Links

Enable HAL-style `_links` on every entity response for discoverability:

```csharp
builder.Services.AddRestLib(opts =>
{
    opts.EnableHateoas = true;
});
```

Responses include contextual navigation links:

```json
{
  "id": "a1b2c3d4-...",
  "name": "Keyboard",
  "price": 49.99,
  "_links": {
    "self":       { "href": "https://api.example.com/api/products/a1b2c3d4-..." },
    "collection": { "href": "https://api.example.com/api/products" },
    "update":     { "href": "https://api.example.com/api/products/a1b2c3d4-..." },
    "patch":      { "href": "https://api.example.com/api/products/a1b2c3d4-..." }
  }
}
```

Links are CRUD-aware: `update`, `patch`, and `delete` only appear when those
operations are enabled on the endpoint. Batch responses include per-item links.

For custom link relations (e.g., related resources), implement
`IHateoasLinkProvider<TEntity, TKey>`:

```csharp
public class ProductLinkProvider : IHateoasLinkProvider<Product, Guid>
{
    public IEnumerable<HateoasLink> GetLinks(Product entity, Guid key, string baseUrl, string collectionPath)
    {
        yield return new HateoasLink("category", $"{baseUrl}/api/categories/{entity.CategoryId}");
    }
}

builder.Services.AddHateoasLinkProvider<Product, Guid, ProductLinkProvider>();
```

### Select Operations

Expose only the operations you want, and mix custom endpoints with generated ones:

```csharp
app.MapRestLib<Category, Guid>("/api/categories", config =>
{
    config.IncludeOperations(RestLibOperation.GetAll, RestLibOperation.GetById);
});

app.MapPost("/api/categories", async (Category category, IRepository<Category, Guid> repo) =>
{
    return Results.Created($"/api/categories/{category.Id}", await repo.CreateAsync(category));
});
```

You can also move this declarative resource configuration out of `Program.cs` and into JSON while keeping your model, repository, and hooks strongly typed:

```json
{
  "RestLib": {
    "Resources": {
      "Products": {
        "Name": "products",
        "Route": "/api/products",
        "AllowAnonymousAll": true,
        "Operations": {
          "Exclude": ["Delete"]
        },
        "Filtering": ["CategoryId", "IsActive"],
        "Sorting": ["Price", "Name", "CreatedAt"],
        "DefaultSort": "name:asc",
        "OpenApi": {
          "Tag": "Product",
          "Summaries": {
            "GetAll": "List products"
          }
        }
      }
    }
  }
}
```

```csharp
var productResource = builder.Configuration
    .GetSection("RestLib:Resources:Products")
    .Get<RestLibJsonResourceConfiguration>()!;

builder.Services.AddJsonResource<Product, Guid>(productResource);

var app = builder.Build();
app.MapJsonResources();
```

### Extensible via Hooks

Inject custom logic into the pipeline without subclassing framework types:

```csharp
app.MapRestLib<Product, Guid>("/api/products", config =>
{
    config.UseHooks(hooks =>
    {
        hooks.BeforePersist = ctx =>
        {
            if (ctx.Entity is Product product && ctx.Operation == RestLibOperation.Create)
            {
                product.CreatedAt = DateTime.UtcNow;
            }

            return Task.CompletedTask;
        };
    });
});
```

If you want a cleaner startup file, JSON config can select named hooks per operation while the hook implementations stay in C#:

```csharp
builder.Services.AddNamedHook<Product, Guid>(HookNames.SetUpdatedAt, ctx =>
{
    if (ctx.Entity is Product product)
    {
        product.UpdatedAt = ctx.Operation == RestLibOperation.Create ? null : DateTime.UtcNow;
    }

    return Task.CompletedTask;
});
```

```json
{
  "Hooks": {
    "BeforePersist": {
      "ByOperation": {
        "Create": ["SetUpdatedAt"],
        "Update": ["SetUpdatedAt"],
        "Patch": ["SetUpdatedAt"]
      }
    }
  }
}
```

This keeps route, auth, filtering, operation selection, OpenAPI metadata, and hook selection in JSON while your actual behavior remains strongly typed and testable in C#. A simple pattern is to centralize hook names in a `HookNames` class and use those constants when registering handlers.

### Persistence-Agnostic

Use the in-memory adapter or plug in your own repository implementation:

```csharp
public class ProductRepository : IRepository<Product, Guid>
{
    private readonly MyDbContext _db;

    public ProductRepository(MyDbContext db)
    {
        _db = db;
    }

    public async Task<Product?> GetByIdAsync(Guid id, CancellationToken ct)
        => await _db.Products.FindAsync([id], ct);

    // Implement the remaining IRepository members...
}

builder.Services.AddRepository<Product, Guid, ProductRepository>();
```

### EF Core Adapter

Use the official EF Core adapter instead of writing a custom repository:

```csharp
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite("Data Source=app.db"));

builder.Services.AddRestLibEfCore<AppDbContext, Product, Guid>();
```

The adapter auto-detects the primary key from EF Core model metadata. To customize
options:

```csharp
builder.Services.AddRestLibEfCore<AppDbContext, Product, Guid>(options =>
{
    options.KeySelector = p => p.Id;
    options.UseAsNoTracking = false;
});
```

The EF Core adapter supports RestLib's filtering, sorting, counting, pagination,
batch operations, and hooks on top of EF Core, with server-side query translation
for filtering, sorting, and counting. Field selection is supported at the API layer,
but not pushed down to SQL. Some capabilities have important implementation limits;
see [Current EF Core Adapter Limitations](#current-ef-core-adapter-limitations)
and [ADR-021](docs/adr/021-ef-core-adapter.md).

#### Current EF Core Adapter Limitations

- **Single-property keys only** - the adapter auto-detects or accepts a single `TKey` value. Composite EF Core keys are not supported.
- **Keyset pagination with offset fallback** - the EF Core adapter uses last-seen sort values plus the key for supported stable sorts, but falls back to encoded offset cursors for unsupported sort shapes.
- **Field selection is not pushed down to SQL** - sparse fieldsets are applied after entity materialization, so EF Core still loads the full entity.
- **Top-level properties only** - filtering, sorting, field selection, and PATCH handling operate on direct entity properties. Nested or related-property paths are not supported.
- **Constraint mapping is provider-limited** - database constraint classification still relies primarily on exception-message inspection and is not yet specialized per provider.

Use the adapter when you want the standard RestLib endpoint surface over a typical
EF Core model, but expect to write a custom repository if you need composite-key
resources, true keyset pagination, SQL-level field projection, or deep/navigational
query semantics.

### Versioning

RestLib integrates with any ASP.NET Core versioning strategy via route groups.

#### URL prefix versioning

```csharp
var v1 = app.MapGroup("/api/v1");
var v2 = app.MapGroup("/api/v2");

v1.MapRestLib<Product, Guid>("/products", cfg =>
{
    cfg.AllowAnonymous();
    cfg.ExcludeOperations(RestLibOperation.Patch, RestLibOperation.Delete);
    cfg.AllowFiltering(p => p.CategoryId);
});

v2.MapRestLib<Product, Guid>("/products", cfg =>
{
    cfg.AllowAnonymous();
    cfg.AllowFiltering(p => p.CategoryId, p => p.IsActive);
    cfg.AllowSorting(p => p.Price, p => p.Name);
    cfg.AllowFieldSelection(p => p.Id, p => p.Name, p => p.Price);
});
```

#### Prefix-less overload on a route group

When the route group already has the full path configured, use the prefix-less overload:

```csharp
app.MapGroup("/api/v1/products").MapRestLib<Product, Guid>(cfg =>
{
    cfg.AllowAnonymous();
});
```

#### With Asp.Versioning.Http

```csharp
// Install: Asp.Versioning.Http
builder.Services.AddApiVersioning();

var versionedApi = app.NewVersionedApi("Products");

versionedApi
    .MapGroup("/api/v{version:apiVersion}/products")
    .HasApiVersion(1.0)
    .MapRestLib<Product, Guid>(cfg => cfg.AllowAnonymous());

versionedApi
    .MapGroup("/api/v{version:apiVersion}/products")
    .HasApiVersion(2.0)
    .MapRestLib<Product, Guid>(cfg =>
    {
        cfg.AllowAnonymous();
        cfg.AllowFieldSelection(p => p.Id, p => p.Name, p => p.Price);
    });
```

RestLib does not depend on `Asp.Versioning.Http` — install it only if you need
query-string, header, or media-type versioning strategies.

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

- [Sample app](https://github.com/Adrian01987/RestLib/blob/main/samples/RestLib.Sample/Program.cs)
- [Architecture decisions](https://github.com/Adrian01987/RestLib/tree/main/docs/adr)
- [Benchmarks](https://github.com/Adrian01987/RestLib/blob/main/benchmarks/RestLib.Benchmarks/README.md)
- [Changelog](https://github.com/Adrian01987/RestLib/blob/main/CHANGELOG.md)
- [Contributing guide](https://github.com/Adrian01987/RestLib/blob/main/CONTRIBUTING.md)

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
- **Post-fetch field selection** — field projection is applied after the full entity is retrieved from the repository, not pushed down to the data source.
- **Flat properties only** — filtering, sorting, and field selection operate on top-level entity properties; nested or related entity paths are not supported.
- **No built-in search** — full-text or fuzzy search is not included; implement it in your repository if needed.
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
