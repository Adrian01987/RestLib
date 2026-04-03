using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Running;

// Configure BenchmarkDotNet to export results to Markdown
var config = DefaultConfig.Instance
    .AddExporter(MarkdownExporter.GitHub);

// Run all benchmarks in the assembly
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
