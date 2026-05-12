# ADR-022: Per-file JSON resources

**Status:** Accepted
**Date:** 2026-05-06

## Context

`appsettings.json` works for a handful of resources, but it becomes noisy once an API exposes several entities. Epic A requires a modular path where each resource can live in its own JSON file without replacing the existing appsettings-based registration story.

## Decision

RestLib now supports root-level JSON resource files loaded explicitly via `AddJsonResourceFromFile<TEntity, TKey>(string path)`.

Example:

```json
{
  "$schema": "../../schemas/restlib-resource.schema.json",
  "Name": "products",
  "Route": "/api/products",
  "AllowAnonymousAll": true,
  "Filtering": ["CategoryId", "IsActive"]
}
```

```csharp
builder.Services.AddJsonResourceFromFile<Product, Guid>("Models/Products.json");
```

The file contains one `RestLibJsonResourceConfiguration` object at the root. It does not use `RestLib:Resources:*` nesting.

Startup-time failures are explicit:

- Missing files throw with the requested path and resolved path.
- Malformed JSON throws with file path plus parser location details.
- Files that bind successfully but omit required `Name` or `Route` still fail startup.

This feature does not add hot reload or file watching. Folder loading remains a separate feature.

Existing `AddJsonResource<TEntity, TKey>(IConfigurationSection)` registration remains supported unchanged for backward compatibility.

## Consequences

- Teams can split resource configuration into one file per entity without giving up the existing JSON-to-fluent translation path.
- Standalone files can reference `schemas/restlib-resource.schema.json` through `$schema` for editor autocomplete.
- Path resolution is explicit and happens at startup; configuration errors fail fast instead of surfacing on first request.
- Per-file registration still requires one call per resource until folder loading is used.
