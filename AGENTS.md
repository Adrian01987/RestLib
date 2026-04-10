# AGENTS.md

Guidelines for AI coding agents working in this repository.

## Project Overview

RestLib is a .NET 10 library for ASP.NET Core Minimal APIs that generates CRUD REST
endpoints from a model and repository. It uses cursor pagination, filtering, sorting,
field selection, RFC 9457 Problem Details, and OpenAPI metadata out of the box.

## Build / Test Commands

```bash
dotnet restore                # Restore packages
dotnet build                  # Build all projects
dotnet test                   # Run all tests (1166 tests)
```

### Run a single test

```bash
dotnet test --filter "FullyQualifiedName~RestLib.Tests.ClassName.MethodName"
dotnet test --filter "FullyQualifiedName~FieldSelectionTests.GetById_WithFields_ReturnsOnlySelectedFields"
```

### Run tests by category/story

```bash
dotnet test --filter "Category=Story7.1"
```

### Run tests in a single class

```bash
dotnet test --filter "FullyQualifiedName~RestLib.Tests.SortingTests"
```

### Run E2E tests

The E2E tests live in `tests/e2eTests/` and run against the sample app on
`http://localhost:5000`. They require `curl` and `jq`.

```bash
bash tests/e2eTests/run-all.sh            # Start sample app, run all suites, stop
bash tests/e2eTests/run-all.sh --no-server # Run against an already-running server
BASE_URL=http://localhost:5000 bash tests/e2eTests/crud-tests.sh  # Single suite
```

### Other commands

```bash
dotnet build --configuration Release                     # Release build
dotnet test --collect:"XPlat Code Coverage"              # With coverage
cd benchmarks/RestLib.Benchmarks && dotnet run -c Release  # Benchmarks
```

## Project Structure

```
src/RestLib/                  Core library (NuGet package)
  Abstractions/               IRepository<TEntity, TKey>, IETagGenerator
  Batch/                      Batch request/response models, processor
  Configuration/              Endpoint config, JSON resource config, options
  FieldSelection/             Field selection config, parser, projector
  Filtering/                  Filter config, parser
  Hooks/                      Hook pipeline, named hooks
  Internal/                   Shared utilities (NamingUtils)
  Pagination/                 Cursor-based pagination
  Responses/                  CollectionResponse, ProblemDetails, ProblemTypes
  Serialization/              RestLibJsonOptions (snake_case)
  Sorting/                    Sort config, parser
  Validation/                 Data annotation validation
src/RestLib.InMemory/         In-memory repository for testing/prototyping
tests/RestLib.Tests/          xUnit integration and unit tests
  Fakes/                      Shared test entities and fake repositories
tests/e2eTests/               Bash E2E tests against the running sample app
samples/RestLib.Sample/       Sample app demonstrating all features
schemas/                      JSON Schema for resource configuration
docs/adr/                     Architecture Decision Records
```

## Code Style

### Enforced by tooling

- **TreatWarningsAsErrors**: `true` — the build fails on any warning
- **StyleCop.Analyzers**: enabled globally via `Directory.Build.props`
- **Nullable**: `enable` — all code uses nullable reference types
- **LangVersion**: `latest`
- **ImplicitUsings**: `enable`

### Formatting

- 4-space indentation, LF line endings, UTF-8
- Allman brace style (opening brace on its own line)
- File-scoped namespaces: `namespace RestLib.FieldSelection;`
- Prefer `var` when type is apparent
- XML doc comments (`<summary>`) required on all public members
- `GenerateDocumentationFile` is true — missing docs cause warnings (which are errors)

### Naming

| Element          | Convention          | Example                                      |
|------------------|---------------------|----------------------------------------------|
| Classes          | PascalCase          | `FieldSelectionParser`, `CollectionResponse`  |
| Interfaces       | `I` prefix          | `IRepository<TEntity, TKey>`                 |
| Methods          | PascalCase          | `AllowFieldSelection()`, `GetByIdAsync()`    |
| Properties       | PascalCase          | `CustomerEmail`, `IsActive`, `CreatedAt`     |
| Private fields   | `_camelCase`        | `_host`, `_repository`, `_store`             |
| Parameters       | camelCase           | `entityName`, `jsonOptions`, `ct`            |
| Enums            | PascalCase values   | `RestLibOperation.GetAll`                    |
| Files            | Match class name    | `FieldSelectionParser.cs`                    |

### StyleCop rules to watch

- **SA1202**: Public members must appear before internal members in a class
- **SA1625**: `<param>` doc text must be unique across overloads (don't copy-paste)
- SA1633 (file headers), SA1200 (using placement), SA1101 (`this.`) are suppressed
- Test projects suppress additional rules (SA1202, SA1111, SA1500, etc.)

### JSON conventions

- All API payloads use `snake_case` via `JsonNamingPolicy.SnakeCaseLower`
- Null values omitted (`JsonIgnoreCondition.WhenWritingNull`)
- Property names are case-insensitive on deserialization
- Configured in `RestLibJsonOptions.cs` — reuse these options, don't create new ones

## Error Handling

All errors use RFC 9457 Problem Details with three layers:

1. **`ProblemTypes`** — string constants for type URIs (e.g., `/problems/not-found`)
2. **`ProblemDetailsFactory`** — static factory methods returning `RestLibProblemDetails`
3. **`ProblemDetailsResult`** — wraps factory output into `IResult` with `application/problem+json`

When adding a new error type: add the constant to `ProblemTypes`, the factory method to
`ProblemDetailsFactory`, the convenience method to `ProblemDetailsResult`, and update the
error types table in `docs/adr/005-problem-details.md`.

## Test Conventions

- **Framework**: xUnit 2.x with FluentAssertions and NSubstitute
- **Assertions**: Always use FluentAssertions (`Should().Be()`, `Should().BeTrue()`, etc.)
- **Naming**: `MethodOrScenario_Condition_ExpectedResult` (e.g., `GetById_NotFound_Returns_ProblemDetails`)
- **Traits**: `[Trait("Category", "Story7.1")]` — tag tests by story number
- **AAA pattern**: Use `// Arrange`, `// Act`, `// Assert` comments

### Integration test structure

```csharp
public class FeatureTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private InMemoryRepository<Entity, Guid> _repository = null!;

    public async Task InitializeAsync()
    {
        _repository = new InMemoryRepository<Entity, Guid>(e => e.Id, Guid.NewGuid);

        (_host, _client) = await new TestHostBuilder<Entity, Guid>(_repository, "/api/entities")
            .WithEndpoint(config => config.AllowAnonymous())
            .BuildAsync();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }
}
```

## Feature Implementation Pattern

New features (filtering, sorting, field selection) follow a consistent structure:

1. **Configuration class** in `src/RestLib/<Feature>/` — property config + `AddProperty` methods
2. **Parser** — static `Parse()` method returning a parse result with `IsValid`, `Fields`/`Values`, `Errors`
3. **Integration** in `RestLibEndpointConfiguration.cs` — `Allow<Feature>()` overloads (expression-based + string-based)
4. **Endpoint wiring** in `RestLibEndpointExtensions.cs` — parse query params, validate, apply
5. **JSON config** — property on `RestLibJsonResourceConfiguration`, builder method in `RestLibJsonResourceBuilder`
6. **Schema** — update `schemas/restlib-resource.schema.json`
7. **Tests** — integration tests + parser unit tests + JSON config tests
8. **Docs** — README section, CHANGELOG entries, ADR if design decisions are non-obvious

Validate query parameters **before** any database call. In GetAll, validation happens
before `repository.GetAllAsync()`. In GetById, validation happens before `repository.GetByIdAsync()`.
