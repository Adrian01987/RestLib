# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Structured logging via `Microsoft.Extensions.Logging` across all endpoints, batch pipelines, hook execution, and error paths
- `[LoggerMessage]` source-generated log methods for zero-allocation structured logging (EventId 1000–1349)
- Diagnostic logging for all 9 previously silent exception catch blocks
- ADR-020: Structured logging design decisions — source generators, request-time resolution, log level policy, no entity payloads

### Changed

- `ProblemDetailsResult` now logs all error responses at appropriate levels (Information for 4xx, Error for 5xx)
- `BatchContext` carries `ILogger` for consistent logging through batch pipeline
- `HookPipeline` emits Trace-level entry/exit events and Debug-level short-circuit events for all hook stages

## [2.0.0] - 2026-04-10

### Breaking Changes

- `RestLibProblemDetails.Errors` changed from `IDictionary<string, string[]>?` to `IReadOnlyDictionary<string, string[]>?` — consumers assigning to `IDictionary` or calling mutable methods (`.Add()`, `.Remove()`) must update their code
- `IBatchRepository<TEntity, TKey>` gained a new required method `GetByIdsAsync(IReadOnlyList<TKey>, CancellationToken)` — all implementations of this interface must add the new method
- Route prefix validation in `MapRestLib` now rejects null, whitespace, and prefixes missing a leading `/` at startup — previously invalid prefixes were silently accepted

### Added

