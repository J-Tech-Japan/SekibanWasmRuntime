using System.Text.Json.Serialization;

namespace Sekiban.Benchmark.Cli.Models;

public sealed class BenchmarkResult
{
    public string ModeLabel { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public int TargetEvents { get; set; }
    public int Concurrency { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime CompletedAtUtc { get; set; }
    public double TotalDurationSeconds { get; set; }
    public List<PhaseResult> Phases { get; set; } = [];
}

public sealed class PhaseResult
{
    public string Name { get; set; } = "";
    public int TotalOperations { get; set; }
    public int TotalEventsCreated { get; set; }
    public int ErrorCount { get; set; }
    public double DurationSeconds { get; set; }
    public double OperationsPerSecond { get; set; }
    public double EventsPerSecond { get; set; }
    public LatencyStats CommandLatency { get; set; } = new();
    public LatencyStats? QueryLatency { get; set; }
    public Dictionary<int, int> StatusCodeDistribution { get; set; } = new();
}
