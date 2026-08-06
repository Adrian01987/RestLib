# ADR-031: Bounded Performance Caches and Batch-Key Planning

**Status:** Accepted
**Date:** 2026-08-06

## Context

The Q-15 structural refactor gave EF Core key metadata, pagination, projection,
and batch-key lookup focused internal owners. It deliberately did not cache
their work. A scoped `EfCoreRepository` therefore still rediscovered EF model
metadata and compiled resource-key accessors for every scope, while recurring
pagination and projection shapes rebuilt equivalent immutable plans.

Batch lookup had a separate scaling problem. It accepted duplicate keys and
built one left-associated OR tree containing every key-part comparison. The
default HTTP batch limit of 100 kept ordinary endpoint requests modest, but the
public repository contract also permits direct calls and an unlimited HTTP
batch configuration. Large calls could produce deep expression trees, large
SQL statements, provider parameter-limit failures, and avoidable EF/database
plan churn. Scalar, alternate, and two-part composite resource keys all had to
retain the identity and ordering contracts established by Q-04, Q-07, and
Q-09.

The built-in reflection mapper also rediscovered matching properties for a
closed API/database model pair and copied values through `PropertyInfo` on
each mapping call. Any optimization must leave explicitly registered and named
custom mappers under the application's DI lifetime rather than silently
promoting them to process-wide singletons.

## Decision

### Cache identity follows the actual EF and RestLib configuration identities

`EfCoreRepositoryPlanCache<TEntity, TKey>` is closed over the entity and key
types. Within that closed type it keys bundles first by the exact finalized
`IModel` object and then by the exact
`EfCoreRepositoryOptions<TEntity, TKey>` object. Both levels use
`ConditionalWeakTable`, so the cache does not extend either object's lifetime.

An options entry also snapshots the exact `KeySelector` expression reference.
If that property is replaced on the same options instance, the entry builds a
new bundle before another repository uses it. This reference identity is
intentional: two structurally similar expression objects are not assumed to
carry the same captured state. Other mutable options remain request/scope-time
inputs. In particular, projection enablement and logging are read through
accessors and are not frozen into cached metadata.

Each bundle contains immutable `EfCoreKeyMetadata` plus the page and projection
planning caches shared by equivalent repository scopes. It never contains a
`DbContext`, query, tracked entity, logger, cancellation token, or request
value.

### Query-plan caches are normalized and bounded

Keyset planning is keyed by the ordered effective sort shape: mapped property,
direction, and query-parameter identity, including stable resource-key
tie-breakers. Projection planning is keyed by the normalized set of CLR
properties required by keys, selected fields, filters, and sorts. Request
values, cursor contents, page limits, and entity instances are not part of a
plan and are never retained.

Supported and unsupported immutable planning results are cached. Each page or
projection cache retains at most 256 shapes. Once full, it continues to return
newly built results for unseen shapes without retaining them; it does not evict
hot entries merely because an application submits unbounded one-off shapes.
Cache access and construction are serialized by the cache's private lock.

### Batch-key queries use a conservative bounded parameter budget

`EfCoreBatchKeyQueryExecutor<TEntity, TKey>` owns batch-key normalization and
query execution. It removes duplicate input keys with the default `TKey`
equality comparer while preserving their first-occurrence order for chunk
formation. Callers retain the original request list and continue to associate
update and PATCH results in original order with the documented duplicate
multiplicity. Delete and keyed-read results continue to coalesce duplicates.

One query represents at most 512 submitted key-part values:

- a scalar or alternate resource key uses a parameterizable
  `keys.Contains(entity.Key)` predicate and a maximum of 512 distinct keys;
- a two-part composite key uses exact pair comparisons and a maximum of 256
  distinct keys; its OR-of-AND expression is balanced rather than
  left-associated, bounding expression depth logarithmically.

RestLib does not force EF Core's constant, multiple-parameter, or single
collection-parameter translation mode for scalar `Contains`. The application's
provider configuration remains authoritative. Composite row-value `Contains`
is not used because translation is not portable across EF relational
providers.

