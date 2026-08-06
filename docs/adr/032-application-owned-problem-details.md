# ADR-032: Domain Problem Details Are Application-Owned

**Status:** Accepted
**Date:** 2026-08-06

## Context

RestLib standardizes failures produced by its generic REST infrastructure: query
validation, missing resources, batch validation, conditional writes, hook
short-circuits, and unexpected endpoint failures. The core package also exposed
`InsufficientStock` and `InvalidStatusTransition` constants, factories, and
result helpers, even though those conditions came from the ecommerce sample.

That mixed two ownership boundaries. A reusable repository library had to carry
application vocabulary and compatibility obligations, while every additional
business failure encouraged another core constant/factory/result trio.

RestLib already exposes the domain-neutral pieces needed by applications:
`RestLibProblemDetails` represents an RFC 9457 occurrence and
`ProblemDetailsResult.Create` writes it as an `IResult` with the
`application/problem+json` media type.

## Decision

RestLib owns only Problem Details types produced by generic RestLib behavior.
Those built-in definitions remain in the internal data-driven `ProblemCatalog`,
with public convenience methods in `ProblemDetailsFactory` and
`ProblemDetailsResult` where they are part of the library contract.

Applications own business-domain problem type URIs, titles, details, and
extension members. They construct a `RestLibProblemDetails` occurrence and pass
it to `ProblemDetailsResult.Create`. An application may keep its own internal
descriptor catalog when several call sites share the same domain failures, but
that catalog is not a RestLib abstraction.

The ecommerce sample therefore owns its insufficient-stock, invalid-transition,
and payment problem descriptors behind one sample-local result helper. Existing
HTTP payloads keep their relative type URIs and fields; only their source-code
ownership changes.

## Example

```csharp
var problem = new RestLibProblemDetails
{
    Type = "/problems/order-locked",
    Title = "Order Locked",
    Status = StatusCodes.Status409Conflict,
    Detail = "The order can no longer be changed.",
    Instance = httpContext.Request.Path.ToString(),
};

return ProblemDetailsResult.Create(problem);
```

Applications that need an absolute domain problem URI set `Type` accordingly.
They can pass their `JsonSerializerOptions` and `ILogger` to `Create`; RestLib's
`ProblemTypeBaseUri` continues to apply to RestLib-owned endpoint failures.

## Consequences

- The core package no longer grows when an application adds a business failure.
- RestLib's built-in catalog contains only adapter-neutral REST infrastructure
  failures.
- Samples demonstrate the same generic extension seam available to consumers.
- Removing the former ecommerce constants and convenience methods is a source-
  and binary-breaking API change and must ship in the next major version.
- The ecommerce sample's wire contracts remain stable even though the helpers
  moved out of the package.
