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
        var completed = 0;
        var nextIndex = -1;
        var workerTasks = Enumerable.Range(0, concurrency).Select(_ => Task.Run(async () =>
        {
            var localSuccess = 0;
            var localErrors = 0;
            var localLatencies = new List<double>();
            var localStatusCounts = new Dictionary<int, int>();

            while (true)
            {
                var index = Interlocked.Increment(ref nextIndex);
                if (index >= totalEvents)
                {
                    break;
                }

                var summaries = new[] { "Warm", "Cold", "Hot", "Mild", "Freezing" };
                var payload = new
                {
                    forecastId = Guid.NewGuid(),
                    location = $"City-{index:D6}",
                    date = DateTime.UtcNow.AddDays(index % 365).ToString("yyyy-MM-dd"),
                    temperatureC = -10 + (index % 50),
                    summary = summaries[index % summaries.Length]
                };

                var resp = await client.CreateWeatherForecast(payload);
                var code = (int)resp.StatusCode;
                localLatencies.Add(resp.Ms);
                localStatusCounts[code] = localStatusCounts.GetValueOrDefault(code) + 1;
                if (resp.IsSuccessStatusCode)
                    localSuccess++;
                else
                    localErrors++;

                var current = Interlocked.Increment(ref completed);
                if (current % 5000 == 0)
                {
                    var elapsed = sw.Elapsed.TotalSeconds;
                    Console.WriteLine($"  Progress: {current:N0}/{totalEvents:N0} ({current / elapsed:F0} ops/sec)");
                }
            }

            return new WorkerResult(localSuccess, localErrors, localLatencies, localStatusCounts);
        })).ToArray();

        var workerResults = await Task.WhenAll(workerTasks);
        sw.Stop();

        var statusCounts = new Dictionary<int, int>();
        foreach (var worker in workerResults)
        {
            counter += worker.SuccessCount;
            errorCount += worker.ErrorCount;
            foreach (var ms in worker.Latencies)
                phase.CommandLatency.Record(ms);
            foreach (var (code, count) in worker.StatusCounts)
                statusCounts[code] = statusCounts.GetValueOrDefault(code) + count;
        }

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

    private sealed record WorkerResult(
        int SuccessCount,
        int ErrorCount,
        List<double> Latencies,
        Dictionary<int, int> StatusCounts);
}
