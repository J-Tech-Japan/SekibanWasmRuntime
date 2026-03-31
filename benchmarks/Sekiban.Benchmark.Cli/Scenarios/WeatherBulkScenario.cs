using Sekiban.Benchmark.Cli.Models;

namespace Sekiban.Benchmark.Cli.Scenarios;

public static class WeatherBulkScenario
{
    public static async Task<PhaseResult> RunAsync(HttpApiClient client, int totalEvents, int concurrency)
    {
        Console.WriteLine($"\n=== Phase 2: Weather Bulk ({totalEvents:N0} events, concurrency={concurrency}) ===");
        var phase = new PhaseResult { Name = "WeatherBulk" };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var counter = 0;
        var errorCount = 0;
        var statusCounts = new Dictionary<int, int>();
        var latencies = new List<double>();
        var lockObj = new object();

        var semaphore = new SemaphoreSlim(concurrency);
        var tasks = new List<Task>();

        for (var i = 0; i < totalEvents; i++)
        {
            await semaphore.WaitAsync();
            var index = i;
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var summaries = new[] { "Warm", "Cold", "Hot", "Mild", "Freezing" };
                    var payload = new
                    {
                        forecastId = Guid.NewGuid(),
                        location = $"City-{index:D6}",
                        date = DateTime.UtcNow.AddDays(index % 365).ToString("yyyy-MM-dd"),
                        temperatureC = -10 + (index % 50),
                        summary = summaries[index % summaries.Length]
                    };

                    var (resp, ms) = await client.CreateWeatherForecast(payload);
                    var code = (int)resp.StatusCode;

                    lock (lockObj)
                    {
                        latencies.Add(ms);
                        statusCounts[code] = statusCounts.GetValueOrDefault(code) + 1;
                        if (resp.IsSuccessStatusCode)
                            Interlocked.Increment(ref counter);
                        else
                            Interlocked.Increment(ref errorCount);
                    }

                    var current = Interlocked.CompareExchange(ref counter, 0, 0) + Interlocked.CompareExchange(ref errorCount, 0, 0);
                    if (current % 5000 == 0 && current > 0)
                    {
                        var elapsed = sw.Elapsed.TotalSeconds;
                        Console.WriteLine($"  Progress: {current:N0}/{totalEvents:N0} ({current / elapsed:F0} ops/sec)");
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            }));
        }

        await Task.WhenAll(tasks);
        sw.Stop();

        foreach (var ms in latencies) phase.CommandLatency.Record(ms);
        phase.CommandLatency.Compute();
        phase.TotalOperations = counter + errorCount;
        phase.TotalEventsCreated = counter;
        phase.ErrorCount = errorCount;
        phase.DurationSeconds = sw.Elapsed.TotalSeconds;
        phase.OperationsPerSecond = phase.TotalOperations / phase.DurationSeconds;
        phase.EventsPerSecond = phase.TotalEventsCreated / phase.DurationSeconds;
        phase.StatusCodeDistribution = statusCounts;

        Console.WriteLine($"  Completed: {counter:N0} events in {phase.DurationSeconds:F1}s ({phase.EventsPerSecond:F0} events/sec), errors={errorCount}");
        Console.WriteLine($"  Latency: p50={phase.CommandLatency.P50Ms:F1}ms p95={phase.CommandLatency.P95Ms:F1}ms p99={phase.CommandLatency.P99Ms:F1}ms max={phase.CommandLatency.MaxMs:F1}ms");
        return phase;
    }
}
