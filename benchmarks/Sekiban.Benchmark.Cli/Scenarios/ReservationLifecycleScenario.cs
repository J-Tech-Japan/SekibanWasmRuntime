using Sekiban.Benchmark.Cli.Models;

namespace Sekiban.Benchmark.Cli.Scenarios;

public static class ReservationLifecycleScenario
{
    private enum ReservationMixMode
    {
        Mixed,
        QuickOnly,
        DraftOnly
    }

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
        // Each request uses a unique user ID (via X-Debug-User-Id header) to avoid
        // UserMonthlyReservation tag contention
        var effectiveConcurrency = concurrency;
        var estimatedCalls = targetEvents / 3;
        var mixMode = ResolveMixMode();

        Console.WriteLine($"\n=== Phase 3: Reservation Lifecycle (~{estimatedCalls:N0} quick reservations, target {targetEvents:N0} events, concurrency={effectiveConcurrency}, mix={mixMode}) ===");
        var phase = new PhaseResult { Name = "ReservationLifecycle" };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var eventsCreated = 0;
        var errorCount = 0;
        var opsCount = 0;
        var latencies = new List<double>();
        var statusCounts = new Dictionary<int, int>();
        var lastReportedEvents = 0;
        var lastReportedErrors = 0;
        var lastReportedOps = 0;
        var lastReportedAt = TimeSpan.Zero;
        var progressLock = new object();

        var semaphore = new SemaphoreSlim(effectiveConcurrency);
        var tasks = new List<Task>();
        var callIndex = 0;

        var now = DateTime.UtcNow;
        // Keep within this month and next month to satisfy validation.
        // If today's benchmark slots (08:00-21:00 UTC) have already started, advance to tomorrow
        // so we don't generate a fixed block of "past reservation" failures.
        var benchmarkStartDate = now < now.Date.AddHours(8)
            ? now.Date
            : now.Date.AddDays(1);
        var lastReservableDate = new DateTime(
            now.AddMonths(1).Year,
            now.AddMonths(1).Month,
            DateTime.DaysInMonth(now.AddMonths(1).Year, now.AddMonths(1).Month),
            0,
            0,
            0,
            DateTimeKind.Utc);
        var daysInRange = Math.Max((lastReservableDate.Date - benchmarkStartDate.Date).Days + 1, 1);

        // Generate enough unique time slots to avoid room reservation conflicts.
        // Each reservation occupies 1 hour, so we can fit ~14 per day (8:00-22:00).
        // Total unique slots = rooms * days * slotsPerDay.
        var slotsPerDay = 14;
        var totalSlots = roomIds.Count * Math.Max(daysInRange, 1) * slotsPerDay;

        var maxCalls = estimatedCalls * 2; // Safety limit

        while (Volatile.Read(ref eventsCreated) < targetEvents && Volatile.Read(ref callIndex) < maxCalls)
        {
            ReportProgressIfNeeded();
            await semaphore.WaitAsync();
            var idx = Interlocked.Increment(ref callIndex) - 1;

            // Distribute across rooms, days, and time slots to minimize conflicts.
            // Each idx maps to a unique (room, day, hour) triple where possible.
            var slotIndex = idx % Math.Max(totalSlots, 1);
            var roomIndex = slotIndex % roomIds.Count;
            var remaining = slotIndex / roomIds.Count;
            var dayOffset = remaining / slotsPerDay % Math.Max(daysInRange, 1);
            var hourSlot = remaining % slotsPerDay;
            var roomId = roomIds[roomIndex];

            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var baseTime = benchmarkStartDate.AddDays(dayOffset).AddHours(8 + hourSlot);

                    // Use unique organizerId per request to avoid UserMonthlyReservation tag contention
                    var organizerId = Guid.NewGuid();

                    if (ShouldUseQuickReservation(idx, mixMode))
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

                        var resp = await client.QuickReservation(payload);
                        RecordOp(resp);
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

                        var resp = await client.CreateReservationDraftUniqueUser(payload);
                        RecordOp(resp);
                        if (resp.IsSuccessStatusCode)
                            Interlocked.Increment(ref eventsCreated);
                    }

