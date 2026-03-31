using System.Text.Json.Serialization;

namespace Sekiban.Benchmark.Cli.Models;

public sealed class LatencyStats
{
    private readonly List<double> _samples = [];

    [JsonIgnore]
    public int Count => _samples.Count;

    public double MinMs { get; private set; }
    public double MaxMs { get; private set; }
    public double MeanMs { get; private set; }
    public double P50Ms { get; private set; }
    public double P95Ms { get; private set; }
    public double P99Ms { get; private set; }

    public void Record(double ms)
    {
        _samples.Add(ms);
    }

    public void Compute()
    {
        if (_samples.Count == 0) return;
        _samples.Sort();
        MinMs = _samples[0];
        MaxMs = _samples[^1];
        MeanMs = _samples.Average();
        P50Ms = Percentile(0.50);
        P95Ms = Percentile(0.95);
        P99Ms = Percentile(0.99);
    }

    private double Percentile(double p)
    {
        if (_samples.Count == 0) return 0;
        var index = (int)Math.Ceiling(p * _samples.Count) - 1;
        return _samples[Math.Max(0, index)];
    }
}
