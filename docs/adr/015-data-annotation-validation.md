# ADR-015: Data Annotation Validation

**Status:** Accepted
**Date:** 2026-04-06

## Context

REST APIs must validate incoming data before persisting it. Without built-in validation, every consumer of RestLib would need to implement their own validation logic in hooks, leading to duplicated effort and inconsistent error formats.

.NET provides `System.ComponentModel.DataAnnotations` — a standard, attribute-based validation framework that most developers already use on their entity models. RestLib needs to decide whether and how to integrate it.

## Options Considered

### Validation Framework

| Option | Pros | Cons |
| --- | --- | --- |
| **A. Data Annotations (`System.ComponentModel.DataAnnotations`)** | Built into .NET; widely understood; attribute-based (co-located with model); supports custom validators | Limited expressiveness for complex rules; requires reflection |
| B. FluentValidation | Very expressive; separation of concerns | External dependency; different paradigm from attributes; heavier |
| C. No built-in validation (hooks only) | Maximum flexibility; no opinions | Every consumer reinvents validation; inconsistent error formats |

### Validation Timing

| Option | Pros | Cons |
| --- | --- | --- |
| **A. After deserialization, before persistence** | Catches invalid data early; avoids unnecessary DB calls | Two validation points for Patch (pre-merge and post-merge) |
| B. Before deserialization | Cannot validate typed properties | Impractical |
| C. After persistence (compensating) | Simple | Invalid data may reach the database |

### Error Format

| Option | Pros | Cons |
| --- | --- | --- |
| **A. Problem Details with per-field errors** | Consistent with RestLib's existing error format (ADR-005); machine-readable | Slightly more complex response body |
| B. Simple 400 with message string | Easy to produce | Not machine-readable; inconsistent with other errors |

## Decision

### 1. Data Annotations integration (Option A)

RestLib uses `Validator.TryValidateObject()` with `validateAllProperties: true` to validate entities using standard Data Annotation attributes (`[Required]`, `[MaxLength]`, `[Range]`, `[RegularExpression]`, etc.).

```csharp
public class Product
{
    public Guid Id { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; }

    [Range(0.01, 999999.99)]
    public decimal Price { get; set; }
}
```

### 2. Validation runs on mutating operations

Validation is applied to Create, Update, Patch, and their batch equivalents. It does not run on Get or Delete operations.

The timing within the hook pipeline:

- **Create / Update** — validation runs after the `OnRequestReceived` hook and before the `OnRequestValidated` hook.
- **Patch** — validation runs after the entity is merged (i.e., after `OnRequestValidated`), because the merged preview must be validated, not the sparse patch body.

### 3. Enabled by default, globally toggleable

`RestLibOptions.EnableValidation` defaults to `true`. Setting it to `false` disables validation for all endpoints, allowing consumers to handle validation entirely in hooks if preferred.

### 4. Problem Details error format

Validation failures return HTTP 400 with Problem Details:

```json
{
    "type": "/problems/validation-failed",
    "title": "Validation Failed",
    "status": 400,
    "detail": "One or more validation errors occurred.",
    "errors": {
        "name": ["The Name field is required."],
        "price": ["The field Price must be between 0.01 and 999999.99."]
    }
}
```

Field names in the `errors` dictionary are converted using the configured `JsonNamingPolicy` (snake_case by default), ensuring consistency between error field names and response payload property names.

### 5. Field name conversion

The `EntityValidator` converts .NET property names (e.g., `CustomerEmail`) to the API's naming convention (e.g., `customer_email`) using the configured `JsonNamingPolicy`. Validation results that have no member name are grouped under the `_entity` key.

## Rationale

1. **Data Annotations are the .NET standard.** Most developers already annotate their models with `[Required]`, `[MaxLength]`, etc. Using them means zero additional learning curve and co-located validation rules.
2. **Enabled by default follows the safe-by-default principle.** Forgetting to enable validation is a common source of bugs. Consumers who want custom validation can disable it and use hooks.
3. **Problem Details consistency.** Using the same error envelope as all other RestLib errors (ADR-005) means clients need only one error-handling code path.
4. **Snake_case field names in errors.** Returning `customer_email` in errors (matching the JSON response) rather than `CustomerEmail` (the .NET property name) prevents confusion and enables client-side field matching.

## Consequences

- Entities with no Data Annotation attributes pass validation trivially — there is no cost beyond the `TryValidateObject` call.
- Custom `ValidationAttribute` subclasses work out of the box since `TryValidateObject` discovers them via reflection.
- Complex cross-property validation requires `IValidatableObject` — a standard .NET pattern that `TryValidateObject` also supports.
- Patch validation happens after merge, so a valid partial update to an entity with a `[Required]` field will not fail if the required field is already present on the existing entity.
