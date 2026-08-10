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

## Global Service Registration

`AddRestLib` is idempotent and uses a first-successful-call-wins contract. The first call
registers one coherent `RestLibOptions`, RestLib serializer, Minimal API JSON configuration,
default ETag service (when enabled), and OpenAPI infrastructure set. Later calls return the
same service collection without invoking their configuration delegates.

Applications with modular startup should therefore place all global settings in the first
call. A later module may safely call `AddRestLib()` defensively, but it cannot extend or
override the established configuration.

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

The response reports per-item status. RestLib returns 200 only when every item
succeeds and 207 Multi-Status whenever one or more items fail, including when
all items fail.

Once the request envelope and its non-empty `items` array are accepted, the
response contains one entry per array member in the same order. The entry's
`index` is its original zero-based request position. Each member is decoded
independently for the selected action: a malformed member receives an indexed
400 `/problems/bad-request` result and valid siblings continue. It never reaches
validation, hooks, or the repository. Syntactically invalid JSON, a missing,
null, non-array, or empty `items` value, an invalid or disabled action, and an
oversized batch instead produce one top-level 400 Problem Details response.

Update and patch repositories may omit resources that are missing at persistence
time; RestLib correlates returned entities by resource key, so the missing input
receives a 404 in its own slot without shifting the entities returned for later
inputs. Batch processing is non-transactional, so valid siblings may already be
committed. Retry only failed result slots rather than replaying the whole batch.

Custom `IBatchRepository<TEntity, TKey>` implementations must honor the public
cardinality, ordering, key, and duplicate-key rules. Create returns exactly one
non-null entity per input in input order; this is essential when keys are
generated during the call because RestLib cannot reconstruct a different order
from pre-persistence keys. RestLib does compare caller-supplied non-default
create keys with the returned entity at each response position. Update and patch
return non-null matching entities in relative input order while omitting missing
keys. RestLib validates observable bulk-result invariants before after-persist
hooks run. If a result cannot be associated safely, affected unresolved items
enter per-item error handling and default to internal errors (configured error
hooks may replace that response). RestLib does not retry the write because the
repository may already have committed it.

On bulk update and patch, RestLib decodes and structurally checks members before
repository access, then calls `GetByIdsAsync` once when at least one key
survives. It does not issue a `GetByIdAsync` call per item. Existence checks, validation,
merge previews, and pre-persistence hooks use that returned pre-write snapshot;
only surviving items reach `UpdateManyAsync` or `PatchManyAsync`. Repeated keys
share the same original snapshot even though the mutation method applies them in
input order and returns the final value for each successful occurrence.

This is a RestLib-to-repository call-count guarantee, not a physical query-count
or concurrency guarantee. A repository may perform internal reads while
implementing either batch method, and no transaction is implied across the
separate lookup and mutation calls. The individual fallback continues to use
one point lookup per update or patch item.

Batch size is limited to 100 items by default (configurable via
`RestLibOptions.MaxBatchSize`). Hooks and validation run independently for every
member that was decoded successfully, with errors reported in the member's
original result slot.

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

Generated pagination and standard HATEOAS URLs include `Request.PathBase`; create
`Location` headers include the same prefix while remaining root-relative. Behind a reverse
proxy, run ASP.NET Core forwarded-header middleware before routing and configure only
trusted proxies/networks plus `AllowedHosts`. RestLib uses the resulting `Scheme`, `Host`,
and `PathBase` and never interprets raw forwarding headers itself. Custom link-provider
URLs are left exactly as supplied by the application.

For collection links, the API model must expose a conventional `Id` property of
the resource key type or configure `config.KeySelector` (JSON resources use their
configured `KeyProperty` or composite `Key`). RestLib validates this dependency
when endpoints are mapped. If a selector nevertheless returns `null` for an
individual item at runtime, the item is retained in its original position without
`_links`; collection counts and pagination metadata remain unchanged.

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

When a custom repository can determine before persistence that a PATCH document contains
an invalid or forbidden field, throw `PatchValidationException` with a client-safe message.
RestLib maps that typed boundary to 400 for direct PATCH and for individually processed
batch PATCH items. Other repository, mapper, hook, and infrastructure exceptions are not
client-validation failures and are not reclassified by exception name or by broad CLR
exception type. A failed bulk operation remains a server failure for every unresolved item
because neither its per-item cause nor its persistence outcome can be inferred safely.
The built-in InMemory and EF Core adapters use this same boundary for their strict
immutable-key or unknown-field validation.

