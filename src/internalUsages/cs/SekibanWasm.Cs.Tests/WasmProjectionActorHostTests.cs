using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Orleans.Serialization;
using Sekiban.Dcb.Primitives;
using Sekiban.Dcb.WasmRuntime;
using Xunit;

namespace SekibanWasm.Cs.Tests;

public class WasmProjectionActorHostTests
{
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

        Assert.True(restoreResult.IsSuccess);

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

    private static WasmProjectionActorHost CreateHost(StubPrimitiveProjectionInstance instance)
    {
        var registry = new WasmProjectorRegistry();
        registry.Register(new WasmModuleRef(
            ProjectorName: "WeatherForecastMultiProjection",
            ModulePath: "/tmp/weather.wasm",
            AbiKind: "wasi-preview1",
            ModuleVersion: "1.0.0",
            ProjectorVersion: "1.0.0"));

        return new WasmProjectionActorHost(
            new StubPrimitiveProjectionHost(instance),
            registry,
            new JsonSerializerOptions(JsonSerializerDefaults.Web),
            "WeatherForecastMultiProjection",
            NullLogger.Instance);
    }

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

    private sealed class StubPrimitiveProjectionHost(StubPrimitiveProjectionInstance instance) : IPrimitiveProjectionHost
    {
        private readonly StubPrimitiveProjectionInstance _instance = instance;

        public IPrimitiveProjectionInstance CreateInstance(string projectorName) => _instance;
    }

    private sealed class StubPrimitiveProjectionInstance : IPrimitiveProjectionInstance
    {
        public string StateJson { get; private set; } = "{}";
        public string QueryResponseJson { get; set; } = string.Empty;
        public string ListQueryResponseJson { get; set; } = "[]";

        public void ApplyEvent(
            string eventType,
            string eventPayloadJson,
            IReadOnlyList<string> tags,
            string? sortableUniqueId)
        {
            StateJson = eventPayloadJson;
        }

        public string ExecuteQuery(string queryType, string queryParamsJson) =>
            string.IsNullOrEmpty(QueryResponseJson) ? StateJson : QueryResponseJson;

        public string ExecuteListQuery(string queryType, string queryParamsJson) => ListQueryResponseJson;

        public string SerializeState() => StateJson;

        public void RestoreState(string stateJson)
        {
            StateJson = stateJson;
        }

        public void Dispose()
        {
        }
    }
}
