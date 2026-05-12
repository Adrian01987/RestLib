# EF Core Migrations With RestLib

RestLib can expose CRUD endpoints over an EF Core model, but it does not create,
own, or apply database schema migrations for you. Schema management stays with your
application and your normal EF Core workflow.

## Overview

Use RestLib for endpoint generation and repository wiring, and use normal EF Core
tooling for schema evolution:

- create migrations with `dotnet ef migrations add`
- apply migrations with `dotnet ef database update` or `Database.Migrate()`
- keep demo or ephemeral databases on `EnsureCreated()` only when you explicitly
  want a throwaway setup

## Why RestLib Does Not Own Migrations

Migration ownership is application-specific:

- teams choose different providers, naming conventions, and deployment workflows
- production environments often require reviewed SQL, controlled rollout, and
  startup coordination across multiple instances
- RestLib works with your EF Core model, but it should not decide when or how your
  schema changes are applied

## Packages

Typical package set:

```bash
dotnet add package RestLib
dotnet add package RestLib.EntityFrameworkCore
dotnet add package Microsoft.EntityFrameworkCore.Design
dotnet add package Microsoft.EntityFrameworkCore.Sqlite
```

If you do not already have the EF CLI tool:

```bash
dotnet tool install --global dotnet-ef
```

## Example DbContext

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

## Register RestLib And EF Core

```csharp
using Microsoft.EntityFrameworkCore;
using RestLib;
using RestLib.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRestLib();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("AppDb")
        ?? "Data Source=app.db"));
builder.Services.AddRestLibEfCore<AppDbContext, Product, Guid>();
```

RestLib consumes the EF Core model at runtime. Migrations remain the normal EF Core
concern and do not require any special RestLib API.

## Design-Time DbContext Factory

`dotnet ef` often needs a design-time factory so it can create your `DbContext`
outside the running app:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("AppDb")
            ?? "Data Source=app.db";

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseSqlite(connectionString);

        return new AppDbContext(optionsBuilder.Options);
    }
}
```

This factory exists for EF Core tooling. It is independent of RestLib endpoint
registration.

## Create And Apply Migrations

Create an initial migration:

```bash
dotnet ef migrations add InitialCreate
```

Apply it from the CLI:

```bash
dotnet ef database update
```

Create later schema updates the same way:

```bash
dotnet ef migrations add AddProductSku
dotnet ef database update
```

## Apply Migrations At Startup

If your deployment model allows startup migrations, do it in your app code rather
than in RestLib:

```csharp
var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}
```

Use this carefully in production. In multi-instance deployments, coordinate startup
so multiple app instances do not race to apply the same migration set.

## Demo And Local Testing With EnsureCreated

`EnsureCreated()` is still reasonable for a throwaway local demo or ephemeral test
database when you do not care about migration history:

```csharp
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}
```

Do not mix `EnsureCreated()` with real migrations on the same database.

## JSON Resources And Migrations

If you configure resources through JSON files or `AddRestLibFromFolder(...)`, the
migration story is unchanged:

- EF Core still owns the database model and migrations
- RestLib still maps endpoints from your configuration
- startup migration code still lives in your app

## Troubleshooting

- `dotnet ef` cannot create the context:
  add or fix `IDesignTimeDbContextFactory<TContext>`
- migrations build but runtime startup fails:
  confirm the app and the design-time factory use the same provider and connection string shape
- `EnsureCreated()` was used earlier and migrations now fail:
  recreate the demo database or move to a fresh database that starts from migrations
- multiple instances apply migrations at once:
  coordinate deployment or run migrations as a separate release step