Chunks execute sequentially because a `DbContext` does not support concurrent
operations. The caller supplies the base query, preserving tracking for
update/PATCH/delete and `UseAsNoTracking` for keyed reads. All matching entities
are fetched before repository mutation begins, and mutating batch methods still
call `SaveChanges` once. Database row order is never used for result
association.

### Only built-in stateless mapping is shared

The built-in identity mapper is one stateless instance per closed model type.
The built-in reflection mapper lazily compiles object-construction and property
assignment delegates once per closed API/database model pair and exposes a
shared stateless instance. JSON auto-mapping uses that shared instance when no
explicit reflection-mapper service is registered.

Custom `IRestLibMapper` registrations, including named mappers, are still
resolved from the active request service provider. RestLib neither caches
those instances nor changes their transient, scoped, or singleton DI lifetime.
An explicitly registered `ReflectionRestLibMapper` is likewise resolved from
DI before the shared fallback is considered.

### Benchmarks are evidence hooks, not a universal claim

`MappingBenchmarks` compares the former `PropertyInfo` copy loop with the
compiled built-in mapper. `EfCorePlanningBenchmarks` compares recurring
repository/projection planning with fresh versus stable cache identities.
`EfCoreBatchKeyBenchmarks` exercises real SQLite keyed reads at 512 and 2,048
distinct scalar and composite keys, with duplicate inputs included.

These benchmarks make the affected costs reproducible and guard against adding
complexity without a measurable workload. A Debug or BenchmarkDotNet `Dry` run
is only a functional smoke test. Results depend on runtime, hardware, provider,
database size, query distribution, and configured EF translation mode; this
decision makes no blanket latency, allocation, or throughput claim.

## Alternatives Considered

### Cache by CLR types alone

Rejected because the same entity/key types can participate in different EF
models and RestLib registrations. Type-only caching would reuse metadata across
incompatible mappings and could retain dynamically created models forever.

### Structurally compare key-selector expressions

Rejected because structural equality is complex and can hide different
captured values. Exact expression reference identity makes invalidation
predictable and matches the stable options object produced by normal DI
registration.

### Use an unbounded query-shape cache

Rejected because property combinations and sort shapes can be application- or
client-driven. A cache intended to reduce repeated planning must not turn
unbounded shape diversity into process-lifetime memory growth.

### Use provider-specific table-valued parameters, temporary tables, or row values

Rejected for the built-in adapter because those mechanisms need
provider-specific SQL, lifecycle, and transaction handling. Applications with
larger or specialized bulk workloads can provide a custom batch repository.

### Cache every mapper returned by DI

Rejected because doing so would violate custom mapper lifetimes and could make
scoped dependencies captive. Only RestLib-owned, stateless implementations are
shared.

## Consequences

- Equivalent repository scopes reuse key discovery, compiled key accessors,
  keyset plans, and projection plans without retaining models or options after
  the application releases them.
- One-off query-shape diversity has a fixed retention bound, at the cost of
  rebuilding unseen shapes after a cache reaches capacity.
- Large keyed reads avoid one unbounded predicate. Calls above 512 scalar or
  256 composite keys use additional sequential database round trips.
- Chunked reads do not create a point-in-time snapshot across queries. An
  application that requires that isolation must provide an appropriate
  transaction or custom repository; existing RestLib transaction ownership is
  unchanged.
- The 512-part budget is deliberately conservative for common SQL Server and
  SQLite limits, but RestLib cannot guarantee every third-party provider's
  configured limit or best plan. Provider-specific tuning remains outside the
  portable adapter contract.
- EF still owns LINQ translation, compiled-query caching, SQL generation, and
  database plan selection. Request predicates and cursor values are built per
  call; this ADR does not claim that every query becomes a precompiled EF
  query.
- Built-in mapping avoids per-entity reflection access, while application
  mappers retain their configured DI behavior.
