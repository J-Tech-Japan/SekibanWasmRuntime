using System.Text.Json;
using Xunit;

namespace SekibanWasm.Cs.Tests;

public class KenbaiCliSnapshotParityTests
{
    [Theory]
    [InlineData("KanyushaListProjection", 300)]
    [InlineData("HokenNendoShosaiListProjection", 1000)]
    public async Task SnapshotCli_ShouldProduceEquivalentProjectionState_ForNativeAndWasm(
        string projectorName,
        int maxEvents)
    {
        string tempRoot = Path.Combine(
            Path.GetTempPath(),
            $"kenbai-cli-parity-{projectorName}-{maxEvents}-{Guid.NewGuid():N}");
        string nativeOutput = Path.Combine(tempRoot, "native-run");
        string wasmOutput = Path.Combine(tempRoot, "wasm-run");

        try
        {
            await KenbaiWasmParityTestSupport.RunCliSnapshotAsync(
                nativeOutput,
                runtime: "native",
                projectorName,
                maxEvents,
                batchSize: maxEvents);
            await KenbaiWasmParityTestSupport.RunCliSnapshotAsync(
                wasmOutput,
                runtime: "wasm",
                projectorName,
                maxEvents,
                batchSize: maxEvents);

            string nativeSnapshotPath = Path.Combine(nativeOutput, "native", $"{projectorName}.snapshot.json");
            string wasmSnapshotPath = Path.Combine(wasmOutput, "wasm", $"{projectorName}.snapshot.json");

            string nativeStateJson = KenbaiWasmParityTestSupport.LoadCanonicalProjectionStateFromSnapshot(nativeSnapshotPath);
            string wasmStateJson = KenbaiWasmParityTestSupport.LoadCanonicalProjectionStateFromSnapshot(wasmSnapshotPath);
            Assert.Equal(nativeStateJson, wasmStateJson);

            SnapshotRunSummary nativeSummary = LoadSingleRunSummary(Path.Combine(nativeOutput, "snapshot-benchmark-report.json"));
            SnapshotRunSummary wasmSummary = LoadSingleRunSummary(Path.Combine(wasmOutput, "snapshot-benchmark-report.json"));

            Assert.Equal(nativeSummary.ProjectorName, wasmSummary.ProjectorName);
            Assert.Equal(nativeSummary.EventsRead, wasmSummary.EventsRead);
            Assert.Equal(nativeSummary.LastSortableUniqueId, wasmSummary.LastSortableUniqueId);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static SnapshotRunSummary LoadSingleRunSummary(string reportPath)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(reportPath));
        JsonElement run = GetProperty(document.RootElement, "runs", "Runs")[0];
        return new SnapshotRunSummary(
            GetProperty(run, "projectorName", "ProjectorName").GetString() ?? string.Empty,
            GetProperty(run, "eventsRead", "EventsRead").GetInt64(),
            GetProperty(run, "lastSortableUniqueId", "LastSortableUniqueId").GetString() ?? string.Empty);
    }

    private static JsonElement GetProperty(JsonElement element, params string[] names)
    {
        foreach (string name in names)
        {
            if (element.TryGetProperty(name, out JsonElement property))
            {
                return property;
            }
        }

        throw new KeyNotFoundException($"None of the JSON properties were found: {string.Join(", ", names)}");
    }

    private sealed record SnapshotRunSummary(
        string ProjectorName,
        long EventsRead,
        string LastSortableUniqueId);
}
