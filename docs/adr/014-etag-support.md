# ADR-014: ETag Support for Caching and Concurrency

**Status:** Amended
**Date:** 2026-04-06 (amended 2026-08-05)

## Context

REST APIs benefit from two HTTP mechanisms that rely on ETags (RFC 9110 Section 8.8.3):

- **Conditional requests** — `If-None-Match` allows clients to skip re-downloading a resource that has not changed (304 Not Modified), reducing bandwidth and latency.
- **Optimistic concurrency** — `If-Match` allows mutating endpoints (PUT, PATCH, DELETE) to reject stale updates, preventing lost-update problems without pessimistic locking.

RestLib needs a built-in ETag story that is opt-in (not every API needs it), pluggable (custom ETag strategies), and correctly implements the RFC 9110 strong/weak comparison semantics.

## Options Considered

### ETag Generation Strategy

| Option | Pros | Cons |
| --- | --- | --- |
| **A. SHA-256 hash of JSON-serialized entity** | Deterministic; any property change produces a new ETag; no schema changes needed | Serialization cost on every response; ties ETag to JSON options |
| B. Timestamp-based (`Last-Modified` header) | Cheap to compute | Requires a timestamp column; second-precision loses sub-second changes |
| C. Row version / concurrency token | Database-native; very cheap | Requires schema support; not available in all stores |

### ETag Scope

| Option | Pros | Cons |
| --- | --- | --- |
| **A. Per-entity on single-resource endpoints** | Clear semantics; matches RFC usage | No collection-level ETag |
| B. Per-entity + collection-level ETag | Full coverage | Collection ETags are fragile (any item change invalidates); complex to implement correctly |

### Opt-in Model

| Option | Pros | Cons |
| --- | --- | --- |
| **A. Global option (`EnableETagSupport`)** | Simple toggle; one decision per app | Cannot enable per-resource |
| B. Per-resource configuration | Fine-grained control | More configuration surface for a cross-cutting concern |

## Decision

### 1. SHA-256 hash of JSON-serialized entity (Option A)

The default `HashBasedETagGenerator` serializes the entity to UTF-8 JSON using the configured `JsonSerializerOptions`, computes a SHA-256 hash, takes the first 128 bits (16 bytes), and encodes them as base64url. The result is wrapped in quotes to produce a strong ETag per RFC 9110:

```
"abc123def456_-xY"
```

This approach is deterministic (same entity state always produces the same ETag) and requires no schema changes.

### 2. Pluggable via `IETagGenerator` interface

The `IETagGenerator` abstraction exposes two methods:

```csharp
string Generate<TEntity>(TEntity entity) where TEntity : class;
bool Validate<TEntity>(TEntity entity, string etag) where TEntity : class;
```

`HashBasedETagGenerator` is the default implementation. Consumers can replace it by registering their own `IETagGenerator` before calling `AddRestLib()`, since registration uses `TryAddSingleton`.

### 3. Global opt-in via `RestLibOptions.EnableETagSupport`

ETag support defaults to `false`. When enabled:

- **GetById** — sets the `ETag` response header; checks `If-None-Match` and returns 304 if matched (weak comparison per RFC 9110).
- **Create** — sets the `ETag` response header on the created entity.
- **Update / Patch** — sets the `ETag` response header and evaluates `If-Match` atomically with persistence. A mismatch returns 412 Precondition Failed.
- **Delete** — evaluates `If-Match` atomically with deletion and returns 412 Precondition Failed on a mismatch.
- **GetAll** — no collection-level ETag (see decision #5).

Repositories that do not implement the optional atomic conditional-write capability return
501 Conditional Write Not Supported when an `If-Match` header is supplied. They continue to
support unconditional writes and conditional GETs.

### 4. RFC 9110 comparison semantics via `ETagComparer`

A static `ETagComparer` class implements both comparison functions:

- **`IfMatchSucceeds`** — strong comparison; both ETags must be strong and byte-for-byte identical. Used by Update, Patch, and Delete.
- **`IfNoneMatchSucceeds`** — weak comparison; strips the `W/` prefix before comparing the opaque tag. Used by GetById for conditional GET.

Missing headers are treated as "precondition satisfied" (i.e., `If-Match` is optional — omitting it skips the check).

### 5. No collection-level ETags

ETags are only generated for individual entities. Collection endpoints (`GetAll`) do not produce ETags because any item insertion, update, or deletion would invalidate the collection ETag, making it almost useless with cursor pagination.

### 6. Atomic `If-Match` writes through an optional repository capability

`IConditionalWriteRepository<TEntity, TKey>` exposes conditional update, patch, and delete
operations. Each method receives a synchronous predicate over the current persisted entity and
returns `Succeeded`, `NotFound`, or `PreconditionFailed`. Implementations must evaluate the
predicate and complete a successful mutation in one indivisible persistence scope.

Endpoints may perform an earlier read to reject an already-stale request before hooks run, but
that read is only an optimization. The repository evaluates the same ETag predicate again inside
the atomic mutation; this second evaluation is authoritative and closes the check-then-write race.

The InMemory adapter evaluates the predicate and mutation under its shared mutation lock. The EF
Core adapter uses a Serializable transaction and treats `DbUpdateConcurrencyException` as a
failed precondition. If an application supplies an existing EF transaction, that transaction must
also use Serializable isolation.

Mapped resources generate and compare the ETag from the mapped API model while the atomic
capability receives the current database model. This preserves the public representation contract
without exposing persistence-only fields through the validator.

## Rationale

1. **SHA-256 of JSON is universally applicable for generation.** It works with any entity type and requires no schema support. Atomic mutation still requires repository participation because an endpoint-only comparison cannot prevent a lost update.
2. **Strong ETags are the safe default.** Strong ETags satisfy both `If-Match` (strong comparison) and `If-None-Match` (weak comparison), so a single ETag value works for both use cases.
3. **Opt-in avoids unnecessary overhead.** Not every API needs caching or concurrency control. The serialization and hashing cost is only incurred when explicitly enabled.
4. **`TryAddSingleton` enables replacement.** Applications with database-native concurrency tokens (e.g., SQL Server `rowversion`) can register a custom `IETagGenerator` that uses the token directly, avoiding the serialization cost entirely.

## Consequences

- Every response from GetById, Create, Update, and Patch includes an `ETag` header when enabled, increasing response size by ~30 bytes.
- Each single-resource response incurs one JSON serialization + SHA-256 hash. For GetAll, each entity in the collection gets an ETag in its response envelope.
- Custom repositories must implement `IConditionalWriteRepository<TEntity, TKey>` to accept `If-Match` on write endpoints. Without it, RestLib returns a 501 Problem Details response instead of claiming unsafe optimistic-concurrency protection.
- Clients must send `If-Match` headers for optimistic concurrency; without them, updates proceed unconditionally.
- The `ETagComparer` correctly handles comma-separated ETag lists and wildcard (`*`) values per RFC 9110.
