# ADR-005: RFC 9457 Problem Details for Errors

**Status:** Accepted  
**Date:** 2026-01-25

## Context

Error responses in REST APIs need a consistent, machine-readable format. Options include:

- **Custom error objects:** Flexible but inconsistent across projects
- **Plain text messages:** Simple but not machine-parseable
- **RFC 9457 Problem Details:** Industry standard with defined structure

## Options Considered

| Option                   | Pros                                            | Cons                                     |
| ------------------------ | ----------------------------------------------- | ---------------------------------------- |
| Custom error format      | Full control, can match existing patterns       | Inconsistent, requires documentation     |
| Plain text               | Simple, human-readable                          | Not machine-parseable, no structure      |
| RFC 9457 Problem Details | Industry standard, machine-readable, extensible | Slightly more verbose, requires adoption |

## Decision

**All error responses use RFC 9457 Problem Details format.**

```json
{
  "type": "/problems/not-found",
  "title": "Resource Not Found",
  "status": 404,
  "detail": "Product with ID '550e8400-e29b-41d4-a716-446655440000' does not exist.",
  "instance": "/api/products/550e8400-e29b-41d4-a716-446655440000"
}
```

## Rationale

1. **Industry standard:** Adopted by Microsoft, AWS, Azure, and many cloud providers
2. **Machine-readable:** Structured fields (`type`, `title`, `status`, `detail`) enable automated error handling
3. **Extensible:** Custom fields can be added for domain-specific information
4. **ASP.NET Core support:** Built-in `ProblemDetails` class with full serialization support
5. **Zalando Rule 176:** Recommends using Problem Details for error responses
6. **Content negotiation:** Uses `application/problem+json` media type for clear identification

## Consequences

- **All 4xx and 5xx responses** return `application/problem+json` content type
- **Problem type URIs** are relative paths (e.g., `/problems/not-found`)
- **Validation errors** use the `errors` extension property for field-level details
- **Clients can programmatically handle** errors based on `type` field
- **Consistent error experience** across all RestLib endpoints

## Error Types

| HTTP Status | Type                            | Title                 |
| ----------- | ------------------------------- | --------------------- |
| 400         | `/problems/bad-request`         | Bad Request           |
| 400         | `/problems/validation-failed`   | Validation Failed     |
| 401         | `/problems/unauthorized`        | Unauthorized          |
| 403         | `/problems/forbidden`           | Forbidden             |
| 404         | `/problems/not-found`           | Resource Not Found    |
| 409         | `/problems/conflict`            | Resource Conflict     |
| 412         | `/problems/precondition-failed` | Precondition Failed   |
| 500         | `/problems/internal-error`      | Internal Server Error |

## Validation Error Example

```json
{
  "type": "/problems/validation-failed",
  "title": "Validation Failed",
  "status": 400,
  "detail": "One or more validation errors occurred.",
  "instance": "/api/products",
  "errors": {
    "name": ["The Name field is required."],
    "price": ["The Price field must be greater than 0."]
  }
}
```

## Implementation

```csharp
public static class RestLibProblemDetails
{
    public static ProblemDetails NotFound(string entityName, object id, string instance)
        => new()
        {
            Type = "/problems/not-found",
            Title = "Resource Not Found",
            Status = 404,
            Detail = $"{entityName} with ID '{id}' does not exist.",
            Instance = instance
        };
}
```

## References

- [RFC 9457 - Problem Details for HTTP APIs](https://www.rfc-editor.org/rfc/rfc9457)
- [Zalando RESTful API Guidelines - Rule 176](https://opensource.zalando.com/restful-api-guidelines/#176)
- [Microsoft ProblemDetails Class](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.mvc.problemdetails)
