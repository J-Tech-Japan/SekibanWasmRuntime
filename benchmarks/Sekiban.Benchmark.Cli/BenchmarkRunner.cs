using System.Text.Json;
using Sekiban.Benchmark.Cli.Models;
using Sekiban.Benchmark.Cli.Scenarios;

namespace Sekiban.Benchmark.Cli;

public sealed class BenchmarkRunner
{
    private readonly string _baseUrl;
    private readonly string _modeLabel;
    private readonly int _totalEvents;
    private readonly int _concurrency;
    private readonly string? _outputPath;
    private readonly bool _skipSetup;

    public BenchmarkRunner(string baseUrl, string modeLabel, int totalEvents, int concurrency, string? outputPath, bool skipSetup)
    {
        _baseUrl = baseUrl;
        _modeLabel = modeLabel;
        _totalEvents = totalEvents;
        _concurrency = concurrency;
        _outputPath = outputPath;
        _skipSetup = skipSetup;
    }

    public async Task<BenchmarkResult> RunAsync()
    {
        Console.WriteLine("╔══════════════════════════════════════════════════════╗");
        Console.WriteLine($"║  Sekiban Event Sourcing Benchmark                    ║");
        Console.WriteLine($"║  Mode: {_modeLabel,-20}                       ║");
        Console.WriteLine($"║  Target: {_totalEvents,10:N0} events                      ║");
        Console.WriteLine($"║  Concurrency: {_concurrency,3}                                ║");
        Console.WriteLine($"║  URL: {_baseUrl,-38}     ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════╝");

        using var client = new HttpApiClient(_baseUrl);

        var result = new BenchmarkResult
        {
            ModeLabel = _modeLabel,
            BaseUrl = _baseUrl,
            TargetEvents = _totalEvents,
            Concurrency = _concurrency,
            StartedAtUtc = DateTime.UtcNow
        };

        var totalSw = System.Diagnostics.Stopwatch.StartNew();

        // Try to authenticate (needed for Native template with JWT auth)
        // For WASM samples using X-Debug headers, this will fail silently and that's fine.
        Console.WriteLine("\n--- Attempting authentication (for Native template) ---");
        var authenticated = await client.AuthenticateAsync();
        if (!authenticated)
            Console.WriteLine("  Auth not available (using debug headers instead)");

        // Detect correct weather endpoint path
        await client.DetectWeatherEndpointAsync();

        // Phase 1: Setup
        List<Guid> roomIds;
        if (_skipSetup)
        {
            Console.WriteLine("\n=== Phase 1: Setup (SKIPPED - loading existing rooms) ===");
            roomIds = await client.GetExistingRoomIdsAsync();
            Console.WriteLine($"  Found {roomIds.Count} existing rooms");
            if (roomIds.Count == 0)
            {
                Console.WriteLine("\nERROR: --skip-setup was specified, but no existing rooms were found. Aborting benchmark.");
                return await FinalizeAsync();
            }
        }
        else
        {
            var (setupResult, rooms) = await SetupScenario.RunAsync(client);
            result.Phases.Add(setupResult);
            roomIds = rooms;
        }

        // Budget: 60% weather (pure throughput, no contention), 40% reservation lifecycle (complex, with contention)
        var weatherEvents = (int)(_totalEvents * 0.60);
        var reservationEvents = _totalEvents - weatherEvents;

        // Phase 2: Weather Bulk
        var weatherResult = await WeatherBulkScenario.RunAsync(client, weatherEvents, _concurrency);
        result.Phases.Add(weatherResult);

        if (roomIds.Count == 0)
        {
            Console.WriteLine("\nERROR: Setup phase did not produce any rooms. Aborting benchmark before reservation/query phases.");
            return await FinalizeAsync();
        }

        // Phase 3: Reservation Lifecycle
        var reservationResult = await ReservationLifecycleScenario.RunAsync(
            client, roomIds, reservationEvents, _concurrency);
        result.Phases.Add(reservationResult);

        // Phase 4/5: Query Performance
        var queryResult = await QueryPerformanceScenario.RunAsync(client, roomIds, 50);
        result.Phases.Add(queryResult);

        return await FinalizeAsync();

        async Task<BenchmarkResult> FinalizeAsync()
        {
            totalSw.Stop();
            result.CompletedAtUtc = DateTime.UtcNow;
            result.TotalDurationSeconds = totalSw.Elapsed.TotalSeconds;
            PrintSummary(result);
            await SaveResultsAsync(result);
            return result;
        }
    }

    private void PrintSummary(BenchmarkResult result)
    {
        Console.WriteLine("\n╔══════════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                           BENCHMARK SUMMARY                             ║");
        Console.WriteLine("╠═══════════════════╦═══════════╦═══════════╦════════╦════════╦════════════╣");
        Console.WriteLine("║ Phase             ║   Events  ║  Ops/sec  ║  p50   ║  p95   ║   Errors   ║");
        Console.WriteLine("╠═══════════════════╬═══════════╬═══════════╬════════╬════════╬════════════╣");

        foreach (var phase in result.Phases)
        {
            Console.WriteLine(
                $"║ {phase.Name,-17} ║ {phase.TotalEventsCreated,9:N0} ║ {phase.OperationsPerSecond,9:F0} ║ {phase.CommandLatency.P50Ms,5:F0}ms ║ {phase.CommandLatency.P95Ms,5:F0}ms ║ {phase.ErrorCount,10} ║");
        }

        var totalEvents = result.Phases.Sum(p => p.TotalEventsCreated);
        var totalErrors = result.Phases.Sum(p => p.ErrorCount);
        Console.WriteLine("╠═══════════════════╬═══════════╬═══════════╬════════╬════════╬════════════╣");
        Console.WriteLine(
            $"║ TOTAL             ║ {totalEvents,9:N0} ║ {totalEvents / result.TotalDurationSeconds,9:F0} ║   --   ║   --   ║ {totalErrors,10} ║");
        Console.WriteLine("╚═══════════════════╩═══════════╩═══════════╩════════╩════════╩════════════╝");
        Console.WriteLine($"\nTotal wall-clock time: {result.TotalDurationSeconds:F1}s");
    }

    private async Task SaveResultsAsync(BenchmarkResult result)
    {
        var path = _outputPath ?? $"benchmark-{_modeLabel}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json";
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json);
        Console.WriteLine($"\nResults saved to: {path}");
    }
}
