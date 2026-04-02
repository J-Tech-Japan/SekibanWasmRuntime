using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using Sekiban.Dcb;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Orleans.Serialization;
using Sekiban.Dcb.Primitives;
using Sekiban.Dcb.Snapshots;
using Sekiban.Dcb.WasmRuntime;
using Sekiban.Dcb.WasmRuntime.Wasmtime;
using SekibanWasm.Cs.Domain;
using SekibanWasm.Cs.Domain.Weather;
using Xunit;

namespace SekibanWasm.Cs.Tests;

public class WasmProjectionActorHostTests
{
    private static readonly JsonSerializerOptions DomainJsonOptions = DomainType.GetDomainTypes().JsonSerializerOptions;

    [Fact]
    public async Task ExecuteListQueryAsync_Extracts_Items_And_Pagination_Metadata()
    {
        var instance = new StubPrimitiveProjectionInstance
        {
            ListQueryResponseJson = """
                                    {"totalCount":2,"totalPages":1,"currentPage":1,"pageSize":20,"items":[{"forecastId":"1"},{"forecastId":"2"}]}
                                    """
        };
        var host = CreateHost(instance);

        var result = await host.ExecuteListQueryAsync(
            new SerializableQueryParameter
            {
                QueryTypeName = "GetWeatherForecastListQuery",
                CompressedQueryJson = await CompressStringAsync("{}")
            },
            safeVersion: null,
            safeThreshold: null,
            safeThresholdTime: null,
            unsafeVersion: null);

        Assert.True(result.IsSuccess);
        var payload = result.GetValue();
        Assert.Equal(2, payload.TotalCount);
        Assert.Equal(1, payload.TotalPages);
        Assert.Equal(1, payload.CurrentPage);
        Assert.Equal(20, payload.PageSize);
        Assert.Equal(
            """[{"forecastId":"1"},{"forecastId":"2"}]""",
            await DecompressToStringAsync(payload.CompressedItemsJson));
    }

    [Fact]
    public async Task Snapshot_Restores_State_And_Metadata()
    {
        var instance = new StubPrimitiveProjectionInstance();
        var host = CreateHost(instance);
        var eventId = Guid.NewGuid();

        await host.AddSerializableEventsAsync(
        [
            new SerializableEvent(
                Payload: Encoding.UTF8.GetBytes("""{"forecastId":"f-1","location":"Tokyo"}"""),
                SortableUniqueIdValue: "20260316010101000000000000000001",
                Id: eventId,
                EventMetadata: new EventMetadata("cause", "correlation", "user"),
                Tags: ["WeatherForecast:f-1"],
                EventPayloadName: "WeatherForecastCreated")
        ]);

        await using var snapshotStream = new MemoryStream();
        var writeResult = await host.WriteSnapshotToStreamAsync(snapshotStream, canGetUnsafeState: true, CancellationToken.None);
        Assert.True(writeResult.IsSuccess);

        snapshotStream.Position = 0;

        var restoredInstance = new StubPrimitiveProjectionInstance();
        var restoredHost = CreateHost(restoredInstance);
        var restoreResult = await restoredHost.RestoreSnapshotFromStreamAsync(snapshotStream, CancellationToken.None);

        Assert.True(
            restoreResult.IsSuccess,
            restoreResult.IsSuccess ? string.Empty : restoreResult.GetException().ToString());

        var metadataResult = await restoredHost.GetStateMetadataAsync(includeUnsafe: true);
        Assert.True(metadataResult.IsSuccess);
        Assert.Equal(1, metadataResult.GetValue().SafeVersion);
        Assert.Equal("20260316010101000000000000000001", metadataResult.GetValue().SafeLastSortableUniqueId);

        var queryResult = await restoredHost.ExecuteQueryAsync(
            new SerializableQueryParameter
            {
                QueryTypeName = "EchoState",
                CompressedQueryJson = await CompressStringAsync("{}")
            },
            safeVersion: null,
            safeThreshold: null,
            safeThresholdTime: null,
            unsafeVersion: null);

        Assert.True(queryResult.IsSuccess);
        Assert.Equal(
            """{"forecastId":"f-1","location":"Tokyo"}""",
            await DecompressToStringAsync(queryResult.GetValue().CompressedResultJson));
    }

