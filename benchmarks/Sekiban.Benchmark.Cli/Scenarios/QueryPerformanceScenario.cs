using Sekiban.Benchmark.Cli.Models;

namespace Sekiban.Benchmark.Cli.Scenarios;

public static class QueryPerformanceScenario
{
    public static async Task<PhaseResult> RunAsync(HttpApiClient client, List<Guid> roomIds, int iterations = 50)
    {
        Console.WriteLine($"\n=== Phase 4/5: Query Performance ({iterations} iterations) ===");
        var phase = new PhaseResult { Name = "QueryPerformance" };
        var queryLatency = new LatencyStats();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var ops = 0;
        var errors = 0;
        var statusCounts = new Dictionary<int, int>();

        for (var i = 0; i < iterations; i++)
        {
            // Query 1: Room list
            RecordQuery(await client.GetRooms());

            // Query 2: Reservation list page 1
            RecordQuery(await client.GetReservations(1, 100));

            // Query 3: Reservations by room (pick a random room)
            var roomId = roomIds[i % roomIds.Count];
            RecordQuery(await client.GetReservationsByRoom(roomId));

            // Query 4: Weather count
            RecordQuery(await client.GetWeatherCount());

            // Query 5: Weather list
            RecordQuery(await client.GetWeatherList());

            if ((i + 1) % 10 == 0)
                Console.WriteLine($"  Query iteration {i + 1}/{iterations}");
        }

        sw.Stop();
        queryLatency.Compute();
        phase.CommandLatency = queryLatency;
        phase.TotalOperations = ops;
        phase.ErrorCount = errors;
        phase.DurationSeconds = sw.Elapsed.TotalSeconds;
        phase.OperationsPerSecond = ops / Math.Max(phase.DurationSeconds, 0.001);
        phase.StatusCodeDistribution = statusCounts;

        Console.WriteLine($"  Completed: {ops} queries in {phase.DurationSeconds:F1}s ({phase.OperationsPerSecond:F0} queries/sec), errors={errors}");
        Console.WriteLine($"  Latency: p50={queryLatency.P50Ms:F1}ms p95={queryLatency.P95Ms:F1}ms p99={queryLatency.P99Ms:F1}ms max={queryLatency.MaxMs:F1}ms");
        return phase;

        void RecordQuery(MeasuredResponse resp)
        {
            queryLatency.Record(resp.Ms);
            var code = (int)resp.StatusCode;
            statusCounts[code] = statusCounts.GetValueOrDefault(code) + 1;
            ops++;
            if (!resp.IsSuccessStatusCode) errors++;
        }
    }
}
