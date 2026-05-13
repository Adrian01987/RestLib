# ADR-026: EF Core Projection Pushdown

## Status

Accepted.

## Context

RestLib field selection is implemented in the core endpoint layer and works for
any repository by materializing entities first, then applying `FieldProjector`.
That preserves adapter neutrality, but EF Core resources backed by wide tables
can still read columns that the client did not request.

The EF Core adapter can reduce that cost by translating eligible sparse field
selection requests into SQL projections. The optimization must preserve the
existing response contract and avoid interfering with features that need a full
entity instance.

## Decision

EF Core projection pushdown is opt-in through
`EfCoreRepositoryOptions<TEntity, TKey>.EnableProjectionPushdown`. The default
remains `false`.

When enabled, the EF Core repository advertises
`IFieldSelectionProjectionRepository<TEntity, TKey>` and can project direct
scalar field selections into an EF-translatable `Select` expression. The
projection includes the requested fields, the primary key, and any fields needed
for active filters or sorting.

Core endpoint handlers decide whether the projection repository may be used.
They skip pushdown when:

- field selection is empty;
- the repository does not implement the projection capability;
- HATEOAS is enabled;
- ETag support is enabled;
- hooks are configured for the endpoint.

If the handler allows the projection-capability path but the EF Core adapter
cannot build a safe direct scalar projection, the adapter returns `null` and the
endpoint falls back to the normal materialized path. Nested field selections are
handled inside the EF Core adapter: it declines SQL projection pushdown, loads
the required reference navigations where possible, and then lets the endpoint
apply the normal sparse response projection.

## Consequences

The optimization is conservative and preserves existing behavior by default.
Applications that opt in can reduce selected columns for common direct scalar
field-selection requests.

Nested selections, HATEOAS, ETags, and hooks continue to use full entity
materialization because those paths may require entity shape, link generation,
ETag calculation, or hook access to the full entity.

The feature is fluent-only at repository registration time. It is not part of
JSON resource configuration and does not require schema changes.
