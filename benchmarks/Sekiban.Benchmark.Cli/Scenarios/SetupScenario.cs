using Sekiban.Benchmark.Cli.Models;

namespace Sekiban.Benchmark.Cli.Scenarios;

public static class SetupScenario
{
    public static async Task<(PhaseResult Result, List<Guid> RoomIds)> RunAsync(
        HttpApiClient client,
        int reservationTargetEvents)
    {
        Console.WriteLine("\n=== Phase 1: Setup (rooms) ===");
        var phase = new PhaseResult { Name = "Setup" };
        var roomIds = new List<Guid>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var roomCount = EstimateRequiredRoomCount(reservationTargetEvents, DateTime.UtcNow);

        Console.WriteLine($"  Provisioning {roomCount} rooms for ~{reservationTargetEvents:N0} reservation events");

        for (var i = 0; i < roomCount; i++)
        {
            var roomId = Guid.NewGuid();
            var payload = new
            {
                roomId,
                name = $"Benchmark Room {i + 1:D2}",
                capacity = 5 + (i * 3),
                location = $"Building {(char)('A' + i / 5)}, Floor {i % 5 + 1}",
                equipment = i % 3 == 0 ? new[] { "Projector", "Whiteboard" } : new[] { "Whiteboard" },
                requiresApproval = false // All rooms no-approval for benchmark throughput
            };

            var resp = await client.CreateRoom(payload);
            phase.CommandLatency.Record(resp.Ms);
            var code = (int)resp.StatusCode;
            phase.StatusCodeDistribution[code] = phase.StatusCodeDistribution.GetValueOrDefault(code) + 1;

            if (resp.IsSuccessStatusCode)
            {
                roomIds.Add(roomId);
                phase.TotalEventsCreated++;
            }
            else
            {
                phase.ErrorCount++;
                Console.WriteLine($"  [WARN] Room creation failed: {resp.StatusCode} - {resp.Body}");
            }
            phase.TotalOperations++;
        }

        sw.Stop();
        phase.DurationSeconds = sw.Elapsed.TotalSeconds;
        phase.OperationsPerSecond = phase.TotalOperations / phase.DurationSeconds;
        phase.EventsPerSecond = phase.TotalEventsCreated / phase.DurationSeconds;
        phase.CommandLatency.Compute();

        Console.WriteLine($"  Created {roomIds.Count} rooms in {phase.DurationSeconds:F1}s");
        return (phase, roomIds);
    }

    private static int EstimateRequiredRoomCount(int reservationTargetEvents, DateTime nowUtc)
    {
        var benchmarkStartDate = nowUtc < nowUtc.Date.AddHours(8)
            ? nowUtc.Date
            : nowUtc.Date.AddDays(1);
        var nextMonth = nowUtc.AddMonths(1);
        var lastReservableDate = new DateTime(
            nextMonth.Year,
            nextMonth.Month,
            DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month),
            0,
            0,
            0,
            DateTimeKind.Utc);
        var daysInRange = Math.Max((lastReservableDate.Date - benchmarkStartDate.Date).Days + 1, 1);
        const int slotsPerDay = 14;
        const double averageEventsPerCall = 2.60;
        const double safetyMargin = 1.10;

        var estimatedCalls = Math.Ceiling(reservationTargetEvents / averageEventsPerCall);
        var slotsPerRoom = Math.Max(daysInRange * slotsPerDay, 1);
        var requiredRooms = (int)Math.Ceiling((estimatedCalls * safetyMargin) / slotsPerRoom);
        return Math.Max(requiredRooms, 20);
    }
}
