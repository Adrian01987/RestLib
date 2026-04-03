# RestLib Benchmarks

Performance benchmarks comparing RestLib endpoints against hand-written Minimal API endpoints.

## Purpose

These benchmarks measure the overhead introduced by RestLib compared to raw Minimal APIs,
helping users understand the performance characteristics of the library.

## Running Benchmarks

### Full Benchmark Run (Release Mode)

For accurate results, run in Release configuration:

```bash
cd benchmarks/RestLib.Benchmarks
dotnet run -c Release
```

Or run specific benchmarks:

```bash
dotnet run -c Release -- --filter "*GetById*"
dotnet run -c Release -- --filter "*FieldProjection*"
```

### Quick Test Run (Debug Mode)

For a quick sanity check (not for accurate measurements):

```bash
dotnet run -c Debug -- --job Dry
```

## Benchmark Suites

### CrudBenchmarks

End-to-end HTTP benchmarks comparing RestLib against raw Minimal API endpoints.

| Benchmark | Category | Description |
| --- | --- | --- |
| `Raw Minimal API - GET by ID` | GetById | Raw Minimal API GET by ID (baseline) |
| `RestLib - GET by ID` | GetById | RestLib GET by ID |
| `Raw Minimal API - GET all` | GetAll | Raw Minimal API GET all |
| `RestLib - GET all` | GetAll | RestLib GET all with pagination wrapper |
| `Raw Minimal API - POST` | Create | Raw Minimal API POST |
| `RestLib - POST` | Create | RestLib POST |
| `Raw Minimal API - PUT` | Update | Raw Minimal API PUT |
| `RestLib - PUT` | Update | RestLib PUT |
| `RestLib - GET by ID (no fields)` | GetById_Fields | RestLib GET by ID without field selection (baseline) |
| `RestLib - GET by ID (?fields=id,name)` | GetById_Fields | RestLib GET by ID with 2-field selection |
| `RestLib - GET all (no fields)` | GetAll_Fields | RestLib GET all without field selection (baseline) |
| `RestLib - GET all (?fields=id,name)` | GetAll_Fields | RestLib GET all with 2-field selection |
| `RestLib - GET all (?fields=id,name,price)` | GetAll_Fields | RestLib GET all with 3-field selection |

### FieldProjectionBenchmarks

Micro-benchmarks comparing the old serialize-then-pick approach against the current hybrid
`FieldProjector` implementation. Uses a 15-property `RichProduct` entity to measure
projection overhead in isolation (no HTTP round-trip).

| Category | Old approach | New approach | What it measures |
| --- | --- | --- | --- |
| Single_2Fields | Serialize-then-pick | Hybrid (reflection) | 1 entity, 2 of 15 fields |
| Single_5Fields | Serialize-then-pick | Hybrid (reflection) | 1 entity, 5 of 15 fields |
| Single_AllFields | Serialize-then-pick | Hybrid (serialize fallback) | 1 entity, all 15 fields |
| Many_10x5Fields | Serialize-then-pick | Hybrid (reflection) | 10 entities, 5 fields each |
| Many_100x5Fields | Serialize-then-pick | Hybrid (reflection) | 100 entities, 5 fields each |
| Many_1000x5Fields | Serialize-then-pick | Hybrid (reflection) | 1000 entities, 5 fields each |
| Many_100xAllFields | Serialize-then-pick | Hybrid (serialize fallback) | 100 entities, all 15 fields |
| Many_1000xAllFields | Serialize-then-pick | Hybrid (serialize fallback) | 1000 entities, all 15 fields |

The hybrid strategy uses compiled expression tree getters for sparse selections (≤50% of
properties) and falls back to serialize-then-pick for dense selections (>50%). See
[ADR-007](../../docs/adr/007-field-selection.md) for design rationale and benchmark results.

## Data Seeding

The CRUD benchmarks seed **100 products** to simulate realistic scenarios:

