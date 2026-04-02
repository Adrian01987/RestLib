# ADR-012: Hook Pipeline for Extensibility

**Status:** Accepted
**Date:** 2026-03-30

## Context

REST endpoint libraries need extensibility points for cross-cutting concerns such as logging, auditing, authorization enrichment, data transformation, and custom validation. Without a structured extensibility mechanism, users must wrap or replace endpoint handlers entirely, which is fragile and loses the benefits of the library's built-in behavior.

Three design decisions arise:

1. **Where to intercept** — at the HTTP level, the repository level, or within the endpoint handler?
2. **How many stages** — a single before/after pair, or multiple fine-grained stages?
3. **How to register hooks** — inline delegates, DI-resolved services, or both?

## Options Considered

### Interception Strategy

| Option | Pros | Cons |
| --- | --- | --- |
| ASP.NET Core middleware | Familiar; works with any endpoint | Too coarse — operates at HTTP level; no access to typed entities or operation context |
| Decorator pattern on `IRepository` | Intercepts all persistence calls | Only covers persistence; cannot intercept request validation, response shaping, or error handling |
| MediatR-style pipeline | Well-known pattern; flexible | Adds an external dependency; heavier than needed for a focused library |
| Stage-based hook pipeline within endpoint handlers | Fine-grained; per-operation; access to typed entities and HTTP context; no external dependencies | Custom abstraction to learn; hooks are RestLib-specific |

### Stage Granularity

| Option | Pros | Cons |
| --- | --- | --- |
| Single before/after pair | Simple | Cannot distinguish between pre-validation, pre-persist, and post-persist concerns |
| Multiple named stages | Each stage has clear semantics; hooks can target exactly the right moment | More stages to understand |

### Registration Mechanism

| Option | Pros | Cons |
| --- | --- | --- |
| Inline delegates only | Simple; visible at configuration site | Cannot share hooks across resources; not suitable for JSON-configured resources |
| DI-resolved services only | Testable; reusable | Verbose for one-off hooks |
| Both inline and DI-resolved (named hooks) | Flexible; simple cases use delegates, complex cases use DI | Two mechanisms to understand |

## Decision

### 1. Stage-based hook pipeline within endpoint handlers

Hooks execute inside the endpoint handler, between request parsing and response generation. This gives hooks access to the full `HookContext<TEntity, TKey>` including the typed entity, HTTP context, operation type, and a shared `Items` dictionary for cross-stage data flow.

### 2. Six named stages

The pipeline defines five standard stages plus one error stage, executed in a fixed order:

| Stage | When it runs | Typical use cases |
| --- | --- | --- |
| `OnRequestReceived` | After request parsing, before any validation | Logging, request enrichment, early rejection |
| `OnRequestValidated` | After validation passes (data annotation + custom) | Authorization checks that depend on valid data |
| `BeforePersist` | After validation, before repository call | Data transformation, setting audit fields (e.g., `CreatedBy`) |
| `AfterPersist` | After successful repository call | Audit logging, cache invalidation, event publishing |
| `BeforeResponse` | After persistence, before HTTP response is sent | Response enrichment, adding custom headers |
| `OnError` | When an exception occurs during persistence | Error logging, custom error responses, error suppression |

Each standard stage hook receives a `HookContext<TEntity, TKey>` with:
- `HttpContext` — the current HTTP context
- `Operation` — the `RestLibOperation` enum value (GetAll, GetById, Create, Update, Patch, Delete)
- `ResourceId` — the resource key (for single-resource operations)
- `Entity` — the entity being processed (mutable — hooks can modify it)
- `OriginalEntity` — the entity before any hook modifications (read-only)
- `Items` — a `Dictionary<string, object>` for sharing data between stages
- `Services` — the `IServiceProvider` for resolving dependencies
- `CancellationToken` — the request cancellation token

The `OnError` stage receives an `ErrorHookContext<TEntity, TKey>` which additionally includes the `Exception` and a `Handled` flag.

### 3. Short-circuit support

Any standard stage hook can stop pipeline execution by setting `context.ShouldContinue = false`. When short-circuiting, the hook can optionally set `context.EarlyResult` to an `IResult` that will be returned as the HTTP response. If `ShouldContinue` is false and no `EarlyResult` is set, the endpoint returns no content.

Error hooks can set `context.Handled = true` to suppress the exception and optionally provide `context.ErrorResult` as the HTTP response.

### 4. Both inline delegates and DI-resolved named hooks

**Inline delegates** are configured directly on the endpoint:

```csharp
ep.MapRestLib<Product, Guid>("/api/products", config =>
{
    config.UseHooks(hooks =>
    {
        hooks.BeforePersist = async context =>
        {
            context.Entity.UpdatedAt = DateTime.UtcNow;
        };
    });
});
```

**Named hooks** are registered in DI and resolved by name, enabling JSON-based configuration:

```csharp
services.AddNamedHook<Product, Guid>("set-audit-fields", async context =>
{
    context.Entity.UpdatedAt = DateTime.UtcNow;
});
```

```json
{
  "Hooks": {
    "BeforePersist": "set-audit-fields"
  }
}
```

### 5. Per-request pipeline instances

A new `HookPipeline<TEntity, TKey>` instance is created for each request. The `Items` dictionary is scoped to that request, enabling safe cross-stage data sharing without concurrency concerns.

### 6. Batch operation hook behavior

Batch operations (batch create, update, patch, delete) fire hooks per-item within the batch. This ensures each item receives the same hook processing as it would in a single-item operation. However, batch operations do not fire `BeforeResponse` or `OnError` per-item — these stages are handled at the batch level.

## Rationale

1. **More granular than middleware.** Middleware operates at the HTTP level and cannot distinguish between a Create and an Update, or access the typed entity. The hook pipeline provides per-operation, per-stage interception with full type safety.
2. **No external dependencies.** Unlike MediatR-style pipelines, the hook system is built into RestLib with zero additional packages. This keeps the library lightweight and avoids version conflicts.
3. **Fixed stage order eliminates ambiguity.** Hooks always execute in the same order (OnRequestReceived → OnRequestValidated → BeforePersist → AfterPersist → BeforeResponse). There is no need to specify execution order or priority — the stage name determines when the hook runs.
4. **Short-circuit enables authorization patterns.** A hook in `OnRequestReceived` can reject unauthorized requests before any validation or persistence occurs, reducing unnecessary work.
5. **Named hooks enable declarative configuration.** JSON-configured resources (from `appsettings.json`) can reference hooks by name, which are resolved from DI at runtime. This supports scenarios where endpoint configuration is data-driven rather than code-driven.
6. **Mutable entity enables data transformation.** Hooks can modify the entity (e.g., setting `CreatedBy`, normalizing values) before persistence without requiring the client to send these values.

## Consequences

- Hook execution adds per-request overhead. Benchmarks show this is negligible (sub-microsecond per empty hook), but hooks that perform I/O (e.g., calling external services) will add latency proportional to that I/O.
- Hook execution order is fixed by stage. Users cannot reorder stages or insert custom stages between existing ones.
- Named hook resolution requires DI registration. If a JSON configuration references a hook name that is not registered, the endpoint will fail at runtime when the hook is first invoked.
- Batch operations fire hooks per-item, which means a batch of 100 items will invoke each configured hook 100 times. For performance-sensitive batch operations, users should consider using `IBatchRepository` for bulk persistence instead of relying solely on hooks.
- The `Items` dictionary is untyped (`Dictionary<string, object>`). Users must coordinate key names and value types between hook stages manually.
