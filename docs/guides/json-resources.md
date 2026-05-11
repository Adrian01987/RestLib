# JSON Resources From A Models Folder

This guide shows the recommended JSON-driven setup for RestLib: one resource file per entity under a `Models/` folder, loaded with `AddRestLibFromFolder("Models")`.

If those JSON resources sit on top of an EF Core-backed application, see
[ef-core-migrations.md](ef-core-migrations.md) for the production database migration workflow.

## 1. Create the app

```bash
dotnet new web -n MyApi
cd MyApi
dotnet add package RestLib
dotnet add package RestLib.InMemory
dotnet add package Microsoft.AspNetCore.OpenApi
dotnet add package Scalar.AspNetCore
```

## 2. Add model classes

Create `Models/Category.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

public class Category
{
    public Guid Id { get; set; }

    [Required]
    [StringLength(100)]
    public required string Name { get; set; }
}
```

Create `Models/Product.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

public class Product
{
    public Guid Id { get; set; }

    [Required]
    [StringLength(200)]
    public required string Name { get; set; }

    [Range(0.01, (double)decimal.MaxValue)]
    public decimal Price { get; set; }

    public Guid CategoryId { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
```

## 3. Add repositories and services

`Program.cs`:

```csharp
using RestLib;
using RestLib.InMemory;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRestLib();
builder.Services.AddRestLibInMemory<Category, Guid>(c => c.Id, Guid.NewGuid);
builder.Services.AddRestLibInMemory<Product, Guid>(p => p.Id, Guid.NewGuid);
builder.Services.AddOpenApi();
```

## 4. Add `Models/` JSON resource files

Create `Models/Categories.json`:

```json
{
  "$schema": "https://raw.githubusercontent.com/Adrian01987/RestLib/main/schemas/restlib-resource.schema.json",
  "EntityType": "Category, MyApi",
  "Name": "categories",
  "Route": "/api/categories",
  "AllowAnonymousAll": true,
  "Operations": {
    "Include": ["GetAll", "GetById"]
  },
  "OpenApi": {
    "Tag": "Category",
    "Summaries": {
      "GetAll": "List categories",
      "GetById": "Get category by id"
    }
  }
}
```

Create `Models/Products.json`:

```json
{
  "$schema": "https://raw.githubusercontent.com/Adrian01987/RestLib/main/schemas/restlib-resource.schema.json",
  "EntityType": "Product, MyApi",
  "Name": "products",
  "Route": "/api/products",
  "AllowAnonymousAll": true,
  "Filtering": ["CategoryId"],
  "Sorting": ["Name", "Price"],
  "DefaultSort": "name:asc",
  "FieldSelection": ["Id", "Name", "Price", "CategoryId"],
  "Search": ["Name"],
  "Validation": {
    "Name": {
      "Required": true,
      "Length": { "Max": 200 }
    },
    "Price": {
      "Min": 0.01
    }
  },
  "OpenApi": {
    "Tag": "Product",
    "Summaries": {
      "GetAll": "List products"
    }
  }
}
```

`EntityType` is optional when you configure `RestLibFolderOptions.TypeResolver` or when the file name matches a public CLR type in a registered assembly. Keeping it in the file is the most explicit path and avoids pluralization guesswork.

### Nested query paths

`Filtering`, `FilteringOperators`, `Sorting`, and `FieldSelection` can use direct CLR
property names or dot-separated nested reference-property paths.

`Search` follows the same path rules, but only for string properties and only on
collection endpoints.

- `Customer.Email` becomes query name `customer.email`
- `Customer.Name` becomes query name `customer.name`
- collection-valued paths such as `Items.Name` are not supported and fail at startup

Example:

```json
{
  "Name": "orders",
  "Route": "/api/orders",
  "FilteringOperators": {
    "Customer.Email": ["contains"]
  },
  "Sorting": ["Customer.Name", "OrderNumber"],
  "DefaultSort": "customer.name:asc",
  "FieldSelection": ["OrderNumber", "Customer.Email"],
  "Search": ["OrderNumber", "Customer.Email"]
}
```

