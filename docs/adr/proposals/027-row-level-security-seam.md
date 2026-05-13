# ADR-027 Proposal: Row-Level Security Seam

**Status:** Proposed
**Date:** 2026-05-13

## Context

RestLib validates query parameters before repository calls and keeps endpoint
configuration adapter-neutral. That works well for resource-level authorization,
but it does not provide a first-class seam for row-level scoping such as:

- customers can only see their own carts, addresses, phones, and orders;
- carriers can only see assigned shipments;
- admins can bypass those scopes.

The ecommerce sample will demonstrate this need immediately. Its implementation
will use EF Core global query filters in `EcommerceDbContext`, which is the
right application-owned workaround today. The gap is that RestLib itself has no
portable way to express "all generated queries for this resource must be scoped
by the current request".

## Decision Needed

Decide whether RestLib should expose a queryable configuration seam that lets
applications apply request-aware scoping before filtering, sorting, search,
counting, pagination, field selection, and key lookup execute.

## Options

### Option 1: Keep scoping entirely in repositories and DbContexts

Applications continue using EF Core global query filters, custom repositories,
or database-native row-level security.

Pros:

- no RestLib core changes;
- works today;
- preserves adapter neutrality.

Cons:

- the recommended pattern is not visible at the RestLib resource boundary;
- non-EF repositories have no common convention;
- generated OpenAPI and sample code cannot show an explicit RestLib scoping
  seam.

### Option 2: Add endpoint-level authorization hooks only

Use existing hooks to reject individual requests after parsing and before
persistence.

Pros:

- no new public abstraction;
- hooks are already resource-aware and request-aware.

Cons:

- hooks cannot safely scope collection queries before repository execution;
- rejecting after `GetById` risks loading unauthorized rows;
- each resource must duplicate ad hoc checks.

### Option 3: Add `ConfigureQueryable`

Add a query-shaping seam for repositories that can expose queryable behavior.
The application supplies a request-aware callback at resource registration time.

Sketch:

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

Possible abstraction:

```csharp
public sealed class RestLibQueryableContext
{
    public required HttpContext HttpContext { get; init; }

    public required RestLibOperation Operation { get; init; }

    public object? ResourceId { get; init; }
}

public interface IQueryableRepository<TEntity, TKey> : IRepository<TEntity, TKey>
    where TEntity : class
    where TKey : notnull
{
    Task<TEntity?> GetByIdAsync(
        TKey id,
        Func<IQueryable<TEntity>, RestLibQueryableContext, IQueryable<TEntity>> configure,
        RestLibQueryableContext context,
        CancellationToken ct = default);

    Task<PagedResult<TEntity>> GetAllAsync(
        PaginationRequest pagination,
        Func<IQueryable<TEntity>, RestLibQueryableContext, IQueryable<TEntity>> configure,
        RestLibQueryableContext context,
        CancellationToken ct = default);
}
```

Pros:

- makes row-level scoping explicit at the RestLib resource boundary;
- applies before query execution;
- EF Core can translate scopes server-side;
- keeps the sample workaround traceable to a future RestLib API.

Cons:

- `IQueryable` is not adapter-neutral for all persistence technologies;
- requires careful fallback behavior for repositories that do not implement the
  capability;
- expression callbacks can leak EF/provider assumptions into application
  configuration.

## Recommended Direction

Keep Phase 1 sample scoping in EF Core global query filters, then explore
Option 3 as an additive EF-capability seam rather than a mandatory core
repository contract.

The public configuration should look like `ConfigureQueryable`, but endpoint
handlers should only use it when the repository advertises the capability. When
the capability is absent, startup should fail with a clear message if a resource
requires query scoping. This avoids silent authorization gaps.

## Consequences

- The ecommerce sample can document EF Core global query filters as the current
  workaround.
- RestLib can later add request-aware query scoping without changing the
  existing `IRepository<TEntity, TKey>` contract.
- Any accepted design must define behavior for `GetById`, `GetAll`, counts,
  batch operations, mapped resources, hooks, and projection pushdown.
