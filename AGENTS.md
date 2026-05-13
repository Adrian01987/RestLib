# AGENTS.md

Compact guidance for OpenCode agents working in RestLib.

## What This Repo Is
- .NET 10 library for ASP.NET Core Minimal APIs that maps typed models and repositories to CRUD REST endpoints.
- Public packages/projects: `src/RestLib` core, `src/RestLib.InMemory` adapter, `src/RestLib.EntityFrameworkCore` adapter.
- Tests are split between `tests/RestLib.Tests` for core/InMemory behavior and `tests/RestLib.EntityFrameworkCore.Tests` for EF Core adapter behavior.
- `samples/RestLib.Sample` is the executable sample used by E2E tests; it mixes JSON-backed resources, InMemory repositories, and EF Core SQLite.

## Commands
- SDK is pinned by `global.json` to `10.0.201` with `rollForward: latestFeature`; use .NET 10.
- Normal verification: `dotnet restore`, `dotnet build`, `dotnet test`.
- CI verification uses Release: `dotnet build --no-restore --configuration Release`, then tests with `--no-build --configuration Release`.
- Focused test class or method: `dotnet test --filter "FullyQualifiedName~RestLib.Tests.SortingTests"`.
- Story/category tests: `dotnet test --filter "Category=Story4.3"`; categories follow `StoryX.Y` (sometimes with a `.Suffix` like `Story4.3.Operators`) per feature file. Copy the trait from the nearest existing test rather than inventing one.
- Run one test project: `dotnet test tests/RestLib.Tests/RestLib.Tests.csproj` or `dotnet test tests/RestLib.EntityFrameworkCore.Tests/RestLib.EntityFrameworkCore.Tests.csproj`.
- E2E: `bash tests/e2eTests/run-all.sh`; requires `curl`, `jq`, `bc`, and dotnet. The script starts the sample on `http://localhost:5000` itself. Options: `--no-build`, `--no-server`, `BASE_URL=http://localhost:5000`, `SUITE=crud`. Suites available in `tests/e2eTests/*-tests.sh` (crud, batch, etag, filtering, filter-operator, field-selection, sorting, pagination, hateoas, versioning, error-handling).
- Single E2E suite: `BASE_URL=http://localhost:5000 bash tests/e2eTests/crud-tests.sh`.
- Benchmarks only for performance-sensitive work: run `dotnet run -c Release` in `benchmarks/RestLib.Benchmarks`.

## Build Gates
- `Directory.Build.props` sets `TargetFramework=net10.0`, `Nullable=enable`, `ImplicitUsings=enable`, `LangVersion=latest`, and `TreatWarningsAsErrors=true`.
- StyleCop is enabled globally; missing XML docs on public members and analyzer warnings fail the build.
- `.editorconfig` enforces LF, UTF-8, 4 spaces, Allman braces, file-scoped namespaces, and private `_camelCase` fields.
- Public package projects include `GenerateDocumentationFile=true`; add XML summaries for all public APIs.
- Test projects suppress some nullable dereference warnings, but still use the same warnings-as-errors/StyleCop baseline.

## Architecture Hotspots
- Endpoint registration starts in `src/RestLib/RestLibEndpointExtensions.cs`; individual operation behavior lives under `src/RestLib/Endpoints/`.
- DI and JSON-backed resource registration live in `src/RestLib/RestLibServiceExtensions.cs`; JSON resource mapping lives in `src/RestLib/RestLibJsonEndpointExtensions.cs`.
- JSON resource shape and application logic live in `src/RestLib/Configuration/RestLibJsonResourceConfiguration.cs` and `RestLibJsonResourceBuilder.cs`; update `schemas/restlib-resource.schema.json` when JSON config changes.
- Query features follow the existing pattern (see `Filtering/`, `Sorting/`, `FieldSelection/`, `Search/`, `Pagination/`, `Hypermedia/`, `Batch/`, `Hooks/`, `Caching/`): configuration object, parser, endpoint validation/wiring in the matching `Endpoints/*Handler.cs`, JSON config in `Configuration/RestLibJsonResourceConfiguration.cs`, `schemas/restlib-resource.schema.json`, tests, ADR under `docs/adr/`.
- Repository contracts live in `src/RestLib/Abstractions/` (`IRepository`, `ICountableRepository`, `IQueryCountableRepository`, `IBatchRepository`, `IFieldSelectionProjectionRepository`, `IETagGenerator`); endpoints feature-detect these interfaces rather than requiring all of them.
- Two-model resources (entity vs. representation) go through `IRestLibMapper` resolved by `Mapping/RestLibMapperResolver.cs`; default is `ReflectionRestLibMapper`. See ADR 024 before changing the mapping seam.
- Composite keys are modeled by `RestLibCompositeKey.cs` + `Configuration/RestLibCompositeKey*` and `Endpoints/EntityKeyHelper.cs`; see ADR 025 before changing key parsing/binding.
- EF Core adapter registration is `src/RestLib.EntityFrameworkCore/EfCoreServiceExtensions.cs`; repository/query translation is `EfCoreRepository.cs` plus the `*Builder.cs` helpers (Comparison, In, String, Search, Sort, Expression).

## Repo-Specific Rules
- Validate query parameters before repository/database calls. Existing handlers do this for filtering, sorting, field selection, and pagination errors.
- API JSON uses `src/RestLib/Serialization/RestLibJsonOptions.cs`: snake_case names, case-insensitive deserialization, omit nulls by default. Do not introduce separate serializer defaults; resolve options via `OptionsResolver` / `RestLibJsonOptions.Create*`.
- New Problem Details types require updates in all layers: `src/RestLib/Responses/ProblemTypes.cs`, `ProblemDetailsFactory.cs`, `ProblemDetailsResult.cs`, and the table in `docs/adr/005-problem-details.md`.
- Keep core features adapter-neutral unless the work is explicitly EF Core-specific. Do not leak EF Core concepts into `src/RestLib`.
- Preserve existing fluent API and appsettings-based JSON configuration unless a planned change explicitly says otherwise.
- For JSON-facing features, update implementation, tests, `schemas/restlib-resource.schema.json`, README/docs, and ADRs when the behavior or accepted shape changes.

## Tests
- Use xUnit, FluentAssertions, and NSubstitute; do not use xUnit `Assert.*` in new tests.
- Test names follow `MethodOrScenario_Condition_ExpectedResult`; include `// Arrange`, `// Act`, `// Assert` comments.
- Integration tests commonly use `TestHostBuilder<TEntity, TKey>` and `InMemoryRepository<TEntity, TKey>` from `tests/RestLib.Tests/Fakes`.
- EF Core tests use SQLite via `Microsoft.EntityFrameworkCore.Sqlite`; keep EF-specific coverage in `tests/RestLib.EntityFrameworkCore.Tests`.
- CI runs both test projects with coverlet and merges them into a single cobertura report (RestLib.Tests JSON -> EF Core cobertura via `MergeWith`); `[RestLib.InMemory]*` is excluded from coverage. Threshold is 80% line coverage on Ubuntu only.

## Release And CI Notes
- CI runs on Ubuntu and Windows, then runs E2E on Ubuntu only.
- Release runs on tags matching `v*`; package version comes from the tag and release notes are extracted from `CHANGELOG.md`.
- `dotnet pack --no-build --configuration Release --output ./artifacts` is the local package smoke command that matches CI packaging behavior.
