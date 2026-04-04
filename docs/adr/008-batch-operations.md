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

### Optional IBatchRepository
`IBatchRepository<TEntity, TKey>` is an optional interface with batch-optimized
methods (`CreateManyAsync`, `UpdateManyAsync`, `DeleteManyAsync`). When the
repository implements it and no hooks are configured, RestLib uses the batch
methods for better performance. Otherwise, it falls back to looping over
`IRepository` methods. This avoids breaking existing repository implementations.

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
- Patch always processes items individually (no bulk path) due to per-item
  merge logic

## Known Limitations

### Post-persist validation for PATCH

Data annotation validation for batch PATCH runs **after** persistence because
the merged entity is only available once the repository has applied the patch
document. This mirrors single-item PATCH behavior and is by-design, but it
means that invalid data may be persisted with no automatic rollback.

The bulk `IBatchRepository.PatchManyAsync()` path amplifies the risk: all items
are persisted in a single call, then validation runs per-item. If a repository
needs transactional rollback semantics, it should implement compensating logic
inside `PatchManyAsync()` or avoid exposing the bulk path for PATCH operations.

This limitation is documented in `BatchActionValidator.cs` (see
`ValidatePatchItemAsync`) and in `BatchActionExecutor.cs` (see
`ExecutePatchesAsync`).
