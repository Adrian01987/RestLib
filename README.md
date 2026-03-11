# RestLib

> **3 lines to a production-ready REST API**

[![Build](https://github.com/Adrian01987/RestLib/actions/workflows/ci.yml/badge.svg)](https://github.com/Adrian01987/RestLib/actions/workflows/ci.yml)
[![Coverage](https://codecov.io/gh/Adrian01987/RestLib/branch/main/graph/badge.svg)](https://codecov.io/gh/Adrian01987/RestLib)
[![NuGet](https://img.shields.io/nuget/v/RestLib.svg)](https://www.nuget.org/packages/RestLib/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/Adrian01987/RestLib/blob/main/LICENSE)

RestLib is a .NET 8 library for ASP.NET Core Minimal APIs that generates CRUD endpoints from your model and repository. It bakes in secure defaults, cursor pagination, filtering, OpenAPI metadata, and RFC 9457 Problem Details so you can ship consistent APIs faster.

## Install

Install the core package:

```bash
dotnet add package RestLib
```

For demos, tests, and quick prototypes, add the optional in-memory adapter:

```bash
dotnet add package RestLib.InMemory
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

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRestLib();
builder.Services.AddRestLibInMemory<Product, Guid>(p => p.Id, Guid.NewGuid);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapRestLib<Product, Guid>("/api/products", config =>
{
    config.AllowAnonymous();
});

app.Run();
```

Run the app and open Swagger:

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

## Why RestLib

Every backend project starts the same way: define a model, write CRUD endpoints, add validation, handle errors, set up pagination, wire Swagger, and repeat for every entity.

RestLib removes that repetition while keeping the parts that matter explicit:

- Proper REST semantics inspired by the Zalando REST API Guidelines
- Secure-by-default endpoints with per-operation opt-out
- Machine-readable RFC 9457 Problem Details responses
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
- Cursor-based pagination
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
});
```

Example request:

```text
GET /api/products?category_id=5&is_active=true
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

## Performance

RestLib adds minimal overhead compared to hand-written Minimal APIs. In some cases, it is faster due to optimized code paths.

| Operation | Raw API  | RestLib  | Overhead | Memory |
| --------- | -------- | -------- | -------- | ------ |
| GET by ID | 67.5 us  | 69.5 us  | +3%      | +2%    |
| GET all   | 173.3 us | 116.5 us | -33%     | +7%    |
| POST      | 97.3 us  | 99.4 us  | +2%      | +13%   |
| PUT       | 88.6 us  | 114.2 us | +29%     | +13%   |

Benchmarks were run on .NET 8.0 with 100 seeded items.

<details>
<summary>Full benchmark results</summary>

```
BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.7623)
Intel Core i3-8130U CPU 2.20GHz (Kaby Lake), 1 CPU, 4 logical and 2 physical cores
.NET SDK 9.0.308
  [Host]     : .NET 8.0.22 (8.0.2225.52707), X64 RyuJIT AVX2
  DefaultJob : .NET 8.0.22 (8.0.2225.52707), X64 RyuJIT AVX2

| Method                        | Categories | Mean      | Error    | StdDev    | Ratio | Allocated | Alloc Ratio |
|------------------------------ |----------- |----------:|---------:|----------:|------:|----------:|------------:|
| 'Raw Minimal API - POST'      | Create     |  97.30 us | 5.561 us | 16.396 us |  1.00 |  11.65 KB |        1.00 |
| 'RestLib - POST'              | Create     |  99.35 us | 1.567 us |  1.308 us |  1.02 |  13.20 KB |        1.13 |
|                               |            |           |          |           |       |           |             |
| 'Raw Minimal API - GET all'   | GetAll     | 173.26 us | 9.706 us | 28.619 us |  1.00 |  17.34 KB |        1.00 |
| 'RestLib - GET all'           | GetAll     | 116.54 us | 1.539 us |  3.037 us |  0.67 |  18.62 KB |        1.07 |
|                               |            |           |          |           |       |           |             |
| 'Raw Minimal API - GET by ID' | GetById    |  67.49 us | 4.209 us | 12.409 us |  1.00 |  10.15 KB |        1.00 |
| 'RestLib - GET by ID'         | GetById    |  69.48 us | 4.483 us | 13.218 us |  1.03 |  10.31 KB |        1.02 |
|                               |            |           |          |           |       |           |             |
| 'Raw Minimal API - PUT'       | Update     |  88.64 us | 1.745 us |  3.010 us |  1.00 |  12.22 KB |        1.00 |
| 'RestLib - PUT'               | Update     | 114.16 us | 6.813 us | 20.088 us |  1.29 |  13.86 KB |        1.13 |
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

## Packages

| Package | Description | NuGet |
| ------- | ----------- | ----- |
| `RestLib` | Core library | [RestLib](https://www.nuget.org/packages/RestLib/) |
| `RestLib.InMemory` | In-memory repository for testing and prototyping | [RestLib.InMemory](https://www.nuget.org/packages/RestLib.InMemory/) |

## Requirements

- .NET 8.0 or later
- ASP.NET Core Minimal APIs

## Contributing

Contributions are welcome. Read the [contributing guide](https://github.com/Adrian01987/RestLib/blob/main/CONTRIBUTING.md).

## License

This project is licensed under the MIT License. See the [license](https://github.com/Adrian01987/RestLib/blob/main/LICENSE).

## Acknowledgments

- [Zalando RESTful API Guidelines](https://opensource.zalando.com/restful-api-guidelines/)
- [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457)
- [FastEndpoints](https://fast-endpoints.com/)
