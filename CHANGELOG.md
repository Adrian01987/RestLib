# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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

[Unreleased]: https://github.com/Adrian01987/RestLib/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/Adrian01987/RestLib/compare/v0.3.0...v1.0.0
[0.3.0]: https://github.com/Adrian01987/RestLib/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/Adrian01987/RestLib/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/Adrian01987/RestLib/releases/tag/v0.1.0
