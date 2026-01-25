# ADR-002: Secure by Default

**Status:** Accepted  
**Date:** 2026-01-25

## Context

CRUD scaffolding libraries often generate public endpoints by default, leaving security as an afterthought. This pattern leads to accidental data exposure, especially during rapid development when developers may forget to add authorization before deployment.

RestLib needs a security posture that balances developer experience with safe defaults.

## Options Considered

| Option                   | Pros                                               | Cons                                                   |
| ------------------------ | -------------------------------------------------- | ------------------------------------------------------ |
| Anonymous by default     | Faster prototyping, less initial friction          | Security risk, easy to forget before production        |
| Authenticated by default | Secure out of the box, forces deliberate decisions | Extra step for truly public APIs, more setup for demos |

## Decision

**All endpoints require authorization by default.** Developers must explicitly opt-out using `AllowAnonymous()`.

```csharp
// Endpoints are protected by default
app.MapRestLib<Product, Guid>("/api/products");

// Explicit opt-out for public endpoints
app.MapRestLib<Product, Guid>("/api/products", config =>
{
    config.AllowAnonymous(RestLibOperation.GetAll, RestLibOperation.GetById);
});
```

## Rationale

1. **Pit of Success:** The secure path should be the default path — developers shouldn't need to remember to "turn on" security
2. **Explicit intent:** Making an endpoint public requires a deliberate action, creating an audit trail of decisions
3. **Compliance:** Many organizations require authenticated APIs; starting secure meets this requirement by default
4. **Zalando Rules 104-105:** Recommend securing endpoints and using standard authentication mechanisms
5. **Real-world alignment:** Production APIs are almost always authenticated; anonymous access is the exception

## Consequences

- **Developers must configure authentication** before endpoints work in production
- **Sample apps need explicit `AllowAnonymous()`** or authentication setup to function
- **Documentation must clearly explain** the opt-out mechanism
- **401 Unauthorized** is returned for unauthenticated requests by default
- **Granular control** is provided — different operations can have different policies

## Example

```csharp
app.MapRestLib<Product, Guid>("/api/products", config =>
{
    // Read operations are public
    config.AllowAnonymous(RestLibOperation.GetAll, RestLibOperation.GetById);

    // Write operations require authentication (default)
    // Delete requires admin role
    config.RequirePolicy(RestLibOperation.Delete, "AdminOnly");
});
```

## References

- [OWASP Secure by Default](https://owasp.org/www-project-proactive-controls/)
- [Zalando RESTful API Guidelines - Security](https://opensource.zalando.com/restful-api-guidelines/#security)
