# ADR-028: Unit of Work Is Application-Owned

**Status:** Accepted
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

That workflow does not fit generic resource CRUD. It is a business command with
transactional and post-commit side-effect requirements.

## Decision

RestLib will not add a unit-of-work abstraction in the current public API.
Cross-resource transactions remain application-owned through normal provider
tools such as `DbContext.Database.BeginTransactionAsync`, custom repositories,
or database transaction APIs.

Generated endpoints keep their current repository-per-operation semantics.
Custom endpoints should own transaction boundaries for business workflows that
span resources.

## Consequences

- Existing repository interfaces remain unchanged.
- Non-transactional adapters such as `RestLib.InMemory` do not need to expose a
  transaction concept that would be misleading.
- Applications that need all-or-nothing behavior can implement it in custom
  repositories or custom endpoints without waiting for RestLib API changes.
- Samples should show explicit transaction ownership in application code.

## Future Option

A future additive API could introduce an optional `IRestLibUnitOfWork` service:

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

That remains out of scope until RestLib has a complete design for commit timing,
rollback behavior, nested transactions, generated endpoint participation, and
post-commit side effects.