                    ReportProgressIfNeeded();
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

            // Prevent unbounded task accumulation and refresh auth token periodically
            if (tasks.Count > concurrency * 10)
            {
                await Task.WhenAll(tasks);
                tasks.Clear();
                await client.EnsureAuthenticatedAsync();
            }
        }

        await Task.WhenAll(tasks);
        sw.Stop();
        ReportProgress(force: true);

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

        void RecordOp(MeasuredResponse resp)
        {
            var code = (int)resp.StatusCode;
            lock (latencies)
                latencies.Add(resp.Ms);
            lock (statusCounts)
                statusCounts[code] = statusCounts.GetValueOrDefault(code) + 1;
            Interlocked.Increment(ref opsCount);
            if (!resp.IsSuccessStatusCode)
            {
                Interlocked.Increment(ref errorCount);
                var currentErrors = Volatile.Read(ref errorCount);
                if (currentErrors <= 5)
                {
                    var body = string.IsNullOrWhiteSpace(resp.Body) ? "<empty>" : resp.Body;
                    if (body.Length > 400)
                    {
                        body = body[..400];
                    }

                    Console.WriteLine($"  [HTTP {(int)resp.StatusCode}] {body}");
                }
            }
        }

        void ReportProgressIfNeeded()
        {
            lock (progressLock)
            {
                var elapsed = sw.Elapsed;
                if (elapsed - lastReportedAt >= TimeSpan.FromSeconds(15))
                {
                    ReportProgressCore(elapsed, force: false);
                }
            }
        }

        void ReportProgress(bool force = false)
        {
            lock (progressLock)
            {
                ReportProgressCore(sw.Elapsed, force);
            }
        }

        void ReportProgressCore(TimeSpan elapsed, bool force)
        {
            if (!force && elapsed - lastReportedAt < TimeSpan.FromSeconds(15))
            {
                return;
            }

            var currentEvents = Volatile.Read(ref eventsCreated);
            var currentErrors = Volatile.Read(ref errorCount);
            var currentOps = Volatile.Read(ref opsCount);
            var intervalSeconds = Math.Max((elapsed - lastReportedAt).TotalSeconds, 0.001);
            var intervalEvents = currentEvents - lastReportedEvents;
            var intervalErrors = currentErrors - lastReportedErrors;
            var intervalOps = currentOps - lastReportedOps;
            var averageEventsPerSecond = currentEvents / Math.Max(elapsed.TotalSeconds, 0.001);
            var intervalEventsPerSecond = intervalEvents / intervalSeconds;

            Console.WriteLine(
                $"  Progress: events={currentEvents:N0}/{targetEvents:N0} ops={currentOps:N0} " +
                $"avg={averageEventsPerSecond:F0} eps interval={intervalEventsPerSecond:F0} eps " +
                $"deltaEvents={intervalEvents:N0} deltaOps={intervalOps:N0} errors={currentErrors:N0} deltaErrors={intervalErrors:N0}");

            if (intervalEvents == 0 && currentEvents < targetEvents)
            {
                Console.WriteLine("  [STALL] No reservation events completed in the last interval.");
            }

            lastReportedAt = elapsed;
            lastReportedEvents = currentEvents;
            lastReportedErrors = currentErrors;
            lastReportedOps = currentOps;
        }
    }

    private static ReservationMixMode ResolveMixMode()
    {
        var raw = Environment.GetEnvironmentVariable("BENCHMARK_RESERVATION_MODE");
        return raw?.Trim().ToLowerInvariant() switch
        {
            "quick-only" => ReservationMixMode.QuickOnly,
            "draft-only" => ReservationMixMode.DraftOnly,
            _ => ReservationMixMode.Mixed
        };
    }

    private static bool ShouldUseQuickReservation(int idx, ReservationMixMode mixMode) =>
        mixMode switch
        {
            ReservationMixMode.QuickOnly => true,
            ReservationMixMode.DraftOnly => false,
            _ => idx % 5 != 0
        };
}