Requests then look like:

```text
GET /api/orders?customer.email[contains]=example.com
GET /api/orders?sort=customer.name:asc
GET /api/orders?fields=order_number,customer.email
GET /api/orders?q=adam@example.com
```

Nested sparse field selection returns dotted keys in the response instead of rebuilt
nested objects:

```json
{
  "order_number": "A-100",
  "customer.email": "adam@example.com"
}
```

### Collection search

JSON resources can opt into simple OR-of-contains search across configured string
properties:

```json
{
  "Name": "products",
  "Route": "/api/products",
  "Search": ["Name", "Description"],
  "SearchOptions": {
    "QueryParameter": "query",
    "CaseSensitive": false
  }
}
```

Requests then look like:

```text
GET /api/products?q=widget
GET /api/products?query=widget
```

Search is intentionally limited to OR-of-contains matching across configured string
fields. It is not full-text indexing, ranking, fuzzy matching, or a general search engine.

## 5. Register named hooks when behavior belongs in C#

JSON declares which hook to use; C# still owns the implementation.

```csharp
public static class HookNames
{
    public const string SetUpdatedAt = nameof(SetUpdatedAt);
}

builder.Services.AddNamedHook<Product, Guid>(HookNames.SetUpdatedAt, ctx =>
{
    if (ctx.Entity is Product product)
    {
        product.UpdatedAt = ctx.Operation == RestLibOperation.Create ? null : DateTime.UtcNow;
    }

    return Task.CompletedTask;
});
```

Then reference it from `Products.json`:

```json
"Hooks": {
  "BeforePersist": {
    "ByOperation": {
      "Create": ["SetUpdatedAt"],
      "Update": ["SetUpdatedAt"],
      "Patch": ["SetUpdatedAt"]
    }
  }
}
```

## 6. Load the folder and map the resources

Finish `Program.cs`:

```csharp
builder.Services.AddRestLibFromFolder("Models");

var app = builder.Build();

app.MapOpenApi();
app.MapScalarApiReference();
app.MapJsonResources();

app.Run();
```

That is the recommended JSON-driven startup path.

## 7. Two-model JSON resources

When you want to expose an API DTO but persist a different DB model, keep `EntityType` as the API model and declare the DB model under `Mapping`.

Create `Models/CustomerDto.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

public class CustomerDto
{
    public Guid Id { get; set; }

    [Required]
    [StringLength(100)]
    public required string Name { get; set; }

    [Required]
    [EmailAddress]
    [StringLength(200)]
    public required string Email { get; set; }

    [StringLength(100)]
    public string? City { get; set; }

    public bool IsActive { get; set; }
}
```

Create `Models/CustomerEntity.cs`:

```csharp
public class CustomerEntity
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Email { get; set; }
    public string? City { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

Register the repository and mapper in `Program.cs`:

```csharp
builder.Services.AddRestLibInMemory<CustomerEntity, Guid>(c => c.Id, Guid.NewGuid);
builder.Services.AddRestLibMapper<CustomerDto, CustomerEntity, CustomerMapper>();
```

Then declare `Models/Customers.json`:

```json
{
  "$schema": "https://raw.githubusercontent.com/Adrian01987/RestLib/main/schemas/restlib-resource.schema.json",
  "EntityType": "CustomerDto, MyApi",
  "Name": "customers",
  "Route": "/api/customers",
  "AllowAnonymousAll": true,
  "Mapping": {
    "DbType": "CustomerEntity, MyApi",
    "Mapper": "CustomerMapper"
  },
  "Filtering": ["City", "IsActive"],
  "Sorting": ["Name", "City", "Email"],
  "FieldSelection": ["Id", "Name", "Email", "City", "IsActive"]
}
```

JSON filtering, sorting, and field selection still use API-model CLR property names in the configuration file. Query names are derived automatically using snake_case per segment, including dotted nested paths.

### Auto mapping shortcut

For trivial same-name, same-type models, JSON can opt into the built-in reflection mapper:

```json
"Mapping": {
  "DbType": "CustomerEntity, MyApi",
  "Auto": true
}
```

`Auto` is intentionally strict. It only copies public instance properties by exact same CLR name and exact same CLR type in both directions. If you need renamed fields, type conversions, nested mapping, computed values, or preservation logic, use a C# mapper instead.

### Hook model selection

Named JSON hooks run on the API model by default. To run them against the DB model instead:

```json
"Mapping": {
  "DbType": "CustomerEntity, MyApi",
  "Mapper": "CustomerMapper",
  "HookModel": "Db"
}
```

Use that when hooks need access to persistence-only properties.

## 8. Composite-key JSON resources

Two-part composite keys use `RestLibCompositeKey<TFirst, TSecond>` in C# and a `Key` object in JSON.

Create `Models/TenantProduct.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

