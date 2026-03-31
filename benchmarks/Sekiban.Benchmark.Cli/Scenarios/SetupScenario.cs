using Sekiban.Benchmark.Cli.Models;

namespace Sekiban.Benchmark.Cli.Scenarios;

public static class SetupScenario
{
    public static async Task<(PhaseResult Result, List<Guid> RoomIds)> RunAsync(HttpApiClient client)
    {
        Console.WriteLine("\n=== Phase 1: Setup (rooms) ===");
        var phase = new PhaseResult { Name = "Setup" };
        var roomIds = new List<Guid>();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Create 20 rooms
        for (var i = 0; i < 20; i++)
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

            var (resp, ms) = await client.CreateRoom(payload);
            phase.CommandLatency.Record(ms);
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
                Console.WriteLine($"  [WARN] Room creation failed: {resp.StatusCode} - {await resp.Content.ReadAsStringAsync()}");
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
}
