# RestLib Ecommerce Sample

This sample is the end-to-end ecommerce reference application for RestLib. It
combines fluent endpoint registration, folder-loaded JSON resources,
`appsettings.json` resources, EF Core SQLite, in-memory repositories, hooks,
custom Minimal API endpoints, authentication, authorization, HATEOAS, ETags,
rate limiting, and operational E2E coverage.

## Running the sample

From the repository root:

```powershell
dotnet run --project samples/RestLib.Sample.Ecommerce/RestLib.Sample.Ecommerce.csproj
```

The app serves OpenAPI and Scalar UI from the configured ASP.NET Core URLs.
E2E suites under `tests/e2eTests/ecommerce` start the sample themselves unless
they are run with `--no-server`.

## Feature index

The table maps every feature listed in the top-level `README.md` Features
section to a concrete implementation point in this sample.

| Top-level feature | Sample reference | What it demonstrates |
| --- | --- | --- |
| Secure by Default | `Program.cs:35`, `Program.cs:188` | Generated endpoints require authorization by default, with role policies layered onto admin, customer, carrier, and support surfaces. |
| Standards-Compliant Responses | `Program.cs:33`, `Ordering/CheckoutEndpoints.cs:160` | ETags are enabled globally, and custom checkout failures use RestLib Problem Details responses. |
| Advanced Filtering | `Models/Products.json:10`, `Program.cs:263` | JSON and fluent resources allow-list filterable fields and operator families. |
| Sorting | `Models/Products.json:16`, `Program.cs:267` | JSON and fluent resources declare sortable fields and default sort orders. |
| Rate Limiting | `Program.cs:39`, `Program.cs:214`, `Models/Products.json:19` | ASP.NET Core rate-limiter policies are registered, middleware is enabled, and generated endpoints bind named policies per operation. |
| Field Selection | `Models/Products.json:18`, `Program.cs:268` | JSON and fluent resources expose sparse fieldsets. |
| Nested object responses (opt-in) | `Program.cs:270` | Admin product sparse responses opt into nested object reconstruction for selected navigation properties. |
| Batch Operations | `Program.cs:291`, `Models/Shipments.json:20` | Admin product and carrier shipment surfaces enable bulk operations with configured action sets. |
| HATEOAS Hypermedia Links | `Program.cs:34`, `Program.cs:38`, `Ordering/OrderLinkProvider.cs:28` | Global HATEOAS is enabled and a custom order link provider adds workflow links such as pay, cancel, and confirm delivery. |
| Select Operations | `Models/SupportTickets.json:7`, `Program.cs:369` | JSON and fluent resources expose only the operations intended for each surface. |
| Extensible via Hooks | `Program.cs:143`, `Models/ShipmentEvents.json:15`, `Fulfillment/ShipmentEventHooks.cs:18` | Named hook registrations connect JSON resources to strongly typed pipeline behavior. |
| Persistence-Agnostic | `Program.cs:118`, `Program.cs:130` | The same endpoint style is backed by EF Core repositories and in-memory reference repositories. |
| EF Core Adapter | `Program.cs:118`, `Data/EcommerceDbContext.cs:11` | EF Core adapter registrations expose the SQLite-backed ecommerce model through generated endpoints. |
| Current EF Core Adapter Limitations | `Models/CartItem.cs:15`, `Program.cs:124` | The sample stays within the adapter's supported key shape by using a two-part composite cart-item key. |
| Versioning | `Models/Products.json:5`, `Program.cs:260` | Storefront and admin product resources demonstrate URL-prefix versioning with `/api/v1` and `/api/v2` routes. |
| URL prefix versioning | `Models/Products.json:5`, `Program.cs:260` | Concrete versioned route prefixes are used directly on generated resource routes. |
| Prefix-less overload on a route group | `Program.cs:367` | Storefront order routes are mapped on a route group before calling the prefix-less RestLib overload. |
| With Asp.Versioning.Http | `Program.cs:260` | This sample does not configure `Asp.Versioning.Http`; it keeps versioning to explicit route prefixes for readability. |

## Documented limitations and workarounds

- Row-level scoping is application-owned in this sample. `EcommerceDbContext`
  injects `ICurrentUser` and applies EF Core query filters so customers and
  carriers only see their own rows, while admins bypass those scopes
  (`Data/EcommerceDbContext.cs:12`, `Data/EcommerceDbContext.cs:187`). The
  current RestLib decision is documented in
  `docs/adr/027-row-level-security-application-owned.md`.
- Cross-resource transactions are also application-owned. Checkout uses a
  custom endpoint and explicit EF Core transaction to create an order, decrement
  stock, create a shipment, and commit as one operation
  (`Ordering/CheckoutEndpoints.cs:28`, `Ordering/CheckoutEndpoints.cs:54`,
  `Ordering/CheckoutEndpoints.cs:137`). The current RestLib decision is
  documented in `docs/adr/028-unit-of-work-application-owned.md`.
