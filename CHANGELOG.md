# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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

[Unreleased]: https://github.com/Adrian01987/RestLib/compare/v0.3.0...HEAD
[0.3.0]: https://github.com/Adrian01987/RestLib/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/Adrian01987/RestLib/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/Adrian01987/RestLib/releases/tag/v0.1.0
