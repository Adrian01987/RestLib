# ADR-028 Proposal: Unit of Work

**Status:** Proposed
**Date:** 2026-05-13

## Context

RestLib repositories own single-resource CRUD operations. Batch operations are
explicitly non-transactional at the RestLib level, and ADR-008 says all-or-
nothing semantics belong in the repository when needed.

The ecommerce sample needs a checkout workflow that crosses resource
boundaries:

- create an order;
- create order items;
- decrement product stock;
- create a shipment;
- dispatch an `OrderPlaced` domain event after commit.

That workflow should be implemented as a custom endpoint with an application-
owned EF Core transaction in the sample. The gap is that RestLib has no common
unit-of-work seam for host-owned custom endpoints and generated resources to
share transaction boundaries.

## Decision Needed

Decide whether RestLib should provide an optional `IRestLibUnitOfWork` service
that coordinates transactions across repositories and custom endpoints without
making every repository transactional by default.

## Options

### Option 1: Keep transactions application-owned

Applications use `DbContext.Database.BeginTransactionAsync`, custom
repositories, or provider-specific transaction APIs.

Pros:

- no RestLib core changes;
- keeps provider-specific transaction behavior explicit;
- matches the current repository contract.

Cons:

- generated endpoints and custom endpoints do not share a RestLib convention;
- samples must explain a workaround instead of showing a reusable seam;
- non-EF adapters lack a common transaction story.

### Option 2: Add transaction methods to `IRepository`

Extend `IRepository<TEntity, TKey>` with begin/commit/rollback behavior.

Pros:

- discoverable from the existing abstraction;
- every repository could advertise one consistent surface.

Cons:

- breaking change for all repositories;
- transactions are not meaningful for every persistence adapter;
- resource-specific repositories are the wrong owner for cross-resource
  transactions.

### Option 3: Add optional `IRestLibUnitOfWork`

Introduce a separate optional service that applications and adapters can
register when transaction coordination is available.

Sketch:

```csharp
public interface IRestLibUnitOfWork
{
    Task<IRestLibTransaction> BeginTransactionAsync(CancellationToken ct = default);

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface IRestLibTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct = default);

    Task RollbackAsync(CancellationToken ct = default);
}
```

EF Core adapter sketch:

```csharp
public sealed class EfCoreRestLibUnitOfWork<TContext> : IRestLibUnitOfWork
    where TContext : DbContext
{
    public Task<IRestLibTransaction> BeginTransactionAsync(CancellationToken ct = default);

    public Task<int> SaveChangesAsync(CancellationToken ct = default);
}
```

Checkout endpoint sketch:

```csharp
app.MapPost("/api/storefront/checkout", async (
    IRestLibUnitOfWork unitOfWork,
    EcommerceDbContext db,
    IDomainEventDispatcher events,
    CancellationToken ct) =>
{
    await using var tx = await unitOfWork.BeginTransactionAsync(ct);

    // create order, decrement stock, create shipment
    await unitOfWork.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);

    await events.DispatchAsync(new OrderPlaced(order.Id), ct);
    return Results.Created($"/api/storefront/orders/{order.Id}", order);
});
```

Pros:

- additive and optional;
- custom endpoints can share one transaction convention;
- EF Core can implement it over the scoped `DbContext`;
- leaves non-transactional adapters free to opt out.

Cons:

- generated endpoints still need explicit design before participating in an
  ambient unit of work;
- provider-specific features such as isolation levels need extension points;
- callers must understand that commit and post-commit side effects remain
  application responsibilities.

## Recommended Direction

Keep Phase 5 checkout sample-local with an EF Core transaction. For the future
RestLib seam, prefer Option 3: a separate optional `IRestLibUnitOfWork` service.

Do not add transaction members to `IRepository`. If this proposal is accepted,
make the API additive, adapter-optional, and explicit about generated endpoint
participation.

## Consequences

- The ecommerce sample can document the custom checkout endpoint as the current
  cross-resource transaction workaround.
- RestLib can later support shared transaction coordination without breaking
  existing repository implementations.
- A complete design must cover commit timing, rollback behavior, nested
  transactions, savepoint support, generated endpoint participation, and
  post-commit domain event dispatch.