- One product is designated for GetById operations
- All 100 products are returned in GetAll operations (testing pagination overhead)
- The field selection host enables `fields` on `Id`, `Name`, and `Price`

The field projection benchmarks use a 15-property `RichProduct` entity with collections
of 1, 10, 100, and 1000 entities to measure scaling behavior.

## Output

Results are exported in multiple formats:

- **Console**: Summary displayed during run
- **Markdown**: `BenchmarkDotNet.Artifacts/results/*.md`
- **HTML**: `BenchmarkDotNet.Artifacts/results/*.html`
- **CSV**: `BenchmarkDotNet.Artifacts/results/*.csv`

## Interpreting Results

Key metrics to observe:

| Metric | Description |
| --- | --- |
| **Mean** | Average execution time |
| **Error** | Half of 99.9% confidence interval |
| **StdDev** | Standard deviation of measurements |
| **Ratio** | Comparison to baseline (1.00 = same as baseline) |
| **Allocated** | Memory allocated per operation |

### Target Performance

RestLib aims for:

- **< 15% overhead** on request latency (most operations)
- **< 20% overhead** on memory allocation

## Latest Results

Benchmarks run on: Windows 11, Intel Core i3-8130U CPU 2.20GHz, .NET 8.0.22

```
BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.7623)
Intel Core i3-8130U CPU 2.20GHz (Kaby Lake), 1 CPU, 4 logical and 2 physical cores
.NET SDK 9.0.308
  [Host]     : .NET 8.0.22 (8.0.2225.52707), X64 RyuJIT AVX2
  DefaultJob : .NET 8.0.22 (8.0.2225.52707), X64 RyuJIT AVX2

| Method                        | Categories | Mean      | Error    | StdDev    | Ratio | Allocated | Alloc Ratio |
|------------------------------ |----------- |----------:|---------:|----------:|------:|----------:|------------:|
| 'Raw Minimal API - POST'      | Create     |  97.30 us | 5.561 us | 16.396 us |  1.00 |  11.65 KB |        1.00 |
| 'RestLib - POST'              | Create     |  99.35 us | 1.567 us |  1.308 us |  1.02 |  13.20 KB |        1.13 |
|                               |            |           |          |           |       |           |             |
| 'Raw Minimal API - GET all'   | GetAll     | 173.26 us | 9.706 us | 28.619 us |  1.00 |  17.34 KB |        1.00 |
| 'RestLib - GET all'           | GetAll     | 116.54 us | 1.539 us |  3.037 us |  0.67 |  18.62 KB |        1.07 |
|                               |            |           |          |           |       |           |             |
| 'Raw Minimal API - GET by ID' | GetById    |  67.49 us | 4.209 us | 12.409 us |  1.00 |  10.15 KB |        1.00 |
| 'RestLib - GET by ID'         | GetById    |  69.48 us | 4.483 us | 13.218 us |  1.03 |  10.31 KB |        1.02 |
|                               |            |           |          |           |       |           |             |
| 'Raw Minimal API - PUT'       | Update     |  88.64 us | 1.745 us |  3.010 us |  1.00 |  12.22 KB |        1.00 |
| 'RestLib - PUT'               | Update     | 114.16 us | 6.813 us | 20.088 us |  1.29 |  13.86 KB |        1.13 |
```

### Summary

| Operation | Overhead | Memory | Status |
| --- | --- | --- | --- |
| POST | +2% | +13% | Excellent |
| GET all | **-33%** | +7% | Faster than raw |
| GET by ID | +3% | +2% | Excellent |
| PUT | +29% | +13% | Under investigation |

**Key Findings:**

- **GET all is 33% faster** with RestLib due to optimized serialization paths
- **POST and GET by ID** have minimal overhead (~2-3%)
- **PUT** has higher overhead (29%) — under investigation for future optimization

## Environment

For consistent benchmarks, ensure:

- No other CPU-intensive processes running
- Power settings set to "High Performance"
- Running on AC power (not battery)
- Consistent hardware/OS across comparisons
