# ADR-004: snake_case JSON Naming

**Status:** Amended
**Date:** 2026-01-25 (amended 2026-08-05)

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

Use **snake_case** by default for all JSON property names.

```json
{
  "id": "550e8400-e29b-41d4-a716-446655440000",
  "product_name": "Wireless Headphones",
  "unit_price": 149.99,
  "created_at": "2026-01-25T10:30:00Z",
  "is_active": true
}
```

ASP.NET Core's `JsonOptions.SerializerOptions` is the single canonical serializer
instance for a normally registered RestLib application. RestLib applies its defaults to
that instance and exposes the same object through dependency injection. Minimal API request
binding and response writing, core PATCH preview, field projection, the standard InMemory
and EF Core adapters, and the default ETag generator therefore interpret one effective
`System.Text.Json` contract.

Applications extend or override those defaults with `ConfigureHttpJsonOptions`. Configured
naming and case policies, `TypeInfoResolver` metadata, converters, ignore rules, and number
handling flow to every standard RestLib serialization path. RestLib makes the finalized
instance read-only when it is resolved, so configuration must be completed during service
registration.

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
builder.Services.AddRestLib(options =>
{
    options.JsonNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.OmitNullValues = true;
});

builder.Services.ConfigureHttpJsonOptions(httpJson =>
{
    // Optional application-wide additions to the same serializer instance.
    httpJson.SerializerOptions.NumberHandling = JsonNumberHandling.AllowReadingFromString;
    httpJson.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
});
```

Call `ConfigureHttpJsonOptions` after `AddRestLib` when overriding a RestLib default, because
.NET applies options configuration delegates in registration order. Standard
`AddRestLibInMemory` registrations resolve the canonical serializer lazily and therefore may
be registered before or after `AddRestLib`. `AddRestLibInMemoryWithOptions` and
`AddRestLibInMemoryWithDataAndOptions` deliberately retain their explicit, repository-local
serializer override.

Internal JSON member metadata is derived from the canonical instance's `JsonTypeInfo`, not
re-created separately by PATCH or field-selection code. Metadata caches are scoped by exact
`JsonSerializerOptions` object identity; two instances using the same naming-policy type can
therefore never share stale resolver or converter state.

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
- [ASP.NET Core HTTP JSON options](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.dependencyinjection.httpjsonserviceextensions.configurehttpjsonoptions)
