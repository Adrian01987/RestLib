# ADR-016: JSON Resource Configuration

**Status:** Accepted
**Date:** 2026-04-06

## Context

RestLib's fluent C# API (`MapRestLib<TEntity, TKey>`) is the primary way to configure endpoints. However, some deployment scenarios benefit from declarative configuration:

- **Multiple resources with similar settings** — repeating the same fluent calls is verbose and error-prone.
- **Environment-specific configuration** — enabling batch operations in development but not production.
- **Non-developer configuration** — operations teams adjusting rate limits or auth policies without modifying C# code.
- **Centralized resource inventory** — a single file listing all resources, their routes, and their capabilities.

RestLib needs a JSON-based configuration path that covers the full feature surface without replacing the fluent API.

## Options Considered

### Configuration Source

| Option | Pros | Cons |
| --- | --- | --- |
| **A. `IConfigurationSection` binding (appsettings.json)** | Standard .NET pattern; environment overrides via env vars; reloadable | Requires understanding .NET config hierarchy |
| B. Standalone JSON file with custom loader | Full control over schema | Non-standard; no env var overrides; another config system to learn |
| C. YAML / TOML | Popular in cloud-native tooling | Not natively supported in .NET; external dependency |

### Registration Model

| Option | Pros | Cons |
| --- | --- | --- |
| **A. Two-phase: register at DI time, map at endpoint time** | Hooks can resolve services from built `ServiceProvider`; clean separation | Slightly more ceremony (two calls) |
| B. Single-phase at endpoint time | Simpler API | Cannot resolve scoped/singleton services for hooks at map time |

## Decision

### 1. `IConfigurationSection` binding (Option A)

JSON resource configuration binds to `RestLibJsonResourceConfiguration` objects via standard .NET configuration binding. Resources can be defined inline in C# or loaded from `appsettings.json`:

```json
{
    "RestLib": {
        "Resources": [
            {
                "Name": "Products",
                "Route": "/api/products",
                "AllowAnonymousAll": true,
                "Filtering": ["CategoryId", "IsActive"],
                "FilteringOperators": {
                    "Price": ["comparison"],
                    "Name": ["contains", "starts_with"]
                },
                "Sorting": ["Name", "Price", "CreatedAt"],
                "DefaultSort": "name:asc",
                "FieldSelection": ["Id", "Name", "Price", "CategoryId"],
                "Batch": { "Actions": ["Create", "Update", "Delete"] },
                "RateLimiting": {
                    "Default": "standard",
                    "ByOperation": { "Create": "strict" },
                    "Disabled": ["GetById"]
                }
            }
        ]
    }
}
```

### 2. Full feature coverage

The JSON schema covers all configurable aspects:

| Feature | JSON Property | Type |
| --- | --- | --- |
| Route | `Route` | `string` |
| Key property | `KeyProperty` | `string?` |
| Operations | `Operations.Include` / `Operations.Exclude` | `RestLibOperation[]` |
| Auth | `AllowAnonymous`, `AllowAnonymousAll`, `Policies` | Various |
| Filtering | `Filtering`, `FilteringOperators` | `string[]`, `Dictionary` |
| Sorting | `Sorting`, `DefaultSort` | `string[]`, `string?` |
| Field selection | `FieldSelection` | `string[]` |
| Batch | `Batch.Actions` | `BatchAction[]` |
| Rate limiting | `RateLimiting.Default`, `.ByOperation`, `.Disabled` | Various |
| OpenAPI | `OpenApi.Tag`, `.Summaries`, `.Descriptions`, `.Deprecated` | Various |
| Hooks | `Hooks.OnRequestReceived`, `.BeforePersist`, etc. | Named hook references |

### 3. Two-phase registration (Option A)

Resources are registered in two steps:

```csharp
// Phase 1: DI registration (ConfigureServices)
builder.Services.AddRestLib(options =>
{
    options.AddJsonResource<Product, Guid>(config.GetSection("RestLib:Resources:0"));
});

// Phase 2: Endpoint mapping (Configure)
app.MapRestLibJsonResources();
```

Phase 1 stores a deferred mapping action in `RestLibJsonResourceRegistry`, keyed by resource name. Phase 2 executes the deferred actions against the built `IServiceProvider`, enabling hook handlers to be resolved from DI.

### 4. `RestLibJsonResourceBuilder` translates JSON to fluent API

The builder reads `RestLibJsonResourceConfiguration` and calls the corresponding fluent methods (`AllowFiltering`, `AllowSorting`, `UseRateLimiting`, etc.). This means JSON configuration and fluent configuration produce identical endpoint behavior — there is no separate code path.

### 5. JSON Schema for IDE validation

A JSON Schema at `schemas/restlib-resource.schema.json` provides autocomplete and validation in editors that support `$schema` references. The schema documents all properties, their types, enums, and defaults.

### 6. String-based property references

Since JSON configuration cannot use C# expressions, property names are specified as strings (e.g., `"Price"` instead of `p => p.Price`). The builder resolves these against the entity type at registration time and throws if a property name does not match.

## Rationale

1. **`IConfigurationSection` is the .NET standard.** It supports `appsettings.json`, `appsettings.{Environment}.json`, environment variables, and user secrets — all without custom code.
2. **Two-phase registration enables DI-resolved hooks.** Named hooks (e.g., `"AuditLogger"`) reference service types registered in DI. Resolving them requires a built `ServiceProvider`, which is only available after `Build()`.
3. **Builder-to-fluent translation avoids dual maintenance.** New features added to the fluent API only need a corresponding JSON property and builder mapping, not a parallel implementation.
4. **JSON Schema improves developer experience.** Autocomplete and validation in VS Code / Rider / Visual Studio catch configuration errors before runtime.

## Consequences

- JSON-configured resources use string-based property names, which are not refactoring-safe. Renaming an entity property requires updating the JSON configuration.
- The two-phase model requires two explicit calls (`AddJsonResource` + `MapRestLibJsonResources`), which is slightly more ceremony than a single `MapRestLib` call.
- Hook references in JSON are resolved by name from DI. If a named hook is not registered, the error surfaces at endpoint mapping time (application startup), not at compile time.
- The JSON Schema must be updated whenever new configuration properties are added.
