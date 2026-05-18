# RestLib Minimal Sample

This sample is the small first-run project for RestLib. It is intentionally
kept compact so a new evaluator can run the repository, open Scalar, and see
generated endpoints without first learning the ecommerce workflow.

For the comprehensive reference application, use the
[ecommerce sample](../RestLib.Sample.Ecommerce/README.md). That sample shows
how RestLib features compose across authentication, authorization, EF Core,
InMemory repositories, JSON resources, hooks, HATEOAS, ETags, rate limiting,
custom workflow endpoints, and end-to-end role flows.

## Run

From the repository root:

```powershell
dotnet run --project samples/RestLib.Sample/RestLib.Sample.csproj
```

Open the Scalar API reference at the app root and use `/health` as the readiness
probe.

## Scope

This sample demonstrates the shortest useful RestLib setup:

- in-memory repositories for `Category`, `Product`, and `Order`;
- an EF Core SQLite-backed `Customer` resource;
- JSON resource loading from `Models/*.json`;
- an appsettings-declared JSON resource for compatibility coverage;
- fluent endpoint registration for orders;
- basic filtering, sorting, field selection, batch operations, hooks, HATEOAS,
  ETags, rate limiting, OpenAPI, and versioned route prefixes.

It is not the canonical feature walkthrough. Use the ecommerce sample when you
need realistic resource ownership, role-specific surfaces, custom workflows,
or documented application-owned workarounds.
