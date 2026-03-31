using Sekiban.Benchmark.Cli.Models;

namespace Sekiban.Benchmark.Cli.Scenarios;

public static class ReservationLifecycleScenario
{
    /// <summary>
    /// Uses QuickReservation endpoint which internally executes:
    /// draft(1) + hold(1) + confirm(1) = 3 events per call.
    /// For rooms with requiresApproval, adds approval events too.
    /// Also mixes in simple draft-only calls for variety.
    /// </summary>
    public static async Task<PhaseResult> RunAsync(
        HttpApiClient client,
        List<Guid> roomIds,
        int targetEvents,
        int concurrency)
    {
        // QuickReservation produces ~3 events per call
        // Use reduced concurrency (max 2) to avoid tag contention on room-level projections
        var effectiveConcurrency = Math.Min(concurrency, 2);
        var estimatedCalls = targetEvents / 3;

        Console.WriteLine($"\n=== Phase 3: Reservation Lifecycle (~{estimatedCalls:N0} quick reservations, target {targetEvents:N0} events, concurrency={effectiveConcurrency}) ===");
        var phase = new PhaseResult { Name = "ReservationLifecycle" };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var eventsCreated = 0;
        var errorCount = 0;
        var opsCount = 0;
        var latencies = new List<double>();
        var statusCounts = new Dictionary<int, int>();
        var lockObj = new object();

        var semaphore = new SemaphoreSlim(effectiveConcurrency);
        var tasks = new List<Task>();
        var callIndex = 0;

        // Keep within this month and next month to satisfy validation
        var now = DateTime.UtcNow;
        var daysInRange = DateTime.DaysInMonth(now.Year, now.Month)
            + DateTime.DaysInMonth(now.AddMonths(1).Year, now.AddMonths(1).Month)
            - now.Day;

        var maxCalls = estimatedCalls * 2; // Safety limit

        while (Volatile.Read(ref eventsCreated) < targetEvents && Volatile.Read(ref callIndex) < maxCalls)
        {
            await semaphore.WaitAsync();
            var idx = Interlocked.Increment(ref callIndex) - 1;
            var roomId = roomIds[idx % roomIds.Count];

            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var dayOffset = idx % Math.Max(daysInRange, 1);
                    var baseTime = now.Date.AddDays(dayOffset).AddHours(8 + (idx % 10));

                    // Use unique organizerId per request to avoid UserMonthlyReservation tag contention
                    var organizerId = Guid.NewGuid();

                    // Mix: 80% quick reservation, 20% draft-only
                    if (idx % 5 != 0)
                    {
                        // Quick reservation (3 events)
                        var payload = new
                        {
                            roomId,
                            organizerId,
                            organizerName = $"Bench-User-{idx:D6}",
                            startTime = baseTime.ToString("O"),
                            endTime = baseTime.AddHours(1).ToString("O"),
                            purpose = $"Benchmark meeting {idx:D6}",
                            selectedEquipment = Array.Empty<string>()
                        };

                        var (resp, ms) = await client.QuickReservation(payload);
                        RecordOp(resp, ms);
                        if (resp.IsSuccessStatusCode)
                            Interlocked.Add(ref eventsCreated, 3); // draft + hold + confirm
                    }
                    else
                    {
                        // Draft only (1 event)
                        var payload = new
                        {
                            roomId,
                            organizerId,
                            organizerName = $"Bench-User-{idx:D6}",
                            startTime = baseTime.ToString("O"),
                            endTime = baseTime.AddHours(1).ToString("O"),
                            purpose = $"Benchmark draft {idx:D6}",
                            selectedEquipment = Array.Empty<string>()
                        };

                        var (resp, ms) = await client.CreateReservationDraft(payload);
                        RecordOp(resp, ms);
                        if (resp.IsSuccessStatusCode)
                            Interlocked.Increment(ref eventsCreated);
                    }

                    var current = Volatile.Read(ref eventsCreated);
                    if (current % 10000 == 0 && current > 0)
                    {
                        var elapsed = sw.Elapsed.TotalSeconds;
                        Console.WriteLine($"  Progress: ~{current:N0}/{targetEvents:N0} events ({current / elapsed:F0} events/sec)");
                    }
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref errorCount);
                    Interlocked.Increment(ref opsCount);
                    if (Volatile.Read(ref errorCount) <= 3)
                        Console.WriteLine($"  [ERROR] {ex.GetType().Name}: {ex.Message}");
                }
                finally
                {
                    semaphore.Release();
                }
            }));

            // Prevent unbounded task accumulation
            if (tasks.Count > concurrency * 10)
            {
                await Task.WhenAll(tasks);
                tasks.Clear();
            }
        }

        await Task.WhenAll(tasks);
        sw.Stop();

        foreach (var ms in latencies) phase.CommandLatency.Record(ms);
        phase.CommandLatency.Compute();
        phase.TotalOperations = Volatile.Read(ref opsCount);
        phase.TotalEventsCreated = Volatile.Read(ref eventsCreated);
        phase.ErrorCount = Volatile.Read(ref errorCount);
        phase.DurationSeconds = sw.Elapsed.TotalSeconds;
        phase.OperationsPerSecond = phase.TotalOperations / Math.Max(phase.DurationSeconds, 0.001);
        phase.EventsPerSecond = phase.TotalEventsCreated / Math.Max(phase.DurationSeconds, 0.001);
        phase.StatusCodeDistribution = statusCounts;

        Console.WriteLine($"  Completed: {phase.TotalEventsCreated:N0} events in {phase.DurationSeconds:F1}s ({phase.EventsPerSecond:F0} events/sec), errors={phase.ErrorCount}");
        Console.WriteLine($"  Latency: p50={phase.CommandLatency.P50Ms:F1}ms p95={phase.CommandLatency.P95Ms:F1}ms p99={phase.CommandLatency.P99Ms:F1}ms max={phase.CommandLatency.MaxMs:F1}ms");
        return phase;

        void RecordOp(HttpResponseMessage resp, double ms)
        {
            var code = (int)resp.StatusCode;
            lock (lockObj)
            {
                latencies.Add(ms);
                statusCounts[code] = statusCounts.GetValueOrDefault(code) + 1;
            }
            Interlocked.Increment(ref opsCount);
            if (!resp.IsSuccessStatusCode)
                Interlocked.Increment(ref errorCount);
        }
    }
}
