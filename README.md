# RestLib

> **3 lines to a production-ready REST API**

[![Build](https://github.com/Adrian01987/RestLib/actions/workflows/ci.yml/badge.svg)](https://github.com/Adrian01987/RestLib/actions/workflows/ci.yml)
[![Coverage](https://codecov.io/gh/Adrian01987/RestLib/branch/main/graph/badge.svg)](https://codecov.io/gh/Adrian01987/RestLib)
[![NuGet](https://img.shields.io/nuget/v/RestLib.svg)](https://www.nuget.org/packages/RestLib/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

```csharp
builder.Services.AddRestLib().AddRestLibInMemory<Product, Guid>();
app.MapRestLib<Product, Guid>("/api/products");
// → GET, POST, PUT, DELETE with auth, pagination, OpenAPI ✨
```

---

## Why I Built This

Every backend project starts the same way: create a model, write a controller, add validation, handle errors, set up pagination, configure Swagger... and repeat for every entity.

I've written this code dozens of times. So have you.

**RestLib eliminates this repetitive work** while enforcing the standards I care about:

- ✅ Proper REST semantics (Zalando guidelines)
- ✅ Secure by default (auth required unless you opt out)
- ✅ Machine-readable errors (RFC 9457 Problem Details)
- ✅ Clean extensibility (hooks, not inheritance)

### What This Project Demonstrates

| Skill                  | How It's Shown                                            |
| ---------------------- | --------------------------------------------------------- |
| **Modern .NET**        | .NET 8, Minimal APIs, generic constraints, async patterns |
| **API Design**         | Zalando guidelines, RFC 9457, OpenAPI 3.1                 |
| **Library Design**     | Clean abstractions, hook-based extensibility, no lock-in  |
| **Production Mindset** | Security defaults, comprehensive testing, CI/CD           |
| **Documentation**      | ADRs, clear README, code examples                         |

---

## Quick Start (5 minutes)

### 1. Create a new project

```bash
dotnet new web -n MyApi
cd MyApi
```

### 2. Install RestLib

```bash
dotnet add package RestLib
dotnet add package RestLib.InMemory
```

### 3. Define your model

```csharp
public class Product
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public decimal Price { get; set; }
}
```

### 4. Configure and run

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
    config.AllowAnonymous(); // For demo purposes
});

app.Run();
```

### 5. Explore your API

```bash
dotnet run
# Open http://localhost:5000/swagger
```

**That's it.** You have a full CRUD API with:

- `GET /api/products` — List all (paginated)
- `GET /api/products/{id}` — Get by ID
- `POST /api/products` — Create
- `PUT /api/products/{id}` — Update
- `DELETE /api/products/{id}` — Delete

---

## Features

### 🔒 Secure by Default

All endpoints require authorization unless explicitly opted out:

```csharp
app.MapRestLib<Product, Guid>("/api/products", config =>
{
    // Only these operations are public
    config.AllowAnonymous(RestLibOperation.GetAll, RestLibOperation.GetById);

    // Delete requires admin role
    config.RequirePolicy(RestLibOperation.Delete, "AdminOnly");
});
```

### 📄 Standards-Compliant

RestLib follows [Zalando REST API Guidelines](https://opensource.zalando.com/restful-api-guidelines/):

- **snake_case** JSON properties
- **Cursor-based** pagination
- **RFC 9457** Problem Details for errors
- **Proper** HTTP status codes

```json
// Error response
{
  "type": "/problems/not-found",
  "title": "Resource Not Found",
  "status": 404,
  "detail": "Product with ID '999' does not exist.",
  "instance": "/api/products/999"
}
```

### � Advanced Filtering

Enable filtering with zero boilerplate. It parses the query string, validates parameters, and passes safe values to your repository.

```csharp
app.MapRestLib<Product, Guid>("/api/products", config =>
{
    // Enable filtering for specific properties:
    // GET /api/products?category_id=5&is_active=true
    config.AllowFiltering(p => p.CategoryId, p => p.IsActive);
});
```

### ✂️ Select Operations

Need a read-only API? Or want to handle specific operations yourself? Enable only what you need:

```csharp
app.MapRestLib<Category, Guid>("/api/categories", config =>
{
    // Only expose GET endpoints (no create/update/delete)
    config.IncludeOperations(RestLibOperation.GetAll, RestLibOperation.GetById);
});

// Implement custom logic alongside RestLib
app.MapPost("/api/categories", async (Category category, IRepository<Category, Guid> repo) => { ... });
```

### �🔌 Extensible via Hooks

Inject custom logic without subclassing:

```csharp
config.UseHooks(hooks =>
{
    hooks.BeforePersist = async ctx =>
    {
        if (ctx.Operation == RestLibOperation.Create)
        {
            ctx.Entity!.CreatedAt = DateTime.UtcNow;
            ctx.Entity!.CreatedBy = ctx.HttpContext.User.Identity?.Name;
        }
    };

    hooks.AfterPersist = async ctx =>
    {
        await PublishEvent(new ProductCreated(ctx.Entity!.Id));
    };
});
```

### 🗄️ Persistence-Agnostic

Bring your own data store:

```csharp
public class ProductRepository : IRepository<Product, Guid>
{
    private readonly MyDbContext _db;

    public async Task<Product?> GetByIdAsync(Guid id, CancellationToken ct)
        => await _db.Products.FindAsync(id, ct);

    // ... other methods
}

builder.Services.AddRepository<Product, Guid, ProductRepository>();
```

---

## Performance

RestLib adds minimal overhead compared to hand-written Minimal APIs. In some cases, RestLib is actually **faster** due to optimized code paths:

| Operation | Raw API  | RestLib  | Overhead    | Memory |
| --------- | -------- | -------- | ----------- | ------ |
| GET by ID | 67.5 μs  | 69.5 μs  | +3%         | +2%    |
| GET all   | 173.3 μs | 116.5 μs | **-33%** ⚡ | +7%    |
| POST      | 97.3 μs  | 99.4 μs  | +2%         | +13%   |
| PUT       | 88.6 μs  | 114.2 μs | +29%        | +13%   |

_Benchmarks run on .NET 8.0, Windows 11, Intel Core i3-8130U, 100 items in repository._

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

<details>
<summary>Run benchmarks locally</summary>

```bash
cd benchmarks/RestLib.Benchmarks
dotnet run -c Release
```

For a quick validation run:

```bash
dotnet run -c Release -- --job Dry --filter "*"
```

</details>

---

## Architecture Decisions

Key decisions are documented as [Architecture Decision Records](docs/adr/):

| ADR                                          | Decision                            |
| -------------------------------------------- | ----------------------------------- |
| [ADR-001](docs/adr/001-cursor-pagination.md) | Cursor-based pagination over offset |
| [ADR-002](docs/adr/002-secure-by-default.md) | Authorization required by default   |
| [ADR-003](docs/adr/003-minimal-apis.md)      | Minimal APIs over controllers       |
| [ADR-004](docs/adr/004-snake-case-json.md)   | snake_case JSON naming              |
| [ADR-005](docs/adr/005-problem-details.md)   | RFC 9457 Problem Details for errors |

---

## Documentation

📝 _Documentation is a work in progress. Check back soon for detailed guides on configuration, hooks, and custom repositories._

---

## Packages

| Package            | Description                      | NuGet                                                                                                             |
| ------------------ | -------------------------------- | ----------------------------------------------------------------------------------------------------------------- |
| `RestLib`          | Core library                     | [![NuGet](https://img.shields.io/nuget/v/RestLib.svg)](https://www.nuget.org/packages/RestLib/)                   |
| `RestLib.InMemory` | In-memory repository for testing | [![NuGet](https://img.shields.io/nuget/v/RestLib.InMemory.svg)](https://www.nuget.org/packages/RestLib.InMemory/) |

---

## Requirements

- .NET 8.0 or later
- ASP.NET Core

---

## Contributing

Contributions are welcome! Please read our [Contributing Guidelines](CONTRIBUTING.md) for details on our code of conduct, and the process for submitting pull requests to us.

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

---

## License

This project is licensed under the MIT License — see the [LICENSE](LICENSE) file for details.

---

## Acknowledgments

- [Zalando RESTful API Guidelines](https://opensource.zalando.com/restful-api-guidelines/) — API design inspiration
- [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) — Problem Details specification
- [FastEndpoints](https://fast-endpoints.com/) — Alternative approach inspiration