    [Fact]
    public async Task Snapshot_RoundTrip_ShouldNotRequire_HostSideProjectorRegistry()
    {
        var instance = new StubPrimitiveProjectionInstance();
        var host = CreateHost(instance, CreateDomainTypesWithoutProjectorRegistry());
        var eventId = Guid.NewGuid();

        await host.AddSerializableEventsAsync(
        [
            new SerializableEvent(
                Payload: Encoding.UTF8.GetBytes("""{"forecastId":"f-2","location":"Osaka"}"""),
                SortableUniqueIdValue: "20260316010101000000000000000002",
                Id: eventId,
                EventMetadata: new EventMetadata("cause", "correlation", "user"),
                Tags: ["WeatherForecast:f-2"],
                EventPayloadName: "WeatherForecastCreated")
        ]);

        await using var snapshotStream = new MemoryStream();
        var writeResult = await host.WriteSnapshotToStreamAsync(snapshotStream, canGetUnsafeState: true, CancellationToken.None);
        Assert.True(
            writeResult.IsSuccess,
            writeResult.IsSuccess ? string.Empty : writeResult.GetException().ToString());

        snapshotStream.Position = 0;

        var restoredInstance = new StubPrimitiveProjectionInstance();
        var restoredHost = CreateHost(restoredInstance, CreateDomainTypesWithoutProjectorRegistry());
        var restoreResult = await restoredHost.RestoreSnapshotFromStreamAsync(snapshotStream, CancellationToken.None);

        Assert.True(
            restoreResult.IsSuccess,
            restoreResult.IsSuccess ? string.Empty : restoreResult.GetException().ToString());

        var metadataResult = await restoredHost.GetStateMetadataAsync(includeUnsafe: true);
        Assert.True(metadataResult.IsSuccess);
        Assert.Equal(1, metadataResult.GetValue().SafeVersion);
        Assert.Equal("20260316010101000000000000000002", metadataResult.GetValue().SafeLastSortableUniqueId);
    }

    [Fact]
    public async Task Snapshot_RoundTrip_ShouldUseUtf8StatePath()
    {
        var instance = new StubPrimitiveProjectionInstance
        {
            ThrowOnSerializeState = true,
            ThrowOnRestoreState = true
        };
        var host = CreateHost(instance);

        await host.AddSerializableEventsAsync(
        [
            new SerializableEvent(
                Payload: Encoding.UTF8.GetBytes("""{"forecastId":"f-3","location":"Nagoya"}"""),
                SortableUniqueIdValue: "20260316010101000000000000000003",
                Id: Guid.NewGuid(),
                EventMetadata: new EventMetadata("cause", "correlation", "user"),
                Tags: ["WeatherForecast:f-3"],
                EventPayloadName: "WeatherForecastCreated")
        ]);

        await using var snapshotStream = new MemoryStream();
        var writeResult = await host.WriteSnapshotToStreamAsync(snapshotStream, canGetUnsafeState: true, CancellationToken.None);
        Assert.True(
            writeResult.IsSuccess,
            writeResult.IsSuccess ? string.Empty : writeResult.GetException().ToString());
        Assert.Equal(0, instance.SerializeStateCallCount);
        Assert.Equal(1, instance.SerializeStateUtf8CallCount);

        snapshotStream.Position = 0;

        var restoredInstance = new StubPrimitiveProjectionInstance
        {
            ThrowOnRestoreState = true
        };
        var restoredHost = CreateHost(restoredInstance);
        var restoreResult = await restoredHost.RestoreSnapshotFromStreamAsync(snapshotStream, CancellationToken.None);

        Assert.True(
            restoreResult.IsSuccess,
            restoreResult.IsSuccess ? string.Empty : restoreResult.GetException().ToString());
        Assert.Equal(0, restoredInstance.RestoreStateCallCount);
        Assert.Equal(1, restoredInstance.RestoreStateUtf8CallCount);
    }

