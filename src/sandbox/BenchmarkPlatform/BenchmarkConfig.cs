using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;

namespace PerformanceBenchmarks;

// Конфигурация BenchmarkDotNet
public class BenchmarkConfig : ManualConfig
{
    public BenchmarkConfig()
    {
        AddJob(Job.Default
            .WithLaunchCount(1)
            .WithWarmupCount(3)
            .WithIterationCount(5)
            .WithStrategy(BenchmarkDotNet.Engines.RunStrategy.Throughput)
            .WithUnrollFactor(16));

        AddDiagnoser(MemoryDiagnoser.Default);
        AddExporter(MarkdownExporter.GitHub);
        AddColumn(StatisticColumn.OperationsPerSecond);
        AddColumn(StatisticColumn.P95);
        AddColumn(RankColumn.Arabic);

        WithOptions(ConfigOptions.JoinSummary | ConfigOptions.DisableLogFile);
    }
}
