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
            var (r1, ms1) = await client.GetRooms();
            RecordQuery(r1, ms1);

            // Query 2: Reservation list page 1
            var (r2, ms2) = await client.GetReservations(1, 100);
            RecordQuery(r2, ms2);

            // Query 3: Reservations by room (pick a random room)
            var roomId = roomIds[i % roomIds.Count];
            var (r3, ms3) = await client.GetReservationsByRoom(roomId);
            RecordQuery(r3, ms3);

            // Query 4: Weather count
            var (r4, ms4) = await client.GetWeatherCount();
            RecordQuery(r4, ms4);

            // Query 5: Weather list
            var (r5, ms5) = await client.GetWeatherList();
            RecordQuery(r5, ms5);

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

        void RecordQuery(HttpResponseMessage resp, double ms)
        {
            queryLatency.Record(ms);
            var code = (int)resp.StatusCode;
            statusCounts[code] = statusCounts.GetValueOrDefault(code) + 1;
            ops++;
            if (!resp.IsSuccessStatusCode) errors++;
        }
    }
}
