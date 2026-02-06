# 006. Operation Selection

Date: 2026-02-06
Status: Accepted

## Context

By default, RestLib registers all 6 standard REST operations (GetAll, GetById, Create, Update, Patch, Delete) for every endpoint.
Developers often need more granular control:

1. **Read-only APIs** (e.g., lookup tables, audit logs).
2. **Partial exposure** (e.g., delete is not allowed).
3. **Custom implementation** (e.g., standard GETs but complex business logic for POST).

Without a mechanism to selectively enable operations, developers would either:

- Expose unwanted endpoints.
- Be unable to mix custom logic with RestLib conveniece.
- Be forced to abandon RestLib for simple variations.

## Decision

We introduce two mutually exclusive configuration methods: `IncludeOperations` (Allowlist) and `ExcludeOperations` (Denylist).

### 1. Configuration API

Developers can choose the strategy that best fits their scenario:

**Scenario A: Allowlist (Recommended for restrictive APIs)**

```csharp
app.MapRestLib<Product, Guid>("/api/products", config =>
{
    // Only explicitly listed operations are enabled
    config.IncludeOperations(RestLibOperation.GetAll, RestLibOperation.GetById);
});
```

**Scenario B: Denylist (Recommended for standard APIs with minor exceptions)**

```csharp
app.MapRestLib<Product, Guid>("/api/products", config =>
{
    // All operations enabled EXCEPT these
    config.ExcludeOperations(RestLibOperation.Delete);
});
```

### 2. Design Choices

#### A. Dual Strategy with Mutual Exclusion

We support both approaches to balance **explicitness** (allowlist) with **convenience** (denylist). However, to prevent ambiguity, **using both in the same configuration is forbidden** and will throw an `InvalidOperationException`.

#### B. Behavior Merging

- Multiple calls to `IncludeOperations` are **merged (unioned)**.
- Multiple calls to `ExcludeOperations` are **merged (unioned)**.

#### C. Default Behavior

To maintain backward compatibility, if neither method is called, **all operations are enabled**.

## Consequences

### Positive

- **Flexibility**: Developers can choose the most concise syntax for their intent.
- **Safety**: Allowlist minimizes attack surface; Denylist minimizes boilerplate.
- **Clarity**: Mutual exclusion prevents confusing configurations (e.g., including "All" but excluding "Delete").

### Negative

- **API Surface**: Two methods instead of one increases API surface slightly.
- **Runtime Validation**: We must check for conflicting configuration at startup.