    [Fact]
    public async Task Snapshot_Write_ShouldUse_CompressedWasmPayload_Format()
    {
        var instance = new StubPrimitiveProjectionInstance();
        var host = CreateHost(instance);

        await host.AddSerializableEventsAsync(
        [
            new SerializableEvent(
                Payload: Encoding.UTF8.GetBytes("""{"forecastId":"f-4","location":"Sapporo"}"""),
                SortableUniqueIdValue: "20260316010101000000000000000004",
                Id: Guid.NewGuid(),
                EventMetadata: new EventMetadata("cause", "correlation", "user"),
                Tags: ["WeatherForecast:f-4"],
                EventPayloadName: "WeatherForecastCreated")
        ]);

        await using var snapshotStream = new MemoryStream();
        var writeResult = await host.WriteSnapshotToStreamAsync(snapshotStream, canGetUnsafeState: true, CancellationToken.None);

        Assert.True(writeResult.IsSuccess);
        snapshotStream.Position = 0;

        var envelope = await JsonSerializer.DeserializeAsync<SerializableMultiProjectionStateEnvelope>(
            snapshotStream,
            DomainJsonOptions);
        Assert.NotNull(envelope);
        Assert.NotNull(envelope!.InlineState);
        Assert.Equal(
            "Sekiban.Dcb.WasmRuntime.WasmCompressedProjectionState",
            envelope.InlineState!.MultiProjectionPayloadType);
        Assert.True(envelope.InlineState.CompressedSizeBytes > 0);
        Assert.True(envelope.InlineState.OriginalSizeBytes >= envelope.InlineState.CompressedSizeBytes);
    }

    [Fact]
    public async Task Snapshot_Restore_ShouldRemainCompatible_WithLegacyWasmStateSnapshot_Format()
    {
        var legacySnapshot = new WasmStateSnapshot(
            StateJson: null,
            SafeVersion: 3,
            UnsafeVersion: 3,
            SafeLastSortableUniqueId: "20260316010101000000000000000005",
            LastSortableUniqueId: "20260316010101000000000000000005",
            LastEventId: Guid.NewGuid(),
            ProjectorVersion: "1.0.0",
            TagProjector: "WeatherForecastMultiProjection",
            CompressedStateJson: await CompressStringAsync("""{"forecastId":"legacy","location":"Kyoto"}"""));
        var legacyInlineState = SerializableMultiProjectionState.FromBytes(
            payload: JsonSerializer.SerializeToUtf8Bytes(legacySnapshot, DomainJsonOptions),
            multiProjectionPayloadType: typeof(WasmStateSnapshot).FullName ?? nameof(WasmStateSnapshot),
            projectorName: "WeatherForecastMultiProjection",
            projectorVersion: "1.0.0",
            lastSortableUniqueId: legacySnapshot.LastSortableUniqueId ?? string.Empty,
            lastEventId: legacySnapshot.LastEventId ?? Guid.Empty,
            version: legacySnapshot.UnsafeVersion,
            isCatchedUp: true,
            isSafeState: true);
        var legacyEnvelope = new SerializableMultiProjectionStateEnvelope(false, legacyInlineState, null);

        await using var snapshotStream = new MemoryStream();
        await JsonSerializer.SerializeAsync(snapshotStream, legacyEnvelope, DomainJsonOptions);
        snapshotStream.Position = 0;

        var restoredInstance = new StubPrimitiveProjectionInstance
        {
            ThrowOnRestoreState = true
        };
        var restoredHost = CreateHost(restoredInstance);
        var restoreResult = await restoredHost.RestoreSnapshotFromStreamAsync(snapshotStream, CancellationToken.None);

        Assert.True(
            restoreResult.IsSuccess,
            restoreResult.IsSuccess ? string.Empty : restoreResult.GetException().ToString());
        Assert.Equal("""{"forecastId":"legacy","location":"Kyoto"}""", restoredInstance.StateJson);
        Assert.Equal(0, restoredInstance.RestoreStateCallCount);
        Assert.Equal(1, restoredInstance.RestoreStateUtf8CallCount);
    }

