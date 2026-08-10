# ADR-008: Batch Operations

**Status:** Accepted (amended 2026-08-10)
**Date:** 2026-03-25

## Context
RestLib provides per-entity CRUD endpoints. Production APIs commonly need to
create, update, or delete multiple resources in a single request to reduce
round trips and support workflows like bulk imports and synchronizations.

## Decision

### Single endpoint with action envelope
All batch operations go through `POST /prefix/batch` with a JSON envelope:
`{ "action": "create|update|patch|delete", "items": [...] }`. This keeps
routing simple (one endpoint) and allows the action to be determined from the
request body rather than HTTP method.

### Action-aware authorization and rate limiting

Authorization for the shared endpoint is evaluated after the envelope action is
parsed and confirmed to be enabled. Disabled actions return the batch-action-not-enabled
response without requiring authorization services or disclosing an authorization
outcome for an unavailable operation. For enabled actions, RestLib combines inherited
endpoint/group authorization metadata with the configuration for the corresponding
`BatchCreate`, `BatchUpdate`, `BatchPatch`, or `BatchDelete` operation, then delegates
evaluation and challenge/forbid handling to ASP.NET Core's authorization services. An
operation configured with `AllowAnonymous` bypasses inherited authorization in the
same way as an ordinary anonymous endpoint.

ASP.NET Core rate-limit middleware runs before the handler parses the body, so it
cannot select a named policy from the envelope action. Every action enabled on one
shared batch route must therefore resolve to the same effective rate-limit policy
and disabled state. Endpoint mapping fails fast when those settings differ. A
resource that needs different batch rate limits must enable one action on that
route or expose the operations as separate resources/routes.

### Partial success semantics
Each accepted array member is processed independently. The response uses 200
only when every member succeeds and 207 Multi-Status when one or more members
fail, including when all members fail. Each item in the response carries its own
`status`, `entity` (on success), and `error` (on failure). This is more practical
than all-or-nothing for large imports.

Once RestLib accepts a syntactically valid request envelope containing a
non-empty `items` array within the configured size limit, the array defines the
response cardinality. The response has exactly one entry per array member in the
same order, and each entry's `index` is that member's zero-based request
position. RestLib normalizes and deserializes each member independently for the
requested action. A member that cannot be decoded receives an indexed 400
`/problems/bad-request` result, does not reach validation, hooks, or persistence,
and does not prevent valid siblings from continuing. Validation, not-found,
hook, persistence, and repository-contract failures likewise stay attached to
the request member that produced them.

Failures that prevent RestLib from accepting the envelope remain one top-level
400 response rather than an indexed batch result. These include syntactically
malformed JSON, a missing, null, non-array, or empty `items` member, an unknown or
disabled action, and a request that exceeds `MaxBatchSize`.

### Non-transactional processing

Batch operations are **non-transactional by design**. There is no rollback
mechanism at the RestLib level. The specific behaviour depends on which
persistence path is used:

Because valid members can commit while another member receives an indexed 400,
clients should retry only failed result slots. Replaying the complete request
can duplicate effects for members that already succeeded.

**Individual path** (`PersistIndividuallyAsync`): Each item is persisted in
its own `try`/`catch`. If item 3 of 5 throws, items 1-2 are already
persisted, item 3 gets a 500 error result, and items 4-5 continue
processing normally. No previously-persisted items are rolled back.

**Bulk path** (`PersistBulkAsync`): All validated items are passed to a
single bulk repository method (e.g. `CreateManyAsync`, `UpdateManyAsync`,
`PatchManyAsync`, `DeleteManyAsync`). The `IBatchRepository` contract requires
each mutating method to be all-or-nothing with respect to repository
persistence. If a bulk repository operation throws, RestLib does not retry any
item; it reports a 500 result for every unresolved item. A retry is still
unsafe because RestLib cannot verify an external implementation's transaction
boundary or any side effects outside repository persistence.

This guarantee applies only to the validated items passed to one bulk
repository call. It does not make the complete HTTP batch request
transactional: items can be excluded by validation and hooks, and the
individual fallback path persists each item independently.

### Optional IBatchRepository
`IBatchRepository<TEntity, TKey>` is an optional interface with batch-optimized
methods (`CreateManyAsync`, `UpdateManyAsync`, `PatchManyAsync`,
`DeleteManyAsync`, `GetByIdsAsync`). When the repository implements it,
RestLib uses the batch methods for better performance. Otherwise, it falls
back to looping over `IRepository` methods without first attempting a bulk
operation. This avoids breaking existing repository implementations.

For bulk update and patch, RestLib first performs read-free member decoding and
structural checks, gathers the keys that survive those checks, and invokes
`GetByIdsAsync` once when at least one candidate survives. It performs no
per-item `GetByIdAsync` calls on that bulk path. The returned keyed lookup is
the pre-write snapshot used for existence
results, model validation, merge-patch previews, and the `OriginalEntity`
available to applicable pre-persistence hooks. Items rejected by those stages
are excluded from the subsequent `UpdateManyAsync` or `PatchManyAsync` call.