public class TenantProduct
{
    public Guid TenantId { get; set; }

    [Required]
    [StringLength(64)]
    public required string Sku { get; set; }

    [Required]
    [StringLength(200)]
    public required string ProductName { get; set; }

    [Range(0.01, (double)decimal.MaxValue)]
    public decimal Price { get; set; }
}
```

Register the repository in `Program.cs`:

```csharp
builder.Services.AddRestLibInMemory<TenantProduct, RestLibCompositeKey<Guid, string>>(
    p => new RestLibCompositeKey<Guid, string>(p.TenantId, p.Sku),
    () => new RestLibCompositeKey<Guid, string>(Guid.NewGuid(), $"generated-{Guid.NewGuid():N}"));
```

Then declare `Models/TenantProducts.json`:

```json
{
  "$schema": "https://raw.githubusercontent.com/Adrian01987/RestLib/main/schemas/restlib-resource.schema.json",
  "EntityType": "TenantProduct, MyApi",
  "Name": "tenant-products",
  "Route": "/api/tenant-products",
  "AllowAnonymousAll": true,
  "Key": {
    "Properties": ["TenantId", "Sku"],
    "RouteParameters": ["tenantId", "sku"]
  },
  "Sorting": ["ProductName", "Price"]
}
```

That produces item routes like `/api/tenant-products/{tenantId}/{sku}`.

Use `KeyProperty` for single-key resources and `Key` for two-part composite-key resources. Do not configure both on the same resource.

## 9. Run the app

```bash
dotnet build
dotnet run
```

You now have:

- `GET /api/categories`
- `GET /api/categories/{id}`
- full CRUD on `/api/products`
- filtering, sorting, field selection, and JSON validation on products

## Troubleshooting

### Type resolution failed

If `AddRestLibFromFolder("Models")` fails to resolve a CLR type:

- add `EntityType` to the JSON file, or
- configure `RestLibFolderOptions.TypeResolver`, or
- add assemblies explicitly through `options.Assemblies`

Example:

```csharp
builder.Services.AddRestLibFromFolder("Models", options =>
{
    options.Assemblies.Add(typeof(Product).Assembly);
});
```

### Invalid validation rule

JSON validation rules are checked at startup. If a rule targets the wrong CLR property type or contains an invalid regex, startup fails before the app begins serving requests.

### Relative paths

Folder and file paths are resolved relative to the current content root first, then the app base directory. For published apps, copy `Models/*.json` into the output alongside the application.

## Backward-compatible alternative

Existing appsettings-based registration is still supported:

```csharp
builder.Services.AddJsonResource<Product, Guid>(
    builder.Configuration.GetSection("RestLib:Resources:Products"));
```

Two-model appsettings registration is also supported:

```csharp
builder.Services.AddJsonResource<CustomerDto, CustomerEntity, Guid>(
    builder.Configuration.GetSection("RestLib:Resources:Customers"));
```

Use that when you prefer standard ASP.NET Core configuration providers. Use the `Models/` folder convention when you want one file per resource.
