# ADR-020: Structured Logging

**Status:** Accepted
**Date:** 2026-04-11

## Context
RestLib had zero logging prior to this change. Silent exception catches
in 9 locations swallowed errors without any diagnostic output, making
production troubleshooting difficult. Operators had no visibility into
request processing, hook execution, batch pipeline outcomes, or error
paths. Adding structured logging was identified as a high-priority
improvement for operational readiness.

## Decision

### Source-generated `[LoggerMessage]` over manual `ILogger.Log` calls
All log messages use the `[LoggerMessage]` attribute on `static partial`
methods. The source generator produces zero-allocation logging code that
avoids boxing, string interpolation, and delegate allocation when the
log level is disabled. This is the recommended approach for
high-performance .NET libraries.

Log message definitions are grouped by area in partial classes under
`src/RestLib/Logging/`:

| File                                  | EventId Range | Area                          |
|---------------------------------------|---------------|-------------------------------|
| `RestLibLogMessages.Endpoints.cs`     | 1000–1099     | CRUD endpoint handlers        |
| `RestLibLogMessages.Batch.cs`         | 1100–1199     | Batch pipeline processing     |
| `RestLibLogMessages.Hooks.cs`         | 1200–1249     | Hook pipeline execution       |
| `RestLibLogMessages.Infrastructure.cs`| 1300–1349     | Parsers, cursor, options, ETag|

### Resolve logger at request-time, not map-time
Handlers resolve `ILogger` from `HttpContext.RequestServices` via
`RestLibLoggerResolver.ResolveLogger()` at request-time. This mirrors
the existing `OptionsResolver` pattern and avoids capturing a logger in
the closure at map-time, which would:

- Bind to a single `ILoggerFactory` instance that may not yet be fully
  configured when `MapRestLib()` runs
- Prevent per-request scoped logging (e.g., enriched with request IDs)
- Couple the logger lifetime to the endpoint delegate lifetime

For batch pipelines, the logger is carried on `BatchContext<TEntity, TKey>`
so pipeline methods don't re-resolve from `HttpContext` on every call.

For hook pipelines, the logger is passed to the `HookPipeline` constructor
so `ExecuteStageAsync` can emit Trace-level entry/exit events without
requiring callers to pass the logger per-stage.

### Log level policy

| Level       | Usage                                                      |
|-------------|------------------------------------------------------------|
| Trace       | Hook stage entry/exit with stage name and operation        |
| Debug       | Request entry, repository calls, parse results, cursor/filter failures, ETag precondition failures |
| Information | Successful mutations (create with ID, delete), batch summaries, 4xx ProblemDetails responses |
| Warning     | Silent exception fallbacks (bulk persistence, envelope parse), options not registered |
| Error       | Swallowed hook exceptions, 5xx ProblemDetails responses, unhandled endpoint exceptions |

### Decision not to log entity payloads
Entity payloads, full cursor strings, request/response bodies, and
per-entity data in hot-path loops are explicitly excluded from logging.
This prevents:

- Accidental PII/sensitive data exposure in log sinks
- Log volume explosion under high throughput
- Performance degradation from serializing entities for logging

Structured parameters include only identifiers (entity name, resource ID),
counts, status codes, and operation names — sufficient for diagnostics
without data leakage.

### Backward-compatible optional parameters
All logging integration uses optional `ILogger? logger = null` parameters
on existing methods (`ProblemDetailsResult.Create()`, `HookHelper.HandleErrorHookAsync()`,
`CursorEncoder.TryDecode()`, etc.). This ensures:

- Existing callers (including user code and tests) continue to work
  without modification
- Logging activates only when a logger is explicitly provided
- No new required dependencies or DI registrations — `ILoggerFactory`
  is already available in any ASP.NET Core host

### Logger category naming
Each handler resolves a logger with category `"RestLib.{Operation}"`
(e.g., `"RestLib.GetAll"`, `"RestLib.Batch"`). This allows operators to
configure log levels per-endpoint area:

```json
{
  "Logging": {
    "LogLevel": {
      "RestLib.GetAll": "Warning",
      "RestLib.Batch": "Debug",
      "RestLib.OptionsResolver": "Warning"
    }
  }
}
```

## Consequences
- All 9 previously silent catch blocks now emit diagnostic log messages
- `BatchContext<TEntity, TKey>` gains a required `ILogger Logger` property
  (internal, no public API impact)
- `HookPipeline` constructor gains an optional `ILogger?` parameter
  (internal, no public API impact)
- `CursorEncoder.TryDecode` and `CursorEncoder.IsValid` gain optional
  `ILogger?` parameters (public API, backward-compatible)
- `ProblemDetailsResult` convenience methods gain optional `ILogger?`
  parameters (public API, backward-compatible)
- Zero additional NuGet dependencies — `Microsoft.Extensions.Logging`
  is already available via the `Microsoft.AspNetCore.App` framework
  reference
- When no `ILoggerFactory` is registered (e.g., unit tests without a
  host), `RestLibLoggerResolver` falls back to `NullLogger.Instance` —
  no exceptions, no overhead