Repeated keys share the same pre-write snapshot: each occurrence is validated,
and each required patch preview is based on that value rather than on the result
of an earlier occurrence. Persistence still follows the documented repeated-key
ordering, and every successful returned occurrence represents the final value.
The snapshot lookup and mutation are separate repository calls and are not
atomic together, so the stored value can change between them. A custom adapter
can also perform additional reads or queries inside either batch method; the
guarantee is at most one `GetByIdsAsync` invocation and zero
endpoint-orchestrated per-item reads, not a fixed number of physical data-store
round trips.

The batch repository contract also defines adapter-independent item semantics:

- Create returns one result per input in input order and rejects duplicate keys
  without persisting any item. Entries are non-null. Input order is especially
  important when the repository generates keys: no pre-persistence key exists
  from which RestLib could reconstruct a reordered association.
- Update and patch skip missing keys and return matching items in relative input
  order. Repeated keys are applied in order; the last value is persisted and
  every matching result represents that final value. Each returned entity is
  identified by its resource key, so an omitted key becomes a 404 at that key's
  original request position without shifting later results.
- Delete ignores missing keys and counts each distinct deleted entity once.
- `GetByIdsAsync` omits missing keys and coalesces repeated keys in its keyed
  result.

The built-in InMemory and EF Core adapters implement these semantics. Custom
batch repositories must provide the same behavior so changing adapters does
not change endpoint outcomes.

### Bulk result contract validation

RestLib validates a bulk repository's returned result set before running
after-persist hooks or building response entities. It checks every observable
invariant needed for safe association, including null entries, cardinality,
requested resource keys, and duplicate-key multiplicity. Update and patch
results are correlated by key so their documented omission behavior is not
mistaken for positional output. Create results retain their documented
same-order contract; when every key is generated during persistence, that
ordering cannot be independently proven and remains the repository's
responsibility. Caller-supplied non-default create keys are captured before the
repository call and compared with the key returned at each position.

If a result set cannot be associated safely, RestLib does not guess, shift
entities between request positions, or run after-persist hooks against a
possibly wrong entity. Unresolved items enter the normal per-item error pipeline
and default to internal-error outcomes; an application error hook may replace
that default response. Results already produced by request validation or
pre-persistence hooks remain intact. A delete count that cannot account for the
distinct keys submitted for deletion is handled with the same unknown-outcome
rule.

Contract validation happens after the repository call may have committed.
RestLib therefore never retries a contract-violating bulk result through the
individual repository path.

### Bulk failure handling

Repository calls within `PersistBulkAsync` use an explicit bulk-persistence
boundary. When one of those calls throws, the base class preserves any
results already produced during validation or pre-persistence hooks and
reports the original exception as a per-item failure for every unresolved
item. It never retries through `PersistIndividuallyAsync`, because RestLib
cannot verify whether a custom implementation honored the atomicity contract
or performed external side effects. Applications that can prove a retry is
safe must initiate that retry according to their own repository and
idempotency guarantees.

Post-persistence processing runs outside the bulk-persistence boundary.
After-persist hooks, model mapping, HATEOAS providers, and result construction
can therefore fail after a successful repository call, but such a failure is
not classified as a persistence failure and cannot trigger another write.
Bulk result contract validation follows the same no-retry rule because an
invalid return value does not prove that persistence failed or rolled back.

Request cancellation is also outside ordinary batch failure handling. When an
`OperationCanceledException` is observed while the request token is cancelled,
RestLib propagates it immediately: bulk failures are not wrapped, individual
loops stop before the next item, and error hooks are not invoked. An
independently cancelled downstream operation remains an ordinary repository
failure when the request token itself has not been cancelled.

### Per-item hooks
Hooks fire once per item with the standard `HookContext`, using batch-specific
`RestLibOperation` values (`BatchCreate`, `BatchUpdate`, `BatchPatch`,
`BatchDelete`). This is consistent with single-entity behavior and gives
hooks full per-item control.

### Batch size limit
`RestLibOptions.MaxBatchSize` defaults to 100. Exceeding the limit returns a
400 error before any processing begins.

## Consequences
- New `IBatchRepository` interface is additive (no breaking change)
- Four new `RestLibOperation` enum values
- One new endpoint per resource with batch enabled
- Hooks fire at most N times for N accepted array members; members that cannot be
  decoded do not enter the hook pipeline

## Known Limitations

### Pre-persist validation for PATCH

When `EnableValidation` is true, both the bulk and individual PATCH paths
perform pre-persist validation: the original entity is fetched, the patch
document is preview-merged via `PatchHelper.PreviewPatch`, and data
annotations are validated on the merged result **before** calling
`PatchAsync` or `PatchManyAsync`. Items that fail validation receive a 400
error and are excluded from persistence.

When `EnableValidation` is false, no preview merge or validation occurs.
The patch document is sent directly to the repository, and any data
integrity enforcement is the responsibility of the repository
implementation.

This pre-persist validation relies on a snapshot of the entity fetched
before persistence. In a concurrent environment, the entity could change
between the fetch and the actual patch call, making the preview stale.
This is an accepted trade-off for the common non-concurrent case.

On the bulk path, one `GetByIdsAsync` call supplies that snapshot when any item
survives structural validation; no per-item existence read is made. Repeated
keys use the same fetched original for each required preview. On the individual
fallback path, each item continues to use `GetByIdAsync`. An adapter may still
perform its own internal reads while implementing `GetByIdsAsync` or
`PatchManyAsync`.