- HATEOAS hypermedia links — opt-in via `RestLibOptions.EnableHateoas` adds HAL-style `_links` to all entity responses (GetById, GetAll, Create, Update, Patch, Batch) with `self`, `collection`, and CRUD-aware links conditioned on enabled operations (#42)
- `IHateoasLinkProvider<TEntity, TKey>` extensibility interface for injecting custom links (e.g., related resources) into entity responses (#42)
- `AddHateoasLinkProvider<TEntity, TKey, TProvider>()` DI extension method for registering custom link providers (#42)
- `IBatchRepository.DeleteManyAsync` now wired into the batch delete pipeline (#3)
- `GetByIdsAsync` used in batch validation to check entity existence before persisting (#12)
- `MaxCursorLength` guard rejects cursors exceeding the configured maximum before decoding (#15)
- `JsonExtensionData` on `RestLibProblemDetails` to preserve unknown members during deserialization (#18)
- Configurable problem type URIs via `RestLibOptions.ProblemTypeBaseUri` (#19)
- `CountAsync` on `IRepository<TEntity, TKey>` for total count queries (#20)
- `SECURITY.md` with vulnerability reporting instructions (#37)
- Route prefix validation in `MapRestLib` — rejects null, whitespace, and prefixes missing a leading `/` (#41)

### Changed

- Batch executor logic extracted from monolithic class into individual pipeline classes (`BatchPatchPipeline`, etc.) (#5)
- `EndpointHelpers` decomposed into individual handler files and `OptionsResolver` (#6)
- Field selection OpenAPI metadata deduplication — shared schema generation instead of per-endpoint copies (#7)
- `PatchAsync` decoupled from `JsonElement` — accepts pre-deserialized patch dictionary (#9)
- `HookContext.EarlyResult` cleared between pipeline stages to prevent stale results leaking (#16)
- `CursorEncoder` rewritten to use UTF-8 `Span<byte>` instead of `Convert.ToBase64String(Encoding.UTF8.GetBytes(...))` (#32)
- `PreviewPatch` allocation reduced by reusing serializer options and avoiding intermediate strings (#33)
- Validation error dictionary uses `IReadOnlyDictionary<string, string[]>` and deduplicates keys (#34)
- Parser naming consistency: `QueryFieldName` → `QueryParameterName`, `FieldPropertyConfiguration` → `FieldSelectionPropertyConfiguration`, `FilterParseResult.Values` → `.Filters` (#35)
- `FilterParser.Parse` replaced `.Where().ToList()` LINQ chain with manual `foreach` loop (#36)

### Fixed

- Bulk batch error attribution — errors now reference the correct item index instead of always index 0 (#2)
- Named hook resolver made thread-safe with `ConcurrentDictionary` (#4)
- `CursorEncoder` catches only `FormatException` instead of broad `Exception` (#8)
- `GetEntityKey` fails fast at registration time if the key property cannot be resolved (#10)
- Error hook exceptions no longer swallow the original exception — wrapped in `AggregateException` (#14)
- `FieldProjector` cache key includes property order to prevent incorrect cache hits (#17)

### Documentation

- ADR-019: HATEOAS design decisions — flat injection, opt-in, custom links, batch integration (#42)
- ADR-008: added batch transactional semantics section (#13)
- ADR-001: added security considerations for cursor pagination (#38)

### Tests

- HATEOAS integration tests — 28 tests covering all endpoints, field selection, disabled operations, custom link providers, and batch operations (#42)
- `HateoasLinkBuilder` unit tests — 17 tests for link building, path extraction, serialization, and custom link merging (#42)
- `FilterParser` unit tests for edge cases and error paths (#21)
- `FieldSelectionParser` unit tests for edge cases and error paths (#22)
- `NamingUtils` unit tests for snake_case conversion (#23)
- Batch bulk exception tests for error propagation (#24)
- `MapJsonResource` singular overload tests (#25)
- `MaxFilterInListSize` validation tests (#26)
- `TestHostBuilder` migration — all integration tests use shared builder (#27)
- `Trait` annotations added to all test classes (#28)
- `ProblemDetailsAssert` helper for consistent assertion patterns (#29)
- Repository exception tests for error paths (#30)
- `PreviewPatch` edge case tests (#31)
- All 39+ test classes migrated from `IDisposable` to `IAsyncLifetime` (#40)
- 32 unit tests for `GetOpenApiSchema` covering all 12 type branches and nullable wrappers (#46)
- 10 new E2E tests: If-Match concurrency (4), filter `in` operator (1), POST Location header (1), DELETE happy path (1), prev-link null (1), multi-sort (1)

## [1.3.1] - 2026-04-06

### Fixed

- Patched entities are now validated **before** persisting, preventing invalid data from being saved during PATCH operations
- `InMemoryRepository.CreateManyAsync` now throws on duplicate keys, consistent with `CreateAsync`
- User-facing error messages now use the clean entity type name instead of the internal suffixed name
- Delete endpoint now implements `If-Match` optimistic concurrency (was documented but not enforced)
- Batch operations now check `AfterPersist` hook return values instead of silently ignoring them
- Exception details in batch error responses are now gated behind `RestLibOptions.IncludeExceptionDetailsInErrors`
- `InMemoryRepository` filter value parsing now uses `InvariantCulture` to avoid culture-dependent behavior
- `IBatchRepository<TEntity, TKey>` is now registered in all `InMemoryServiceExtensions` overloads
- `MaxFilterInListSize` is now validated at startup — zero or negative values are rejected immediately
- JSON configuration filter operator presets (`Equality`, `Comparison`, etc.) now work correctly (ADR-013 documented them but the code did not resolve them)
- `FindKeyPropertyName` now detects key properties by type and `keySelector` probe, not just `Id`/`{Entity}Id` naming patterns

### Changed

- `FilterOperators` preset arrays are now `IReadOnlyList<string>` instead of mutable `string[]`
- Duplicated ETag `If-Match` logic across Update, Patch, and Delete handlers extracted into shared `CheckIfMatchPreconditionAsync` helper
- `ETagGenerator` registered as singleton instead of being allocated per request
- Reflection-based key property lookup is now cached after first resolution
- `HashBasedETagGenerator` eliminates intermediate `string` allocation by serializing directly to UTF-8 bytes
- `EntityValidator` uses `List<T>` instead of O(n^2) array concatenation
- `FieldSelectionParser` duplicate check no longer calls redundant `ToLowerInvariant` (values are already lowered)
- `SortParser` and `FieldSelectionParser` defer allowed-names string building to the error path
- `EntityValidationResult.Success()` reuses a static empty dictionary instead of allocating a new one per call
- `ETagComparer.ParseETags` removes redundant `Trim` already handled by `TrimEntries`
- OpenAPI pagination limit metadata now reads from `RestLibOptions` instead of hardcoded values
- `IRepository<TEntity, TKey>` and `IBatchRepository<TEntity, TKey>` now have a `notnull` constraint on `TKey`
- All C# source files normalized to 4-space indentation per `.editorconfig`
- Integration tests migrated to shared `TestHostBuilder` to eliminate duplicated host setup across 28 test files
- Custom inline test repositories (`OpenApiTestRepository`, `FilterableRepository`, `PaginationTestRepository`) replaced with `InMemoryRepository`

### Documentation

- Updated .NET 8 references to .NET 10 in ADR-003 and CONTRIBUTING.md
- Updated README ADR-007 description to match amended hybrid strategy
- Added test conventions, E2E instructions, and StyleCop details to CONTRIBUTING.md
- Added ADRs for ETag support (ADR-014), validation (ADR-015), JSON configuration (ADR-016), and rate limiting (ADR-017)
- Added Data Annotation validation attributes to sample app models
- Added `// Arrange`, `// Act`, `// Assert` comments to older test files for consistency

### CI

- Added concurrency control and E2E test timeout to CI workflow
- Release pipeline now requires E2E tests to pass before publishing
- GitHub Release body is now auto-populated from CHANGELOG.md
- Added issue templates, PR template, and issue configuration

## [1.3.0] - 2026-04-04

### Added

- `TagDescription` now wired up to OpenAPI documents via `TagDescriptionRegistry` and a document transformer — descriptions set via `RestLibEndpointConfiguration.TagDescription()` appear in the generated OpenAPI spec
- `RestLibOptions.MaxFilterInListSize` — configurable maximum number of items in `in` operator filter lists (default: 50)
- Exception type and message included in the `detail` field of batch 500 ProblemDetails responses for easier debugging
- Batch `BeforeResponse` and `OnError` hooks — batch operations now invoke hook pipeline stages that were previously skipped
- `/health` endpoint added to the sample app

### Changed

- Sample app migrated from Swashbuckle.AspNetCore to built-in ASP.NET OpenAPI (`Microsoft.AspNetCore.OpenApi`) + Scalar.AspNetCore for API documentation UI
- E2E test scripts updated to use `/health` endpoint for server readiness polling instead of `/swagger/v1/swagger.json`
- `FieldSelectionParser` now rejects duplicate field names (consistent with sort and filter parsers)
- README Quick Start updated to reflect OpenAPI + Scalar instead of Swashbuckle

### Fixed

- Removed unused `config` parameter from `BatchProcessor` internal method
- Renamed test method from `PropagatesToSwagger` to `PropagatesToOpenApiDocument` to reflect actual behavior

### Documentation

- ADR-008 (Batch Operations): added Known Limitations section documenting post-persist PATCH validation behavior
- ADR-011 (Filtering): added Repository Contract Enforcement section documenting that filter/sort enforcement depends on the repository implementation

## [1.2.0] - 2026-04-04

### Changed

- Migrated OpenAPI metadata from deprecated `WithOpenApi()` extension to `AddOpenApiOperationTransformer()` — removes dependency on the deprecated API and aligns with .NET 10 best practices
- OpenAPI operation summaries, descriptions, deprecation flags, and response metadata now applied via endpoint-level transformers

## [1.1.0] - 2026-04-04

### Added

- Filter operators beyond equality — bracket syntax (`?price[gte]=10&price[lte]=100`) with nine operators: `eq`, `neq`, `gt`, `lt`, `gte`, `lte`, `contains`, `starts_with`, `in`
- Per-property operator allow-lists via `AllowFiltering(prop, operators)` overloads and `FilterOperators` preset arrays (`Equality`, `Comparison`, `String`, `All`)
- `in` operator for set membership filtering (`?status[in]=active,pending`) with configurable max list size
- Type-safe operator validation — comparison operators require `IComparable`; string operators require `string`
- JSON configuration support for filter operators via `FilteringOperators` property in resource config
- JSON Schema updated for `FilteringOperators` configuration
- ADR-013: Filter Operators Beyond Equality
- E2E test suite for filter operators (17 tests)
- 84 new unit/integration/property-based tests for filter operator parsing, validation, and execution

## [1.0.0] - 2026-04-04

### Breaking Changes

- **Retargeted to .NET 10.** RestLib now requires .NET 10 (net10.0). Projects using .NET 8 should pin to v0.3.0.
- **Upgraded Microsoft.AspNetCore.OpenApi** from 8.x to 10.x.
- **Upgraded Microsoft.OpenApi** (transitive) from 1.x to 2.x. Custom code that reads or manipulates `OpenApiDocument` objects from RestLib may need updates for the v2 API (namespace changes, `JsonSchemaType` enum instead of string, `OpenApiSchemaReference` instead of `OpenApiReference`, etc.).
- **Upgraded Swashbuckle.AspNetCore** to 10.x in the sample app.
- **Empty OpenAPI tags now fall back to the entity type name** instead of passing an empty string, which .NET 10's OpenAPI infrastructure rejects.

### Added

- Prefix-less `MapRestLib<TEntity, TKey>(this RouteGroupBuilder, ...)` overload for versioned API groups
- `RestLibJsonResourceRegistry.MapAll(RouteGroupBuilder)` and `Map(RouteGroupBuilder, string)` overloads for JSON-configured resources on route groups
- DI-scoped `EndpointNameRegistry` for unique OpenAPI operation IDs with proper test isolation
- Route prefix incorporated into OpenAPI operation IDs to prevent collisions across versioned groups
- ADR-010: API Versioning Integration
- Batch operations via `EnableBatch()` with `POST /prefix/batch` endpoint
- Support for batch create, update, patch, and delete actions
- Partial success reporting with per-item status (200/207 Multi-Status)
- `IBatchRepository<TEntity, TKey>` optional interface for optimized batch persistence
- `RestLibOptions.MaxBatchSize` for configurable batch size limits (default: 100)
- Per-item hooks and validation in batch operations
- RFC 9457 Problem Details for batch-level errors (invalid action, size exceeded)
- JSON configuration support for batch operations (`Batch` property)
- JSON Schema updated for batch configuration
- ADR-008: Batch Operations
- Sorting / ordering support for GetAll endpoints via `AllowSorting` and `DefaultSort`
- `sort` query parameter with multi-field, asc/desc support
- RFC 9457 Problem Details response for invalid sort parameters
- Sort preserved in pagination links
- JSON configuration support for sorting (`Sorting` and `DefaultSort` properties)
- JSON Schema updated for sorting configuration
- ADR-009: Sorting
- Rate limiting integration via `UseRateLimiting` and `DisableRateLimiting`
- Per-operation and global rate limit policy assignment
- JSON configuration support for rate limiting (`RateLimiting` property with `Default`, `ByOperation`, and `Disabled`)
- JSON Schema updated for rate limiting configuration
- Field selection / sparse fieldsets via `AllowFieldSelection`
- `fields` query parameter for GetAll and GetById endpoints
- RFC 9457 Problem Details response for invalid field selection
- Fields preserved in pagination links
- JSON configuration support for field selection (`FieldSelection` property)
- JSON Schema updated for field selection configuration
- E2E bash test suite covering CRUD, pagination, filtering, sorting, field selection, batch, and error handling (103 tests across 8 suites)

### Changed

- Upgraded all dependency versions (FluentAssertions 8.x, xunit runner 3.x, Microsoft.NET.Test.Sdk 18.x, BenchmarkDotNet 0.15.x, coverlet 8.x)

### Fixed

- `InMemoryRepository.MergeJsonObjects` now correctly resolves naming convention mismatches between the original entity (camelCase) and patch document (snake_case) during PATCH operations
- `InMemoryRepository.GetAllAsync` no longer overflows when `Limit` is `int.MaxValue`, which caused the `Take(Limit + 1)` pagination pattern to return zero results

## [0.3.0] - 2026-03-11

### Added

- JSON-backed resource configuration via `AddJsonResource<TEntity, TKey>()` and `MapJsonResources()`
- Named hook system for JSON-configured resources (`AddNamedHook`, `AddNamedErrorHook`)
- `RestLibJsonResourceConfiguration` model for declarative resource setup from `appsettings.json`
- `RestLibJsonResourceBuilder` to apply JSON configuration to endpoint configuration
- `RestLibJsonResourceRegistry` for deferred endpoint mapping
- `IRestLibNamedHookResolver<TEntity, TKey>` abstraction for resolving named hooks at runtime
- String-based `AllowFiltering` overload for property names
- Per-operation hook selection via JSON (`ByOperation` and `Default` stages)
- OpenAPI metadata configuration via JSON (tag, summaries, descriptions, deprecation)
- Policy-based authorization configuration via JSON

## [0.2.0] - 2026-02-06

### Added

- Selective endpoint registration via `IncludeOperations()` and `ExcludeOperations()`
- Support for custom endpoints alongside RestLib-generated endpoints
- GitHub Actions CI pipeline with test coverage reporting
- GitHub Actions release pipeline for automated NuGet publishing
- Codecov integration for coverage tracking

## [0.1.0] - 2026-01-25

### Added

- Core CRUD endpoint generation via `MapRestLib<TEntity, TKey>()`
- Repository interface abstraction (`IRepository<TEntity, TKey>`)
- In-memory repository adapter (`RestLib.InMemory`)
- Cursor-based pagination with configurable limits
- Query parameter filtering support
- Problem Details (RFC 9457) error responses
- Hook pipeline for extensibility (6 hook points)
- OpenAPI documentation integration
- snake_case JSON serialization (Zalando Rule 118)
- Secure-by-default endpoints with opt-out `AllowAnonymous()`
- Policy-based authorization per operation
- ETag generation and conditional request support
- Data annotation validation with structured error responses
- Polished sample application with Swagger UI

### Security

- All endpoints require authorization by default
- Explicit opt-out required for public endpoints

### Documentation

- Comprehensive README with "Why I Built This" section
- Architecture Decision Records (ADRs) for key design choices
- XML documentation for public APIs

[Unreleased]: https://github.com/Adrian01987/RestLib/compare/v2.0.0...HEAD
[2.0.0]: https://github.com/Adrian01987/RestLib/compare/v1.3.1...v2.0.0
[1.3.1]: https://github.com/Adrian01987/RestLib/compare/v1.3.0...v1.3.1
[1.3.0]: https://github.com/Adrian01987/RestLib/compare/v1.2.0...v1.3.0
[1.2.0]: https://github.com/Adrian01987/RestLib/compare/v1.1.0...v1.2.0
[1.1.0]: https://github.com/Adrian01987/RestLib/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/Adrian01987/RestLib/compare/v0.3.0...v1.0.0
[0.3.0]: https://github.com/Adrian01987/RestLib/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/Adrian01987/RestLib/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/Adrian01987/RestLib/releases/tag/v0.1.0
