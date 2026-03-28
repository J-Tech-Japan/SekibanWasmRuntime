using System.Text;
using System.Text.Json;
using Aic.Kenbai.EventSource;
using Microsoft.Extensions.Logging.Abstractions;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Runtime;
using Sekiban.Dcb.WasmRuntime;
using Sekiban.Dcb.WasmRuntime.Wasmtime;
using Wasmtime;
using Xunit;

namespace SekibanWasm.Cs.Tests;

public class KenbaiWasmStoredSnapshotReplayTests
{
    private const string RuntimeDatabasePath = "/tmp/kenbai-wasm-api-clean/events.db";
    private const string ProjectorName = "KanyushaListProjection";
    private const string KnownBlockerSortableUniqueId = "063907072027952549901977492386";
    private const string ProgressLogPath = "/tmp/kenbai-stored-snapshot-replay.progress.log";

    [Fact]
    public async Task StoredSqliteSnapshotRestore_ShouldAdvanceThroughKnownBlockerWindow()
    {
        File.WriteAllText(ProgressLogPath, string.Empty);
        LogProgress("start");
        Assert.True(File.Exists(RuntimeDatabasePath), $"Runtime sqlite was not found: {RuntimeDatabasePath}");

        KenbaiWasmParityTestSupport.StoredProjectionSnapshotRow snapshot =
            KenbaiWasmParityTestSupport.LoadStoredProjectionSnapshotRow(ProjectorName, RuntimeDatabasePath);
        IReadOnlyList<KenbaiWasmParityTestSupport.EventRow> rows =
            KenbaiWasmParityTestSupport.LoadEventsAfterSortableUniqueId(
                snapshot.LastSortableUniqueId,
                takeCount: 283,
                databasePath: RuntimeDatabasePath);

        LogProgress($"loaded snapshot version={snapshot.EventsProcessed} last={snapshot.LastSortableUniqueId} rows={rows.Count}");
        Assert.Equal(283, rows.Count);
        Assert.Equal(KnownBlockerSortableUniqueId, rows[^1].SortableUniqueId);

        using var handle = CreateActorHostHandle();
        await using var snapshotStream = new MemoryStream(snapshot.StateData, writable: false);
        LogProgress("before restore");
        var restoreOperation = Task.Run(async () =>
            await handle.Host.RestoreSnapshotFromStreamAsync(snapshotStream, CancellationToken.None));
        Task restoreCompleted = await Task.WhenAny(restoreOperation, Task.Delay(TimeSpan.FromSeconds(15)));

        Assert.True(
            ReferenceEquals(restoreCompleted, restoreOperation),
            $"Timed out while restoring stored snapshot: {snapshot.LastSortableUniqueId}");

        var restoreResult = await restoreOperation;
        LogProgress("after restore");
        Assert.True(
            restoreResult.IsSuccess,
            restoreResult.IsSuccess ? string.Empty : restoreResult.GetException().ToString());

        for (int index = 0; index < rows.Count; index++)
        {
            KenbaiWasmParityTestSupport.EventRow row = rows[index];
            LogProgress($"before event {index + 1}/{rows.Count} {row.EventType} {row.SortableUniqueId}");
            var serializedEvent = new SerializableEvent(
                Payload: Encoding.UTF8.GetBytes(row.PayloadJson),
                SortableUniqueIdValue: row.SortableUniqueId,
                Id: Guid.NewGuid(),
                EventMetadata: new EventMetadata("stored-snapshot", row.EventType, "kenbai-stored-snapshot"),
                Tags: JsonSerializer.Deserialize<List<string>>(row.TagsJson) ?? [],
                EventPayloadName: row.EventType);

            Task operation = Task.Run(async () =>
                await handle.Host.AddSerializableEventsAsync([serializedEvent], finishedCatchUp: false));
            Task completed = await Task.WhenAny(operation, Task.Delay(TimeSpan.FromSeconds(15)));

            Assert.True(
                ReferenceEquals(completed, operation),
                $"Timed out while replaying event #{index + 1}/{rows.Count}: {row.EventType} {row.SortableUniqueId}");

            await operation;
            LogProgress($"after event {index + 1}/{rows.Count} {row.EventType} {row.SortableUniqueId}");
        }

        var metadataResult = await handle.Host.GetStateMetadataAsync(includeUnsafe: true);
        Assert.True(
            metadataResult.IsSuccess,
            metadataResult.IsSuccess ? string.Empty : metadataResult.GetException().ToString());

        ProjectionStateMetadata metadata = metadataResult.GetValue();
        Assert.Equal(snapshot.EventsProcessed + rows.Count, metadata.SafeVersion);
        Assert.Equal(rows[^1].SortableUniqueId, metadata.SafeLastSortableUniqueId);
    }

    private static void LogProgress(string message) =>
        File.AppendAllText(ProgressLogPath, $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}");

    private static ActorHostHandle CreateActorHostHandle()
    {
        var runtime = new WasmtimeRuntime();
        var moduleCache = new WasmtimeModuleCache(runtime);
        var primitiveHost = new WasmtimePrimitiveProjectionHost(
            runtime,
            moduleCache,
            new WasmtimeHostOptions
            {
                ProjectorModulePaths = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [ProjectorName] = KenbaiWasmParityTestSupport.WasmModulePath
                }
            });
        var registry = new WasmProjectorRegistry();
        registry.Register(new WasmModuleRef(
            ProjectorName: ProjectorName,
            ModulePath: KenbaiWasmParityTestSupport.WasmModulePath,
            AbiKind: "wasi-preview1",
            ModuleVersion: "1.0.0",
            ProjectorVersion: "1.0.0"));

        var host = new WasmProjectionActorHost(
            primitiveHost,
            registry,
            KenbaiDcbDomainType.GetDomainTypes(),
            KenbaiDcbDomainType.GetDomainTypes().JsonSerializerOptions,
            ProjectorName,
            NullLogger.Instance);

        return new ActorHostHandle(host, runtime);
    }

    private sealed class ActorHostHandle(WasmProjectionActorHost host, WasmtimeRuntime runtime) : IDisposable
    {
        public WasmProjectionActorHost Host { get; } = host;

        public void Dispose()
        {
            Host.Dispose();
            runtime.Dispose();
        }
    }
}
