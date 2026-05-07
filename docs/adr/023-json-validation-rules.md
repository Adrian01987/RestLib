# ADR-023: JSON validation rules

**Status:** Accepted
**Date:** 2026-05-06

## Context

RestLib already validates entities through Data Annotations on CLR types. Epic A adds a declarative JSON resource story, and some teams need resource-specific validation that supplements the model attributes without moving behavior out of strongly typed C#.

## Decision

`RestLibJsonResourceConfiguration` now supports a `Validation` section keyed by CLR property name.

Supported rules:

- `Required`
- `Min`
- `Max`
- `Length.Min`
- `Length.Max`
- `Pattern`
- `Email`

Example:

```json
"Validation": {
  "Name": {
    "Required": true,
    "Length": { "Max": 200 }
  },
  "Price": {
    "Min": 0.01
  }
}
```

These rules supplement Data Annotation validation instead of replacing it.

- Data Annotations run first.
- JSON validation runs second.
- Errors are merged into the existing RFC 9457 validation Problem Details response.
- Duplicate messages are removed while preserving first-seen order.

Invalid JSON validation configuration fails at startup. RestLib validates the configured property names, rule/property type compatibility, numeric and length ranges, and regex syntax before endpoints are mapped.

Hooks remain imperative C# behavior. Validation still blocks persistence before `BeforePersist` runs for create/update and before patch persistence for patch/batch patch.

## Consequences

- A single CLR type can now be exposed by multiple resources with different declarative validation rules.
- JSON validation remains adapter-neutral because it runs in the core request pipeline, not inside repository implementations.
- Resource JSON still uses CLR property names, so property renames require updating configuration files.
- Custom validators and cross-property rules stay in C# through Data Annotations, `IValidatableObject`, or hooks.
