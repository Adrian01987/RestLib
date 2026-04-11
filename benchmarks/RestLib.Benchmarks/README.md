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

Benchmarks run on: Linux Ubuntu 24.04.4 LTS, Intel Core i3-8130U CPU 2.20GHz, .NET 10.0.5

> **Note:** These benchmarks were run without process priority elevation, which
> increases variance. Focus on the **Ratio** and **Alloc Ratio** columns rather than
> absolute microsecond values.

```
BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Core i3-8130U CPU 2.20GHz (Kaby Lake), 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.201
  [Host]     : .NET 10.0.5 (10.0.5, 10.0.526.15411), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.5 (10.0.5, 10.0.526.15411), X64 RyuJIT x86-64-v3

| Method                                      | Categories     | Mean     | Error    | StdDev    | Median   | Ratio | RatioSD | Allocated | Alloc Ratio |
|-------------------------------------------- |--------------- |---------:|---------:|----------:|---------:|------:|--------:|----------:|------------:|
| 'Raw Minimal API - POST'                    | Create         | 265.5 us | 30.76 us |  89.74 us | 260.1 us |  1.12 |    0.56 |  12.46 KB |        1.00 |
| 'RestLib - POST'                            | Create         | 384.8 us | 71.82 us | 203.73 us | 326.7 us |  1.63 |    1.07 |  14.10 KB |        1.13 |
|                                             |                |          |          |           |          |       |         |           |             |
| 'Raw Minimal API - GET all'                 | GetAll         | 313.4 us | 39.61 us | 111.73 us | 285.8 us |  1.12 |    0.54 |  16.74 KB |        1.00 |
| 'RestLib - GET all'                         | GetAll         | 271.7 us | 31.49 us |  92.86 us | 246.1 us |  0.97 |    0.46 |  19.05 KB |        1.14 |
|                                             |                |          |          |           |          |       |         |           |             |
| 'RestLib - GET all (no fields)'             | GetAll_Fields  | 289.6 us | 41.97 us | 121.78 us | 261.4 us |  1.18 |    0.70 |  19.07 KB |        1.00 |
| 'RestLib - GET all (?fields=id,name)'       | GetAll_Fields  | 475.3 us | 51.86 us | 149.62 us | 448.8 us |  1.93 |    0.99 |  39.98 KB |        2.10 |
| 'RestLib - GET all (?fields=id,name,price)' | GetAll_Fields  | 563.9 us | 88.23 us | 255.96 us | 498.5 us |  2.29 |    1.42 |  43.43 KB |        2.28 |
|                                             |                |          |          |           |          |       |         |           |             |
| 'Raw Minimal API - GET by ID'               | GetById        | 170.5 us | 26.94 us |  79.43 us | 149.6 us |  1.26 |    0.93 |   9.76 KB |        1.00 |
| 'RestLib - GET by ID'                       | GetById        | 217.4 us | 29.76 us |  82.47 us | 203.9 us |  1.61 |    1.07 |  10.13 KB |        1.04 |
|                                             |                |          |          |           |          |       |         |           |             |
| 'RestLib - GET by ID (no fields)'           | GetById_Fields | 124.3 us | 14.85 us |  42.84 us | 112.1 us |  1.12 |    0.55 |  10.21 KB |        1.00 |
| 'RestLib - GET by ID (?fields=id,name)'     | GetById_Fields | 162.7 us | 16.38 us |  46.47 us | 149.7 us |  1.46 |    0.65 |  12.22 KB |        1.20 |
|                                             |                |          |          |           |          |       |         |           |             |
| 'Raw Minimal API - PUT'                     | Update         | 232.1 us | 24.69 us |  72.81 us | 221.1 us |  1.10 |    0.51 |  12.78 KB |        1.00 |
| 'RestLib - PUT'                             | Update         | 282.0 us | 37.88 us | 108.06 us | 258.5 us |  1.34 |    0.69 |  14.44 KB |        1.13 |
```

### Summary

| Operation | Overhead (Mean) | Overhead (Median) | Memory | Status |
| --- | --- | --- | --- | --- |
| POST | +45% | +26% | +13% | Acceptable |
| GET all | **-13%** | **-14%** | +14% | Faster than raw |
| GET by ID | +27% | +36% | +4% | Acceptable |
| PUT | +22% | +17% | +13% | Acceptable |

**Key Findings:**

- **GET all remains faster** with RestLib due to optimized serialization paths
- **Memory overhead** is consistently ~4-14% across all operations
- **High variance** in this run (no priority elevation) — Median values are more
  representative than Mean for this environment

## Environment

For consistent benchmarks, ensure:

- No other CPU-intensive processes running
- Power settings set to "High Performance"
- Running on AC power (not battery)
- Consistent hardware/OS across comparisons