    [Fact]
    public async Task KanyushaListProjection_Should_Apply_NendoKanyuMoushikomiSaikaied()
    {
        var instance = new StubPrimitiveProjectionInstance();
        var registry = new WasmProjectorRegistry();
        registry.Register(new WasmModuleRef(
            ProjectorName: "KanyushaListProjection",
            ModulePath: "/tmp/kanyusha-list.wasm",
            AbiKind: "wasi-preview1",
            ModuleVersion: "1.0.0",
            ProjectorVersion: "1.0.0"));

        var host = new WasmProjectionActorHost(
            new StubPrimitiveProjectionHost(instance),
            registry,
            DomainType.GetDomainTypes(),
            DomainJsonOptions,
            "KanyushaListProjection",
            NullLogger.Instance);

        await host.AddSerializableEventsAsync(
        [
            new SerializableEvent(
                Payload: Encoding.UTF8.GetBytes(
                    """{"kanyushaNo":{"value":12015},"nendoKanyuId":{"value":"7e2cab8b-4241-4f4e-aac3-494ed2084b82"},"keiyakuId":{"value":"d6d0ea72-3fd0-4290-918d-db29d13dc94d"},"hokenNendoId":{"id":14},"saikaiDate":"2026-03-27T19:03:14.740719+00:00"}"""),
                SortableUniqueIdValue: "063910234994742339001698825563",
                Id: Guid.NewGuid(),
                EventMetadata: new EventMetadata("test", "test", "test"),
                Tags:
                [
                    "Kanyusha:12015",
                    "NendoKanyu:7e2cab8b-4241-4f4e-aac3-494ed2084b82",
                    "Keiyaku:d6d0ea72-3fd0-4290-918d-db29d13dc94d"
                ],
                EventPayloadName: "NendoKanyuMoushikomiSaikaied")
        ]);

        Assert.Contains("saikaiDate", instance.StateJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddSerializableEventsAsync_ShouldUseBatchApply_DuringCatchUp()
    {
        var instance = new StubPrimitiveProjectionInstance();
        var host = CreateHost(instance);

        await host.AddSerializableEventsAsync(
        [
            new SerializableEvent(
                Payload: Encoding.UTF8.GetBytes("""{"forecastId":"f-1","location":"Tokyo"}"""),
                SortableUniqueIdValue: "20260316010101000000000000000001",
                Id: Guid.NewGuid(),
                EventMetadata: new EventMetadata("cause", "correlation", "user"),
                Tags: ["WeatherForecast:f-1"],
                EventPayloadName: "WeatherForecastCreated"),
            new SerializableEvent(
                Payload: Encoding.UTF8.GetBytes("""{"forecastId":"f-2","location":"Osaka"}"""),
                SortableUniqueIdValue: "20260316010101000000000000000002",
                Id: Guid.NewGuid(),
                EventMetadata: new EventMetadata("cause", "correlation", "user"),
                Tags: ["WeatherForecast:f-2"],
                EventPayloadName: "WeatherForecastCreated")
        ], finishedCatchUp: false);

        Assert.Equal(1, instance.ApplyEventsBatchCallCount);
        Assert.Equal(2, instance.BatchAppliedEventCount);
        Assert.Equal(0, instance.ApplyEventCallCount);
    }

    [Fact]
    public async Task AddSerializableEventsAsync_ShouldSerializeCatchUpAcrossProjectors()
    {
        var tracker = new BatchApplyTracker();
        var firstInstance = new BlockingBatchProjectionInstance(tracker);
        var secondInstance = new BlockingBatchProjectionInstance(tracker);
        using var catchUpGate = new SemaphoreSlim(1, 1);
        var host = new NamedPrimitiveProjectionHost(new Dictionary<string, IPrimitiveProjectionInstance>
        {
            ["WeatherForecastMultiProjection"] = firstInstance,
            ["ReservationListProjection"] = secondInstance
        });

        var firstHost = CreateHost(host, "WeatherForecastMultiProjection", catchUpGate: catchUpGate);
        var secondHost = CreateHost(host, "ReservationListProjection", catchUpGate: catchUpGate);
        var events = CreateBatchEvents();

        var firstTask = Task.Run(() => firstHost.AddSerializableEventsAsync(events, finishedCatchUp: false));
        Assert.True(firstInstance.BatchEntered.Wait(TimeSpan.FromSeconds(5)));

        var secondTask = Task.Run(() => secondHost.AddSerializableEventsAsync(events, finishedCatchUp: false));
        await Task.Delay(150);
        Assert.False(secondInstance.BatchEntered.IsSet);

        firstInstance.AllowCompletion.Set();
        await firstTask;

        Assert.True(secondInstance.BatchEntered.Wait(TimeSpan.FromSeconds(5)));
        secondInstance.AllowCompletion.Set();
        await secondTask;

        Assert.Equal(1, tracker.MaxConcurrentBatchApplies);
    }

    [Fact]
    public async Task CompactSafeHistory_ShouldRecreateInstance_AndPreserveState()
    {
        var first = new StubPrimitiveProjectionInstance();
        var second = new StubPrimitiveProjectionInstance();
        var host = new WasmProjectionActorHost(
            new FactoryPrimitiveProjectionHost([first, second]),
            CreateRegistry(),
            DomainType.GetDomainTypes(),
            DomainJsonOptions,
            "WeatherForecastMultiProjection",
            NullLogger.Instance);

        await host.AddSerializableEventsAsync(
        [
            new SerializableEvent(
                Payload: Encoding.UTF8.GetBytes("""{"forecastId":"f-1","location":"Tokyo"}"""),
                SortableUniqueIdValue: "20260316010101000000000000000001",
                Id: Guid.NewGuid(),
                EventMetadata: new EventMetadata("cause", "correlation", "user"),
                Tags: ["WeatherForecast:f-1"],
                EventPayloadName: "WeatherForecastCreated")
        ]);

        host.CompactSafeHistory();

        Assert.True(first.IsDisposed);
        Assert.False(second.IsDisposed);

        var queryResult = await host.ExecuteQueryAsync(
            new SerializableQueryParameter
            {
                QueryTypeName = "EchoState",
                CompressedQueryJson = await CompressStringAsync("{}")
            },
            safeVersion: null,
            safeThreshold: null,
            safeThresholdTime: null,
            unsafeVersion: null);

        Assert.True(queryResult.IsSuccess);
        Assert.Equal(
            """{"forecastId":"f-1","location":"Tokyo"}""",
            await DecompressToStringAsync(queryResult.GetValue().CompressedResultJson));
    }

    private static WasmProjectionActorHost CreateHost(
        IPrimitiveProjectionInstance instance,
        DcbDomainTypes? domainTypes = null,
        string projectorName = "WeatherForecastMultiProjection",
        SemaphoreSlim? catchUpGate = null)
    {
        return new WasmProjectionActorHost(
            new StubPrimitiveProjectionHost(instance),
            CreateRegistry(),
            domainTypes ?? DomainType.GetDomainTypes(),
            DomainJsonOptions,
            projectorName,
            NullLogger.Instance,
            catchUpGate: catchUpGate);
    }

    private static WasmProjectionActorHost CreateHost(
        IPrimitiveProjectionHost host,
        string projectorName,
        DcbDomainTypes? domainTypes = null,
        SemaphoreSlim? catchUpGate = null)
    {
        return new WasmProjectionActorHost(
            host,
            CreateRegistry(),
            domainTypes ?? DomainType.GetDomainTypes(),
            DomainJsonOptions,
            projectorName,
            NullLogger.Instance,
            catchUpGate: catchUpGate);
    }

    private static WasmProjectorRegistry CreateRegistry()
    {
        var registry = new WasmProjectorRegistry();
        registry.Register(new WasmModuleRef(
            ProjectorName: "WeatherForecastMultiProjection",
            ModulePath: "/tmp/weather.wasm",
            AbiKind: "wasi-preview1",
            ModuleVersion: "1.0.0",
            ProjectorVersion: "1.0.0"));
        registry.Register(new WasmModuleRef(
            ProjectorName: "ReservationListProjection",
            ModulePath: "/tmp/reservation.wasm",
            AbiKind: "wasi-preview1",
            ModuleVersion: "1.0.0",
            ProjectorVersion: "1.0.0"));
        return registry;
    }

    private static IReadOnlyList<SerializableEvent> CreateBatchEvents() =>
    [
        new SerializableEvent(
            Payload: Encoding.UTF8.GetBytes("""{"forecastId":"f-1","location":"Tokyo"}"""),
            SortableUniqueIdValue: "20260316010101000000000000000011",
            Id: Guid.NewGuid(),
            EventMetadata: new EventMetadata("cause", "correlation", "user"),
            Tags: ["WeatherForecast:f-1"],
            EventPayloadName: "WeatherForecastCreated"),
        new SerializableEvent(
            Payload: Encoding.UTF8.GetBytes("""{"forecastId":"f-2","location":"Osaka"}"""),
            SortableUniqueIdValue: "20260316010101000000000000000012",
            Id: Guid.NewGuid(),
            EventMetadata: new EventMetadata("cause", "correlation", "user"),
            Tags: ["WeatherForecast:f-2"],
            EventPayloadName: "WeatherForecastCreated")
    ];

    private static DcbDomainTypes CreateDomainTypesWithoutProjectorRegistry() =>
        DomainType.GetDomainTypes() with
        {
            MultiProjectorTypes = new AotMultiProjectorTypes()
        };

    private static async Task<byte[]> CompressStringAsync(string value)
    {
        await using var output = new MemoryStream();
        await using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            await gzip.WriteAsync(Encoding.UTF8.GetBytes(value));
        }

        return output.ToArray();
    }

    private static async Task<string> DecompressToStringAsync(byte[] compressed)
    {
        await using var input = new MemoryStream(compressed);
        await using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private sealed class StubPrimitiveProjectionHost(IPrimitiveProjectionInstance instance) : IPrimitiveProjectionHost
    {
        private readonly IPrimitiveProjectionInstance _instance = instance;

        public IPrimitiveProjectionInstance CreateInstance(string projectorName) => _instance;
    }

    private sealed class NamedPrimitiveProjectionHost(IReadOnlyDictionary<string, IPrimitiveProjectionInstance> instances)
        : IPrimitiveProjectionHost
    {
        private readonly IReadOnlyDictionary<string, IPrimitiveProjectionInstance> _instances = instances;

        public IPrimitiveProjectionInstance CreateInstance(string projectorName) =>
            _instances.TryGetValue(projectorName, out var instance)
                ? instance
                : throw new InvalidOperationException($"No instance registered for {projectorName}.");
    }

    private sealed class FactoryPrimitiveProjectionHost(IEnumerable<StubPrimitiveProjectionInstance> instances) :
        IPrimitiveProjectionHost,
        IFreshPrimitiveProjectionHost
    {
        private readonly Queue<StubPrimitiveProjectionInstance> _instances = new(instances);

        public IPrimitiveProjectionInstance CreateInstance(string projectorName) => CreateFreshInstance(projectorName);

        public IPrimitiveProjectionInstance CreateFreshInstance(string projectorName)
        {
            if (_instances.Count == 0)
            {
                throw new InvalidOperationException("No more stub instances registered.");
            }

            return _instances.Dequeue();
        }
    }

    private sealed class StubPrimitiveProjectionInstance :
        IPrimitiveProjectionInstance,
        ISerializableEventBatchProjectionInstance
    {
        public string StateJson { get; private set; } = "{}";
        public string QueryResponseJson { get; set; } = string.Empty;
        public string ListQueryResponseJson { get; set; } = "[]";
        public int ApplyEventCallCount { get; private set; }
        public int ApplyEventsBatchCallCount { get; private set; }
        public int BatchAppliedEventCount { get; private set; }
        public int SerializeStateCallCount { get; private set; }
        public int SerializeStateUtf8CallCount { get; private set; }
        public int RestoreStateCallCount { get; private set; }
        public int RestoreStateUtf8CallCount { get; private set; }
        public bool ThrowOnSerializeState { get; set; }
        public bool ThrowOnRestoreState { get; set; }
        public bool IsDisposed { get; private set; }

        public void ApplyEvent(
            string eventType,
            string eventPayloadJson,
            IReadOnlyList<string> tags,
            string? sortableUniqueId)
        {
            ApplyEventCallCount++;
            ApplyEventCore(eventType, eventPayloadJson);
        }

        public void ApplyEvents(IReadOnlyList<PrimitiveProjectionEventEnvelope> events)
        {
            ApplyEventsBatchCallCount++;
            BatchAppliedEventCount += events.Count;
            foreach (var ev in events)
            {
                ApplyEventCore(ev.EventType, ev.EventPayloadJson);
            }
        }

        public void ApplySerializableEvents(IReadOnlyList<SerializableEvent> events)
        {
            ApplyEventsBatchCallCount++;
            BatchAppliedEventCount += events.Count;
            foreach (var ev in events)
            {
                ApplyEventCore(ev.EventPayloadName, Encoding.UTF8.GetString(ev.Payload));
            }
        }

        private void ApplyEventCore(string eventType, string eventPayloadJson)
        {
            if (eventType == nameof(WeatherForecastCreated))
            {
                using JsonDocument payload = JsonDocument.Parse(eventPayloadJson);
                string forecastId = payload.RootElement.GetProperty("forecastId").GetString()
                    ?? throw new InvalidOperationException("forecastId is required.");
                string location = payload.RootElement.GetProperty("location").GetString()
                    ?? throw new InvalidOperationException("location is required.");
                StateJson = JsonSerializer.Serialize(
                    new WeatherForecastMultiProjection
                    {
                        Forecasts = new Dictionary<string, WeatherForecastItem>
                        {
                            [forecastId] = new(
                                forecastId,
                                location,
                                0,
                                string.Empty,
                                DateTimeOffset.UnixEpoch)
                        }
                    });
                return;
            }

            StateJson = eventPayloadJson;
        }

        public string ExecuteQuery(string queryType, string queryParamsJson)
        {
            if (!string.IsNullOrEmpty(QueryResponseJson))
            {
                return QueryResponseJson;
            }

            using JsonDocument state = JsonDocument.Parse(StateJson);
            JsonElement item = state.RootElement;
            if (!state.RootElement.TryGetProperty("forecasts", out JsonElement forecasts) &&
                !state.RootElement.TryGetProperty("Forecasts", out forecasts))
            {
                forecasts = default;
            }

            if (forecasts.ValueKind == JsonValueKind.Object)
            {
                item = forecasts.EnumerateObject().First().Value;
            }

            string forecastId = TryGetString(item, "forecastId", "ForecastId");
            string location = TryGetString(item, "location", "Location");

            return JsonSerializer.Serialize(
                new
                {
                    forecastId,
                    location
                });
        }

        public string ExecuteListQuery(string queryType, string queryParamsJson) => ListQueryResponseJson;

        public string SerializeState()
        {
            SerializeStateCallCount++;
            if (ThrowOnSerializeState)
            {
                throw new InvalidOperationException("String snapshot path should not be used.");
            }

            return StateJson;
        }

        public byte[] SerializeStateUtf8()
        {
            SerializeStateUtf8CallCount++;
            return Encoding.UTF8.GetBytes(StateJson);
        }

        public void RestoreState(string stateJson)
        {
            RestoreStateCallCount++;
            if (ThrowOnRestoreState)
            {
                throw new InvalidOperationException("String restore path should not be used.");
            }

            StateJson = stateJson;
        }

        public void RestoreStateUtf8(byte[] stateJsonUtf8)
        {
            RestoreStateUtf8CallCount++;
            StateJson = Encoding.UTF8.GetString(stateJsonUtf8);
        }

        public void Dispose()
        {
            IsDisposed = true;
        }

        private static string TryGetString(JsonElement element, string camelCaseName, string pascalCaseName)
        {
            if (!element.TryGetProperty(camelCaseName, out JsonElement property) &&
                !element.TryGetProperty(pascalCaseName, out property))
            {
                return string.Empty;
            }

            return property.GetString() ?? string.Empty;
        }
    }

    private sealed class BlockingBatchProjectionInstance(BatchApplyTracker tracker) :
        IPrimitiveProjectionInstance,
        ISerializableEventBatchProjectionInstance
    {
        private readonly BatchApplyTracker _tracker = tracker;

        public ManualResetEventSlim BatchEntered { get; } = new(false);
        public ManualResetEventSlim AllowCompletion { get; } = new(false);

        public void ApplyEvent(
            string eventType,
            string eventPayloadJson,
            IReadOnlyList<string> tags,
            string? sortableUniqueId)
        {
        }

        public void ApplyEvents(IReadOnlyList<PrimitiveProjectionEventEnvelope> events) => ApplySerializableEvents([]);

        public void ApplySerializableEvents(IReadOnlyList<SerializableEvent> events)
        {
            var current = Interlocked.Increment(ref _tracker.CurrentConcurrentBatchApplies);
            _tracker.MaxConcurrentBatchApplies = Math.Max(_tracker.MaxConcurrentBatchApplies, current);
            BatchEntered.Set();
            if (!AllowCompletion.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("Timed out waiting to release the batch apply.");
            }

            Interlocked.Decrement(ref _tracker.CurrentConcurrentBatchApplies);
        }

        public string ExecuteQuery(string queryType, string queryParamsJson) => "{}";

        public string ExecuteListQuery(string queryType, string queryParamsJson) => "[]";

        public string SerializeState() => "{}";

        public byte[] SerializeStateUtf8() => "{}"u8.ToArray();

        public void RestoreState(string stateJson)
        {
        }

        public void RestoreStateUtf8(byte[] stateJsonUtf8)
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class BatchApplyTracker
    {
        public int CurrentConcurrentBatchApplies;
        public int MaxConcurrentBatchApplies;
    }
}
