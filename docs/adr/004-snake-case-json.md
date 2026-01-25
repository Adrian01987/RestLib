# ADR-004: snake_case JSON Naming

**Status:** Accepted  
**Date:** 2026-01-25

## Context

JSON property naming conventions vary across ecosystems:

- `camelCase`: JavaScript/TypeScript default, .NET's `System.Text.Json` default
- `PascalCase`: C# property naming convention
- `snake_case`: Python, Ruby, many REST APIs (GitHub, Stripe, Slack)

RestLib needs a consistent JSON naming strategy for request/response serialization.

## Options Considered

| Option     | Pros                                                 | Cons                                              |
| ---------- | ---------------------------------------------------- | ------------------------------------------------- |
| camelCase  | .NET default, JavaScript-friendly, no configuration  | Not Zalando-compliant, less readable for some     |
| PascalCase | Matches C# exactly, no mapping needed                | Uncommon in REST APIs, not Zalando-compliant      |
| snake_case | Zalando-compliant, widely adopted, arguably readable | Requires custom JsonNamingPolicy, differs from C# |

## Decision

Use **snake_case** for all JSON property names.

```json
{
  "id": "550e8400-e29b-41d4-a716-446655440000",
  "product_name": "Wireless Headphones",
  "unit_price": 149.99,
  "created_at": "2026-01-25T10:30:00Z",
  "is_active": true
}
```

## Rationale

1. **Zalando Rule 118** mandates snake_case for property names
2. **Industry adoption:** Major APIs use snake_case:
   - GitHub REST API
   - Stripe API
   - Slack API
   - Twitter API
3. **Readability:** `created_at` is arguably clearer than `createdAt`, especially for non-developers
4. **Consistency:** Following an established standard reduces bikeshedding
5. **Tooling:** Many API testing tools and client generators handle snake_case well

## Consequences

- **Requires custom `JsonNamingPolicy`** — we use `JsonNamingPolicy.SnakeCaseLower`
- **C# properties differ from JSON** — `CreatedAt` in C# becomes `created_at` in JSON
- **Documentation must show both formats** — examples should include C# models and JSON output
- **Client code generation** may need configuration to map correctly
- **Nulls are omitted** from responses to reduce payload size (related decision)

## Implementation

```csharp
var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true // Accept any casing on input
};
```

## Example Mapping

| C# Property   | JSON Property  |
| ------------- | -------------- |
| `Id`          | `id`           |
| `ProductName` | `product_name` |
| `UnitPrice`   | `unit_price`   |
| `CreatedAt`   | `created_at`   |
| `IsActive`    | `is_active`    |
| `OrderItems`  | `order_items`  |

## References

- [Zalando RESTful API Guidelines - Rule 118](https://opensource.zalando.com/restful-api-guidelines/#118)
- [Google JSON Style Guide](https://google.github.io/styleguide/jsoncstyleguide.xml) (uses camelCase, for contrast)
- [System.Text.Json Naming Policies](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/customize-properties#use-a-built-in-naming-policy)
