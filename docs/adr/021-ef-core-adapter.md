# ADR-021: EF Core Repository Adapter

**Status:** Accepted
**Date:** 2026-04-15

## Context
RestLib's core abstractions are intentionally persistence-agnostic. The library defines
`IRepository<TEntity, TKey>` for CRUD operations, `IBatchRepository<TEntity, TKey>` for
bulk operations, and `ICountableRepository<TEntity, TKey>` for collection counting, but
it does not mandate a specific database or ORM. That separation keeps the core package
 usable for custom repositories, test doubles, and non-relational backends.

The existing InMemory adapter covers demos, tests, and quick prototypes, but production
applications typically need a repository implementation backed by a real database. Entity
Framework Core is the most widely adopted ORM in the .NET ecosystem, so an official EF
Core adapter provides the most natural path for RestLib users who want to map the core
HTTP and query capabilities onto relational persistence without writing repetitive
repository code themselves.

The adapter also had to respect a key product constraint: no changes to RestLib core
source code. The EF Core package therefore had to implement the existing repository
contracts, fit the established pagination and query models, and integrate through DI
extension methods rather than through changes to endpoint or abstraction layers.

## Decision

### Keyset pagination for stable sorts with explicit offset fallback
The EF Core adapter implements RestLib's cursor pagination contract as true forward-only
keyset pagination when a stable sortable property set is available. `EfCoreRepository.GetAllAsync`
builds an ordered sort plan from the effective request sort plus the primary-key tie-breaker,
encodes the last-seen sort values into an opaque cursor payload, and translates subsequent
pages into a `WHERE` predicate of the form:

`(Sort1 > last1) OR (Sort1 = last1 AND Sort2 > last2) ...`

For descending sorts the comparison operators are inverted accordingly. The key is always
included as the final tie-breaker so pages remain deterministic even when many rows share
the same client-visible sort value.

This strategy was chosen because it improves deep-page performance and makes forward
pagination stable under inserts between requests for the common case of simple scalar sorts.
It also preserves RestLib's existing opaque cursor HTTP contract: the public API still uses
the same `cursor` parameter and `next` link shape, while the EF Core adapter is free to use
an adapter-specific payload internally.

The adapter still falls back to offset pagination for unsupported sort shapes. In the current
implementation, keyset pagination is used only when all effective sort fields resolve to direct,
database-friendly scalar CLR properties (including strings, numbers, GUIDs, dates, booleans,
nullable variants, and enums). When that condition is not met, the adapter logs a warning and
continues with encoded offset cursors so the public API remains usable.

This is intentionally a statement about the EF Core adapter implementation, not a broader
claim that every RestLib cursor is a keyset cursor. RestLib's public API exposes an opaque
cursor contract; adapter implementations can satisfy it with either keyset state or offset
state. See ADR-001 for the library-level pagination contract.

### AsNoTracking by default for read operations
The adapter defaults `EfCoreRepositoryOptions.UseAsNoTracking` to `true` and applies that
setting to read queries through `GetBaseQuery()`. `GetByIdAsync` uses
`AsNoTracking().FirstOrDefaultAsync(...)` when no-tracking is enabled, while collection
queries such as `GetAllAsync`, `GetByIdsAsync`, and `CountAsync` all flow through the same
conditional base-query helper.

This default reflects the common usage profile of REST APIs: reads are far more frequent
than writes, and most read endpoints do not need identity resolution or change tracking.
Using `AsNoTracking` lowers memory overhead, avoids unnecessary change tracker work, and
improves read performance without changing the externally observable repository contract.

Write operations intentionally ignore the no-tracking default and continue to use tracked
entities. `UpdateAsync`, `PatchAsync`, and `DeleteAsync` rely on `FindAsync` and tracked
entity state because EF Core change tracking is what enables `SetValues`,
`EntityEntry.Property(...).CurrentValue`, and `Remove(...)` to work cleanly. Consumers who
need tracking on reads can opt out explicitly through the options callback.

### JSON Merge Patch via EF Core change tracking
Partial updates are implemented by loading the target entity as a tracked EF Core entity,
iterating the incoming JSON patch document, mapping snake_case JSON fields to CLR
properties through `SnakeCasePropertyMap`, and assigning values through
`EntityEntry.Property(name).CurrentValue`. Primary key properties are excluded so a patch
cannot mutate the entity identity.

This design uses EF Core the way it is intended to be used. Change tracking allows the
adapter to update only the properties that were actually patched, and `SaveChangesAsync`
can generate an `UPDATE` that reflects the modified columns instead of rewriting the full
row. It also keeps patch behavior aligned with the rest of the EF Core persistence model,
including type conversion, validation integration, and concurrency handling.

