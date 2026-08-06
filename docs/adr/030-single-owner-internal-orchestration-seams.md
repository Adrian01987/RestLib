# ADR-030: Single-Owner Internal Orchestration Seams

**Status:** Accepted
**Date:** 2026-08-06

## Context

RestLib supports single-model and two-model resources, multiple repository
adapters, optional query features, hooks, batch persistence, and configurable
Problem Details responses. As those capabilities were added, several paths
evolved in parallel:

- mapped and unmapped batch pipelines repeated the same state transitions;
- mapped and unmapped create, get-by-id, update, and delete handlers repeated
  operation-specific state machines around a small representation difference;
- mapped and unmapped collection endpoints repeated query validation and
  planning;
- `EfCoreRepository` owned resource-key metadata, query composition, cursor
  pagination, projection decisions, PATCH planning, and persistence details in
  one large class;
- sorting and field-selection parsers repeated the same configured comma-list
  mechanics; and
- Problem Details metadata and endpoint response settings were repeated across
  factories and result wrappers.

The implementations had extensive behavioral coverage, but each duplicated
decision was another place where cancellation, error handling, result ordering,
serializer settings, or mapped-model behavior could drift. The refactor must
reduce those maintenance seams without changing RestLib's public APIs, HTTP
contracts, or adapter-neutral boundaries.

## Decision

### One batch state machine and one HTTP coordinator

`BatchActionPipelineBase<TKey, TRawItem, TValidItem, TContext>` owns the common
batch state machine: cancellation checks, request-item deserialization,
per-item validation, stable result slots, bulk-versus-individual execution,
and shared failure handling. `BatchPipelineContext<TKey>` contains the
transport and execution state that every batch path needs. The mapped and
unmapped pipelines remain thin adapters for model-specific validation, mapping,
hooks, persistence calls, HATEOAS, and response entities.

`BatchHandler` owns the HTTP boundary once: envelope parsing, action selection,
action-aware authorization, request limits, dispatch, before-response hooks,
aggregate status selection, and serialization. Its private batch-request
processor boundary chooses the mapped or unmapped adapter without duplicating
the HTTP protocol.

`BulkPersistenceExecutor` marks only the bulk repository call. This boundary is
important because a repository failure may occur after a write was committed,
while a later mapping or hook failure is a post-persistence failure. The shared
pipeline therefore preserves the existing no-retry rule whenever replaying a
bulk operation could duplicate a write.

### One collection-query coordinator

`CollectionQueryCoordinator` validates cursor and limit values, then parses
filtering, sorting, field selection, and search in the established HTTP
contract order. It returns either the first validation response or a
`CollectionQueryPlan` containing the repository request and response-projection
settings.

Both mapped and unmapped collection endpoints use this coordinator. They still
own their model-specific repository capability checks, mapping, hooks, ETags,
HATEOAS, and response projection, but they no longer maintain parallel copies
of query validation and pagination-default logic. Query input remains fully
validated before any repository call.

### One executor per compatible CRUD operation

`EndpointModelAdapter<TApiModel, TDbModel>` defines the representation boundary
for endpoint state machines. A one-model resource receives an identity adapter;
a two-model resource receives its configured mapper.
`EndpointModelState<TApiModel, TDbModel>` keeps the current API and persistence
representations explicit as hooks and persistence operations replace them.

Create, get-by-id, update, and delete each have one operation-specific generic
executor for their mapped and unmapped paths. Their delegate factories resolve
the repository, mapper, and API- or DB-model hook pipeline, then select the
identity or mapped adapter before entering that executor. The shared executor
owns the operation order, cancellation and error boundary, authoritative route
key, persistence call, hook replacement propagation, and final response
handling. Mapping remains at the representation boundary rather than leaking
into repository contracts. Identity mode preserves the existing one-model
projection capability and skips the mapped path's additional API-model
revalidation after hook replacements; those are explicit branches inside the
shared executor, not separate orchestration pipelines.

PATCH is an intentional exception. A one-model PATCH passes the partial
document to the repository's native `PatchAsync` implementation, including
adapter-native strict-document rejection and tracked-state rollback
guarantees, while a two-model PATCH must preview and validate the API
representation and then persist a full mapped update because the mapper does
not translate partial documents. Combining those persistence state machines
would hide a real semantic difference. PATCH still uses the shared response
and supporting infrastructure, but it keeps separate mapped and unmapped
orchestration until a future contract can represent both behaviors honestly.
This decision therefore does not claim that every endpoint handler has one
universal executor.

### Focused EF Core collaborators behind the repository facade

`EfCoreRepository<TEntity, TKey>` remains the implementation of RestLib's
public repository capability interfaces and the owner of the scoped
`DbContext`. It delegates cohesive internal decisions to four collaborators:

- `EfCoreKeyMetadata<TEntity, TKey>` resolves implicit or explicit EF resource
  keys, including alternate and two-part composite keys, and owns their
  accessors, stable sort tie-breakers, lookup predicates, value binding,
  primary-key preservation, and route-key assignment;
- `EfCorePageQueryExecutor<TEntity>` applies search and filters, chooses a safe
  keyset plan or the documented offset fallback, materializes `limit + 1`, and
  issues the next cursor;
- `EfCoreProjectionPlanner<TEntity>` decides whether scalar projection
  pushdown is safe and resolves navigation includes for the materialized
  fallback; and
- `EfCorePatchPlanner<TEntity>` builds mutation-free PATCH plans, applies them
  while recording tracked state, and restores that state when a rejected or
  failed operation must not leak mutations.

These collaborators are internal implementation details. The repository
interfaces, registration API, cursor format contract, projection capability,
PATCH semantics, and exception boundaries are unchanged.

### One configured comma-list parser

`ConfiguredQueryListParser` owns the mechanics shared by sorting and field
selection: splitting comma-separated input, ignoring empty segments, resolving
configured names case-insensitively, producing unknown-field diagnostics, and
detecting duplicates by canonical configured name. `SortParser` and
`FieldSelectionParser` provide only their feature-specific tokenization,
validation, result types, and error messages.

The shared parser is deliberately internal. It does not impose one public
grammar on every query feature, and it preserves each feature's existing
validation order and response contract.

### One Problem Details catalog and response pipeline

`ProblemCatalog` owns the invariant type, title, status, and default detail for
each built-in problem. `ProblemDetailsFactory` adds occurrence-specific details,
validation errors, and extensions. `ProblemDetailsResponder` binds endpoint
JSON settings, logging, and RestLib options to the single
`ProblemDetailsResult.Create` result-writing path.

The existing public `ProblemDetailsFactory` and `ProblemDetailsResult` methods
remain compatibility facades. Their signatures, media type, logging behavior,
serializer behavior, configurable problem-type base URI, and
`UseProblemDetails` fallback remain unchanged.

### Structural ownership is separate from caching

This decision establishes one owner for each orchestration policy; it is not a
caching or throughput optimization. The collaborators do not introduce global
metadata caches, compiled-query caches, or new service lifetimes. Cache keys,
thread safety, memory bounds, and measured performance remain the separate
scope of Q-16. A future Q-16 change may cache work inside these owners, but it
must not duplicate their policies or change the contracts recorded here.

Large public configuration builders remain coherent compatibility facades.
They should delegate to focused internal owners when another behavior is
extracted, rather than being split solely to reduce line counts or forcing a
public fluent-API redesign.

## Alternatives Considered

### Split large classes into partial files only

Rejected because partial files reduce visual size without removing duplicated
decisions or establishing a single behavioral owner.

### Keep compatible mapped and unmapped implementations parallel

Rejected for batch and compatible CRUD operations because mapping differences
occur at well-defined boundaries. Keeping two complete orchestration paths
would continue to require every correctness fix to be applied and tested
twice. PATCH retains separate paths because its repository operations are not
equivalent.

### Expose the new collaborators as public extension points

Rejected because these types coordinate existing contracts rather than define
new capabilities. Making them public would enlarge the compatibility surface
without giving repository or application authors a stable abstraction they
need to implement.

### Combine this refactor with caching and compiled-query work

Rejected because structural equivalence can be verified against existing
behavior, while caching introduces independent lifetime, invalidation,
thread-safety, and performance trade-offs. Those concerns belong to Q-16 and
require their own evidence.

## Consequences

- Mapped and unmapped batch requests now traverse the same state machine and
  HTTP coordinator, reducing drift in ordering, cancellation, and failure
  semantics.
- Collection-query validation has one contract order and one pagination-plan
  builder before adapter or mapping decisions begin.
- Create, get-by-id, update, and delete each have one state machine across
  identity and mapped representations; PATCH keeps its distinct native-patch
  and full-mapped-update persistence semantics.
- EF Core key metadata, pagination, projection, and PATCH rules can be tested
  independently while `EfCoreRepository` remains the stable adapter facade.
- Sorting and field selection retain distinct grammars and errors while sharing
  their configured-list mechanics.
- Built-in Problem Details metadata and endpoint response settings each have a
  single owner, while all existing public compatibility methods remain.
- The internal abstractions add a small amount of indirection and generic
  plumbing, so behavioral parity tests across mapped/unmapped and adapter paths
  remain required.
- This ADR makes no performance claim. Any caching or compiled-expression work
  must be measured and decided under Q-16.
