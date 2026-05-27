# ADR-027: Row-Level Security Is Application-Owned

**Status:** Accepted
**Date:** 2026-05-13

## Context

RestLib validates query parameters before repository calls and keeps endpoint
configuration adapter-neutral. That works well for resource-level authorization,
but it does not provide a first-class seam for row-level scoping such as:

- customers can only see their own carts, addresses, phones, and orders;
- carriers can only see assigned shipments;
- admins can bypass those scopes.

The ecommerce sample demonstrates this need. Its implementation uses EF Core
global query filters in `EcommerceDbContext`, which keeps scoping close to the
application's persistence model and applies before RestLib filtering, sorting,
search, counting, pagination, field selection, and key lookup execute.

## Decision

RestLib will not add a row-level security abstraction in the current public API.
Row-level scoping remains application-owned through one of these mechanisms:

- EF Core global query filters;
- custom repositories;
- database-native row-level security;
- custom Minimal API endpoints when the workflow is not generic resource CRUD.

Hooks are not the recommended scoping mechanism for collection queries because
they run around endpoint execution rather than shaping the repository query
before it reaches the database.

## Consequences

- The core repository contracts stay adapter-neutral and unchanged.
- Generated endpoints remain safe only when the registered repository or
  persistence model already applies the correct row-level scope.
- Samples must show row-level scoping at the application boundary, not as a
  RestLib feature toggle.
- Future work may add an optional query-shaping capability for repositories that
  can expose `IQueryable<TEntity>`, but it must be additive and fail clearly when
  a resource requires scoping and the repository does not support it.

## Future Option

A future API could look like `ConfigureQueryable`, applied only when the
repository advertises an explicit query-shaping capability:

```csharp
app.MapRestLib<Order, Guid>("/api/storefront/orders", config =>
{
    config.RequirePolicyForOperations("Customer", RestLibOperation.GetAll, RestLibOperation.GetById);
    config.ConfigureQueryable((query, context) =>
    {
        var currentUser = context.HttpContext.RequestServices.GetRequiredService<ICurrentUser>();
        return currentUser.IsAdmin
            ? query
            : query.Where(order => order.CustomerId == currentUser.UserId);
    });
});
```

That remains out of scope for the current API because it would introduce an
adapter-specific query model into a core configuration surface.
