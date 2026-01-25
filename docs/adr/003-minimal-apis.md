# ADR-003: Minimal APIs Over Controllers

**Status:** Accepted  
**Date:** 2026-01-25

## Context

ASP.NET Core offers two approaches for building HTTP endpoints:

- **Controllers:** Traditional MVC pattern with `[ApiController]` attribute, action methods, and model binding
- **Minimal APIs:** Lambda-based routing introduced in .NET 6, using `MapGet`, `MapPost`, etc.

RestLib generates CRUD endpoints dynamically, so we need to choose which paradigm to build upon.

## Options Considered

| Option       | Pros                                                        | Cons                                                       |
| ------------ | ----------------------------------------------------------- | ---------------------------------------------------------- |
| Controllers  | Familiar to most developers, feature-rich, built-in filters | Verbose, slower startup, more ceremony, reflection-heavy   |
| Minimal APIs | Concise, fast startup, AOT-friendly, less magic             | Less discoverable, fewer built-in features, newer paradigm |

## Decision

Use **Minimal APIs** with `RouteGroupBuilder` for endpoint generation.

```csharp
public static RouteGroupBuilder MapRestLib<TEntity, TKey>(
    this IEndpointRouteBuilder endpoints,
    string prefix,
    Action<RestLibEndpointConfiguration<TEntity, TKey>>? configure = null)
{
    var group = endpoints.MapGroup(prefix);

    group.MapGet("", GetAllHandler);
    group.MapGet("/{id}", GetByIdHandler);
    group.MapPost("", CreateHandler);
    group.MapPut("/{id}", UpdateHandler);
    group.MapPatch("/{id}", PatchHandler);
    group.MapDelete("/{id}", DeleteHandler);

    return group;
}
```

## Rationale

1. **Alignment with vision:** RestLib's goal is "minimal boilerplate" — Minimal APIs embody this philosophy
2. **Performance:** Faster startup time and lower memory footprint compared to controller-based routing
3. **.NET 8 maturity:** Route groups, endpoint filters, and OpenAPI support are now feature-complete
4. **AOT compatibility:** Better support for Native AOT compilation, important for cloud-native scenarios
5. **Simplicity:** No need for controller base classes, attributes, or conventions — just functions
6. **Composability:** `RouteGroupBuilder` allows further customization by the consumer

## Consequences

- **Requires .NET 8 or later** — we don't support older frameworks
- **Some advanced filter scenarios** need custom implementation (though endpoint filters help)
- **Documentation should cover** Minimal API concepts for developers unfamiliar with the paradigm
- **Route groups enable** clean prefix handling and shared metadata
- **Integration testing** uses `WebApplicationFactory` seamlessly

## Example Output

For `app.MapRestLib<Product, Guid>("/api/products")`:

| Method | Route                | Handler        |
| ------ | -------------------- | -------------- |
| GET    | `/api/products`      | GetAllHandler  |
| GET    | `/api/products/{id}` | GetByIdHandler |
| POST   | `/api/products`      | CreateHandler  |
| PUT    | `/api/products/{id}` | UpdateHandler  |
| PATCH  | `/api/products/{id}` | PatchHandler   |
| DELETE | `/api/products/{id}` | DeleteHandler  |

## References

- [Microsoft - Minimal APIs Overview](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/overview)
- [.NET 8 Minimal API Improvements](https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-8.0)
- [Route Groups in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/route-handlers#route-groups)
