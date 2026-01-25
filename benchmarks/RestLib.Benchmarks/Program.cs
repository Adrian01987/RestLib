using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Running;
using RestLib.Benchmarks;

// Configure BenchmarkDotNet to export results to Markdown
var config = DefaultConfig.Instance
    .AddExporter(MarkdownExporter.GitHub)
    .WithOptions(ConfigOptions.DisableOptimizationsValidator);

// Run all benchmarks in the assembly
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
