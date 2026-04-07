# ADR-008: Batch Operations

**Status:** Accepted
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

### Partial success semantics
Each item is processed independently. The response uses 200 when all items
succeed, 207 Multi-Status when results are mixed. Each item in the response
carries its own `status`, `entity` (on success), and `error` (on failure).
This is more practical than all-or-nothing for large imports.

### Non-transactional processing

Batch operations are **non-transactional by design**. There is no rollback
mechanism at the RestLib level. The specific behaviour depends on which
persistence path is used:

**Individual path** (`PersistIndividuallyAsync`): Each item is persisted in
its own `try`/`catch`. If item 3 of 5 throws, items 1-2 are already
persisted, item 3 gets a 500 error result, and items 4-5 continue
processing normally. No previously-persisted items are rolled back.

**Bulk path** (`PersistBulkAsync`): All validated items are passed to a
single bulk repository method (e.g. `CreateManyAsync`, `UpdateManyAsync`,
`PatchManyAsync`, `DeleteManyAsync`). If the bulk method throws, RestLib
falls back to the individual path for every item whose result slot is still
empty (see "Bulk-to-individual fallback" below). Whether the bulk method
itself is transactional depends entirely on the repository implementation —
RestLib does not impose or assume any transactional guarantee.

Consumers who need all-or-nothing semantics should implement transactional
logic inside their repository (e.g. wrapping bulk methods in a database
transaction that rolls back on failure).

### Optional IBatchRepository
`IBatchRepository<TEntity, TKey>` is an optional interface with batch-optimized
methods (`CreateManyAsync`, `UpdateManyAsync`, `PatchManyAsync`,
`DeleteManyAsync`, `GetByIdsAsync`). When the repository implements it,
RestLib uses the batch methods for better performance. Otherwise, it falls
back to looping over `IRepository` methods. This avoids breaking existing
repository implementations.

### Bulk-to-individual fallback

When a `PersistBulkAsync` override throws, the base class catches the
exception and retries via `PersistIndividuallyAsync`. Only items whose
result slot is still `null` are retried — some subclasses (e.g.
`BatchPatchPipeline`) may populate result slots during pre-validation
inside `PersistBulkAsync` before the actual bulk call, so those items are
not retried. This ensures every item receives a per-item result even when
the bulk path fails, at the cost of falling back to N individual calls.

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
- Hooks fire N times for N items (may be slow for very large batches)

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

Implementation: `BatchPatchPipeline.PersistBulkAsync` (bulk path with
`GetByIdsAsync`) and `BatchPatchPipeline.PersistSingleItemAsync`
(individual path with `GetByIdAsync`).