Alternatives were considered but rejected. Deserializing the patch into a full entity and
calling `SetValues()` would blur the distinction between full replacement and partial
update, making it too easy to overwrite properties that were not present in the incoming
document. Using lower-level bulk update APIs such as `ExecuteUpdate()` would bypass the
tracked entity model and complicate parity with hooks, validation, and existing update
semantics.

### Opt-in projection pushdown for field selection
Field selection remains a core API-layer feature, but the EF Core adapter now exposes an
optional projection capability through `IFieldSelectionProjectionRepository<TEntity, TKey>`.
When `EfCoreRepositoryOptions.EnableProjectionPushdown` is enabled and the request selects
only direct scalar properties, the adapter builds an `Expression<Func<TEntity, TEntity>>`
projection, automatically includes the primary key plus any active filter/sort columns, and
applies `.Select(...)` before materialization.

This was chosen as an opt-in behavior because it changes the SQL shape and can affect how
much data is available to downstream features. To preserve existing observable semantics,
the core endpoints only use pushdown when the repository advertises projection support and
when HATEOAS links, ETag generation, and hooks are not active for the request. If any
requested field is not safely projectable, the repository returns `null` for the projection
attempt and the endpoints fall back to the existing post-fetch `FieldProjector` behavior.

The current implementation intentionally limits pushdown to direct scalar CLR properties
(including strings, numbers, GUIDs, dates, booleans, nullable variants, and enums). That
keeps the generated `Select` expression straightforward and provider-friendly while still
covering the common wide-entity scenario that motivated the optimization. Navigation paths,
blob-like shapes, and other non-projectable members continue to use the previous fallback.

### Automatic primary key detection from EF Core model metadata
The adapter resolves the key selector automatically when the caller does not provide one.
`EfCoreRepository` now resolves the selector from the real scoped `DbContext` during
repository construction, reads the EF Core model metadata for the entity type, finds the
primary key, verifies that it is a single-property key, checks that the key type matches
`TKey`, and builds the corresponding `Expression<Func<TEntity, TKey>>`.

This was chosen to reduce boilerplate for the common case. In most EF Core applications,
the model already knows the primary key, so forcing every registration to repeat
`entity => entity.Id` would add noise without adding meaningful clarity. Auto-detection
makes the happy path smaller while still allowing an explicit override when a consumer
wants full control.

Resolving metadata from the real scoped `DbContext` avoids the DI anti-pattern of building
a temporary `ServiceProvider` during registration and removes startup-order sensitivity.
The trade-off is that invalid entity-model or key-shape configurations now surface when the
repository is resolved from DI rather than during service registration. Composite keys are
still rejected with a clear error message because the current repository abstraction is
built around a single `TKey` value rather than a composite key shape.

### Scoped repository lifetime matching DbContext
The EF Core repository is registered as `Scoped`, and all three repository interfaces
(`IRepository`, `IBatchRepository`, and `ICountableRepository`) forward to the same scoped
instance. This matches the default `DbContext` lifetime used by `AddDbContext` and ensures
that all repository behavior for a request shares the same EF Core unit of work.

Scoped lifetime was chosen because the repository is effectively a thin wrapper around the
`DbContext`. Registering it as a singleton would create an invalid captive dependency over
a scoped context. Registering it as transient would be technically possible but would add
unnecessary allocations and make it easier to accidentally fragment per-request state.

Using a single scoped repository instance per request also keeps behavior predictable for
tracking, batching, and exception handling. A request sees one repository, one context,
and one consistent view of tracked state, while concurrent requests remain isolated from
one another.

## Consequences
- The adapter satisfies RestLib's repository contracts without requiring changes to core
  RestLib source code.
- Cursor pagination remains compatible with the existing opaque cursor contract, but EF Core
  now uses keyset pagination for supported stable sorts and falls back to `Skip`/`Take` only
  for unsupported sort shapes.
- Read-heavy API workloads benefit from `AsNoTracking` by default, while applications that
  need tracked reads must opt out explicitly.
- Partial updates integrate cleanly with EF Core and persist only modified properties, but
  patch behavior remains tied to EF Core change tracking.
- Primary key auto-detection reduces configuration noise for simple entities, resolves from
  the real scoped `DbContext`, and does not support composite keys.
- Scoped lifetime keeps the repository aligned with `DbContext` semantics and request
  isolation, but it also means repository instances are not suitable for singleton caching
  patterns.