When ETag support is enabled, conditional GETs work with the base repository contract. To accept
`If-Match` on PUT, PATCH, or DELETE, a custom repository must also implement
`IConditionalWriteRepository<TEntity, TKey>` and evaluate its supplied predicate atomically with
the mutation. RestLib returns 501 Conditional Write Not Supported for an `If-Match` write when
that optional capability is absent; it never falls back to a race-prone read-then-write sequence.

### Injecting official adapter capabilities

The official adapter registration extensions expose every repository capability
implemented by their concrete repository. Each interface resolves to the same
adapter instance rather than creating a second store, `DbContext`, or unit of
work.

| Repository service | InMemory | EF Core |
| --- | --- | --- |
| `IRepository<TEntity, TKey>` | Singleton | Scoped |
| `IBatchRepository<TEntity, TKey>` | Same singleton | Same scope |
| `IConditionalWriteRepository<TEntity, TKey>` | Same singleton | Same scope |
| `ICountableRepository<TEntity, TKey>` | Same singleton | Same scope |
| `IQueryCountableRepository<TEntity, TKey>` | Same singleton | Same scope |
| `IFieldSelectionProjectionRepository<TEntity, TKey>` | Not implemented | Same scope |

This makes an implemented capability directly injectable by application
services; endpoint feature detection and DI resolution describe the same
adapter surface. Projection remains resolvable for EF Core when pushdown is
disabled because the capability can decline an individual request and return
`null` for the normal materialized fallback.

`AddRepository` registers the base custom-repository contract only. When a
custom repository implements optional capabilities that application services
will inject directly, register those interfaces explicitly with the same
implementation and lifetime.

### InMemory concurrency and entity ownership

The InMemory adapter supports concurrent calls to its repository methods, but
that guarantee applies to the repository's store operations rather than to the
internals of your entity objects:

- repository-owned mutations are serialized;
- point reads, counts, bulk reads, and collection membership snapshots are
  coordinated with those mutations, so they do not observe a partial batch
  storage commit;
- filtering, search, sorting, and pagination run over a shallow snapshot after
  the store lock is released;
- stored and returned entities are the same caller-owned references. RestLib
  does not clone them, freeze them, or synchronize direct property mutations;
- entity keys must remain stable after insertion. Mutating a stored entity's key
  directly does not re-key the dictionary;
- entity getters and configured key selector, generator, assigner, comparer, and
  precondition delegates must be safe for the way the application uses them;
  preconditions must not mutate the entity they inspect;
- mutation callbacks run inside the repository's store critical section. They
  must not re-enter the same repository or synchronously wait for another
  thread to call it. Re-entrant writes can invalidate the outer operation's
  staged assumptions, while a second thread must wait for the callback to
  release the store.

Consequently, a repository query has stable membership but not a deep snapshot
of mutable entity state. Applications sharing mutable entity instances across
threads must provide their own synchronization or use immutable entities.

Cancellation is cooperative. Operations reject an already-cancelled token;
collection reads and batch planning check it while iterating. Mutating batches
check once more immediately before their atomic storage commit and then finish
that commit without interruption, preventing cancellation from leaving a
partially persisted batch. `Clear` and `Seed` are synchronous setup helpers and
do not accept cancellation tokens. Cancellation cannot undo side effects that
application callbacks have already performed outside repository storage.

These guarantees are local to one repository instance. The adapter does not
provide cross-resource transactions or application-level object isolation; see
[ADR-033](../adr/033-inmemory-concurrency-contract.md).

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

When `KeySelector` is set, RestLib uses that direct mapped property expression as the
resource identity for generated endpoints. Use this only for a stable unique key, such as
an alternate public identifier. Arbitrary expressions and unmapped properties fail when the
repository is resolved.

That resource identity is immutable. For PUT and batch update, the item route or batch
envelope key wins when the request body contains a different value. For JSON Merge Patch,
single and batch requests that include the configured key field are rejected with `400`
item semantics before persistence. This applies equally when the public `KeySelector`
points to an EF alternate key while the database keeps a different primary key.

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
