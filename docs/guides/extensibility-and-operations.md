# Extensibility And Operations

This guide covers the deeper RestLib customization surface: batch operations,
selective operation exposure, hooks, persistence adapters, EF Core specifics,
and versioning patterns.

## See also

- [README](../../README.md)
- [JSON resources guide](json-resources.md)
- [EF Core migrations guide](ef-core-migrations.md)
- [ADR-008: Batch operations with partial success](../adr/008-batch-operations.md)
- [ADR-010: API versioning via route groups](../adr/010-versioning.md)
- [ADR-012: Hook pipeline for extensibility](../adr/012-hook-pipeline.md)
- [ADR-019: HATEOAS hypermedia links](../adr/019-hateoas.md)
- [ADR-021: EF Core repository adapter](../adr/021-ef-core-adapter.md)

## Batch Operations

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

## HATEOAS Hypermedia Links

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

## Select Operations

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

You can also move this declarative resource configuration out of `Program.cs` and into JSON while keeping your model, repository, and hooks strongly typed.

Recommended path: folder-based loading with one file per resource:

```json
{
  "$schema": "https://raw.githubusercontent.com/Adrian01987/RestLib/main/schemas/restlib-resource.schema.json",
  "EntityType": "Product, MyApi",
  "Name": "products",
  "Route": "/api/products",
  "AllowAnonymousAll": true,
  "Filtering": ["CategoryId", "IsActive"],
  "Sorting": ["Price", "Name", "CreatedAt"],
  "DefaultSort": "name:asc",
  "Validation": {
    "Name": {
      "Required": true,
      "Length": { "Max": 200 }
    },
    "Price": {
      "Min": 0.01
    }
  }
}
```

```csharp
builder.Services.AddNamedHook<Product, Guid>(HookNames.SetUpdatedAt, ctx =>
{
    if (ctx.Entity is Product product)
    {
        product.UpdatedAt = ctx.Operation == RestLibOperation.Create ? null : DateTime.UtcNow;
    }

    return Task.CompletedTask;
});

builder.Services.AddRestLibFromFolder("Models");

var app = builder.Build();
app.MapJsonResources();
```

Two-model JSON resources use the same folder loading path. Keep `EntityType` as the API model and add a `Mapping` section for the DB model and mapper:

```json
{
  "EntityType": "CustomerDto, MyApi",
  "Name": "customers",
  "Route": "/api/customers",
  "Mapping": {
    "DbType": "CustomerEntity, MyApi",
    "Mapper": "CustomerMapper",
    "HookModel": "Db"
  },
  "Filtering": ["City", "IsActive"],
  "Sorting": ["Name", "City", "Email"],
  "FieldSelection": ["Id", "Name", "Email", "City", "IsActive"]
}
```

If you prefer to resolve both API and DB types in code, configure `UnifiedTypeResolver`.
It takes precedence over the legacy `TypeResolver` and over `Mapping.DbType` lookup.
Return `DbType = null` for a single-model resource:

```csharp
builder.Services.AddRestLibFromFolder("Models", options =>
{
    options.UnifiedTypeResolver = (file, config) => file.EndsWith("Customers.json", StringComparison.Ordinal)
        ? new RestLibResolvedResourceTypes
        {
            ApiType = typeof(CustomerDto),
            DbType = typeof(Customer),
            KeyType = typeof(Guid),
        }
        : null;
});
```

Resolver precedence is `UnifiedTypeResolver` > `TypeResolver` > `EntityType` > file-name match in `Assemblies`, with `Mapping.DbType` only used when the unified resolver does not provide the DB model.

Backward-compatible alternative: `appsettings.json` with `IConfigurationSection` binding:

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

The same registration surface supports two-model resources:

```csharp
builder.Services.AddJsonResource<CustomerDto, CustomerEntity, Guid>(
    builder.Configuration.GetSection("RestLib:Resources:Customers"));
```

Both paths use the same `RestLibJsonResourceConfiguration` model and the same JSON-to-fluent translation pipeline.

## Extensible via Hooks

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

## Persistence-Agnostic

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

## EF Core Adapter

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
for filtering, sorting, and counting. Field selection can also be pushed down to SQL
when projection pushdown is enabled and the request only uses projectable direct scalar
properties. Nested filtering and sorting also translate server-side. Nested field
selection uses a conservative fallback that loads the needed reference navigations and
applies sparse projection after materialization. Some capabilities have important
implementation limits;
see [Current EF Core Adapter Limitations](../../README.md#current-ef-core-adapter-limitations)
and [ADR-021](../adr/021-ef-core-adapter.md).

RestLib uses your EF Core model but does not create or manage migrations. Keep schema
ownership in your application and use the normal EF Core tooling and startup migration
patterns described in [ef-core-migrations.md](ef-core-migrations.md).

When your public API model intentionally hides a persistence-only column, enforce that
invariant at the DbContext boundary rather than in a mapper that only sees the API model.
The sample app uses this pattern for `Customer.CreatedAt`: `CustomerDto` does not expose
the property, `SampleDbContext.SaveChanges*` fills it on inserts, and updates mark the
column as not modified so PUT and PATCH do not reset it accidentally.

```csharp
public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
{
    PreserveCustomerCreatedAt();
    return base.SaveChangesAsync(cancellationToken);
}

private void PreserveCustomerCreatedAt()
{
    foreach (var entry in ChangeTracker.Entries<Customer>())
    {
        if (entry.State == EntityState.Added && entry.Entity.CreatedAt == default)
        {
            entry.Entity.CreatedAt = DateTime.UtcNow;
        }

        if (entry.State == EntityState.Modified)
        {
            entry.Property(customer => customer.CreatedAt).IsModified = false;
        }
    }
}
```

Use the same approach for audit stamps and other persistence-owned fields when the API
surface should not let clients set them directly.

## Versioning

RestLib integrates with any ASP.NET Core versioning strategy via route groups.

### URL prefix versioning

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

### Prefix-less overload on a route group

When the route group already has the full path configured, use the prefix-less overload:

```csharp
app.MapGroup("/api/v1/products").MapRestLib<Product, Guid>(cfg =>
{
    cfg.AllowAnonymous();
});
```

### With Asp.Versioning.Http

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
