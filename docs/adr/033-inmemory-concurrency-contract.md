# ADR-033: InMemory Concurrency and Cancellation Contract

**Status:** Accepted
**Date:** 2026-08-06

## Context

The `RestLib.InMemory` package and `InMemoryRepository<TEntity, TKey>` XML
documentation previously described the adapter simply as thread-safe. The
store uses a `ConcurrentDictionary`, and repository-owned mutations share a
lock, but that short description did not define what was protected.

In particular, entity instances are stored and returned by reference. A caller
can mutate such an object without going through the repository. Collection
operations work over entity references rather than deep copies, and user
delegates or property getters may execute while those references are being used
elsewhere. The adapter also accepted cancellation tokens without observing
them.

Earlier batch-contract work made mutating batch calls all-or-nothing on failure
and serialized them against other repository-owned mutations. Reads still
needed coordination with that shared mutation boundary to avoid capturing
membership between individual writes in a batch commit.

## Decision

### Coordinate repository-owned store operations

Every repository method may be called concurrently. Repository-owned mutations
remain serialized by one lock. Point reads, counts, multi-key reads, and the
shallow membership snapshot used by collection queries coordinate with that
same lock. A repository read therefore observes store membership before or
after an atomic batch commit, never an intermediate subset of that commit.

Collection filtering, search, sorting, and pagination execute after releasing
the lock. This avoids holding the store lock while application property getters,
key selectors, or comparers perform potentially expensive work.

Key selectors, generators, assigners, and preconditions used by mutation paths
execute inside the store critical section. They must not synchronously wait for
a repository call dispatched to another thread on the same instance; that call
cannot enter the store until the callback returns. Although the underlying
monitor is re-entrant, semantic callback re-entry into the same repository is
unsupported: a nested write can invalidate the outer operation's staged state,
and cross-thread callback cycles can deadlock.

### Keep caller-owned entity references

The adapter continues to retain and return the supplied `TEntity` instances.
It does not deep-clone or freeze them. The membership snapshot is shallow: its
set of entity references is stable, but mutable properties can still change if
application code mutates an entity concurrently.

Callers must synchronize direct mutation of shared mutable entities or prefer
immutable entity types. They must also keep stored keys stable. Changing an
entity's key property directly does not move the entry to another dictionary
key. Configured key selectors, generators, assigners, comparers, preconditions,
and entity property getters remain application code and must be safe for their
own concurrent use.
Precondition delegates must be side-effect-free because they receive the stored
entity reference while the repository lock is held.

### Observe cancellation without weakening atomic writes

All token-taking operations reject an already-cancelled token. Collection and
bulk-read loops observe cancellation while enumerating. Mutating operations
check after acquiring the store lock and after application callbacks or
potentially expensive planning steps.

Mutating batches stage and validate their work with cancellation checks, then
check once immediately before the storage commit. No cancellation check occurs
inside or after that commit loop. Once the commit begins it completes and the
method returns its successful result, so cancellation cannot create a partial
batch or report cancellation after persistence succeeded.

The synchronous `Clear` and `Seed` setup helpers do not accept cancellation
tokens. The contract is cooperative rather than preemptive because the adapter
performs synchronous in-process work and returns completed tasks. Cancellation
protects repository storage; it cannot roll back side effects already performed
inside application callbacks or direct mutations to caller-owned input objects.

## Consequences

- The broad thread-safe label is replaced by a specific store-level guarantee.
- Reads and mutations have a clear before-or-after relationship at the shallow
  membership boundary, including batch commits.
- Cancellation can stop long reads and batch planning without violating the
  batch repository's all-or-nothing persistence contract.
- Existing entity identity and mutation behavior remains compatible. Consumers
  that relied on receiving the same object instance continue to do so.
- This is not a cross-resource transaction or deep object-isolation mechanism.
  Application-owned transaction guidance in ADR-028 remains unchanged.

## Alternatives Considered

### Clone entities on write and read

Rejected because it would change observable reference and mutation behavior,
require a cloning policy for arbitrary entity graphs and custom converters, and
add substantial allocation cost to an adapter intended for tests, prototypes,
and demos.

### Document lock-free reads as weakly consistent

Rejected because briefly coordinating the shallow snapshot with the existing
mutation lock provides a much clearer contract and prevents partial batch
membership from escaping without holding the lock during query processing.

### Check cancellation inside batch commit loops

Rejected because cancellation could then persist an arbitrary prefix of a
batch, contradicting the public `IBatchRepository` atomicity contract.
