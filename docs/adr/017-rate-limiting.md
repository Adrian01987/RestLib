# ADR-017: Rate Limiting Integration

**Status:** Accepted
**Date:** 2026-04-06

## Context

Rate limiting is essential for production REST APIs to prevent abuse, ensure fair usage, and protect downstream resources. ASP.NET Core 7+ includes a built-in rate limiting middleware (`Microsoft.AspNetCore.RateLimiting`) with configurable policies (fixed window, sliding window, token bucket, concurrency).

RestLib needs to decide how to integrate rate limiting: build a custom implementation, wrap the built-in middleware, or simply delegate to it.

## Options Considered

### Integration Strategy

| Option | Pros | Cons |
| --- | --- | --- |
| **A. Pure delegation to ASP.NET Core rate limiting** | Zero custom code for enforcement; inherits all built-in algorithms; 429 responses handled by middleware | Requires consumer to configure `AddRateLimiter()` separately; 429 responses use ASP.NET Core's format, not Problem Details |
| B. Custom rate limiting implementation | Full control over behavior and error format | Reinvents the wheel; maintenance burden; unlikely to match ASP.NET Core's battle-tested implementation |
| C. Wrapper with Problem Details 429 | Consistent error format | Complex to intercept middleware responses; middleware runs before endpoint logic |

### Configuration Granularity

| Option | Pros | Cons |
| --- | --- | --- |
| **A. Three tiers: default, per-operation, disable** | Covers all practical scenarios; simple mental model | Slightly more API surface than a single policy |
| B. Single policy per resource | Simple | Cannot differentiate read vs. write operations |
| C. Per-endpoint (route-level) | Maximum flexibility | Too granular; breaks the resource abstraction |

## Decision

### 1. Pure delegation to ASP.NET Core rate limiting (Option A)

RestLib applies rate limiting by calling ASP.NET Core's `RequireRateLimiting(policyName)` or `DisableRateLimiting()` on each endpoint's `RouteHandlerBuilder`. RestLib does not implement any rate limiting algorithm — it only wires policies to endpoints.

Consumers must configure rate limiting policies using ASP.NET Core's `AddRateLimiter()`:

```csharp
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("standard", opt =>
    {
        opt.PermitLimit = 100;
        opt.Window = TimeSpan.FromMinutes(1);
    });
});
```

### 2. Three-tier configuration (Option A)

Rate limiting is configured at three levels with clear precedence:

```
Disable > Per-operation > Default > None
```

```csharp
config.UseRateLimiting("standard");                          // Default for all operations
config.UseRateLimiting("strict", RestLibOperation.Create);   // Override for Create
config.DisableRateLimiting(RestLibOperation.GetById);        // Exempt GetById
```

| Level | Method | Effect |
| --- | --- | --- |
| Default | `UseRateLimiting(policy)` | Applied to all operations unless overridden |
| Per-operation | `UseRateLimiting(policy, operations...)` | Overrides default for specific operations |
| Disable | `DisableRateLimiting(operations...)` | Exempts operations entirely (highest precedence) |

### 3. 429 responses handled by ASP.NET Core middleware

When a request is rate-limited, ASP.NET Core's middleware returns a `429 Too Many Requests` response with `Retry-After` header. RestLib does not intercept or wrap this response in Problem Details, because:

- The middleware runs before endpoint logic, so RestLib's error handling pipeline is never reached.
- The `Retry-After` header is the critical piece of information for clients, and it is already included.
- Consistency with non-RestLib endpoints in the same application.

### 4. JSON configuration support

Rate limiting can also be configured via JSON (see ADR-016):

```json
{
    "RateLimiting": {
        "Default": "standard",
        "ByOperation": {
            "Create": "strict",
            "Update": "strict"
        },
        "Disabled": ["GetById"]
    }
}
```

## Rationale

1. **Delegation avoids reinventing the wheel.** ASP.NET Core's rate limiting middleware is mature, well-tested, and supports four algorithms out of the box. Wrapping or replacing it would add complexity with no benefit.
2. **Three-tier precedence is intuitive.** "Apply this policy everywhere, except use a stricter one for writes, and exempt health-check-like reads" covers the vast majority of real-world scenarios.
3. **Policy names decouple configuration from definition.** RestLib references policies by name, so the algorithm and parameters can change without touching endpoint configuration.
4. **No Problem Details for 429 is pragmatic.** The middleware runs before routing, so intercepting its response would require custom middleware — adding complexity for minimal benefit since `Retry-After` is already the standard signal.

## Consequences

- Consumers must call `AddRateLimiter()` and `UseRateLimiter()` in their ASP.NET Core pipeline. RestLib does not register the middleware — only the per-endpoint policies.
- If a policy name referenced in RestLib configuration does not exist in the rate limiter, ASP.NET Core will throw at request time (not startup).
- 429 responses do not use RestLib's Problem Details format. Clients must handle both Problem Details (for RestLib errors) and plain 429 (for rate limiting).
- Rate limiting applies to all requests uniformly. Per-user or per-tenant rate limiting requires custom `RateLimiterPolicy` implementations in the consumer's code.
