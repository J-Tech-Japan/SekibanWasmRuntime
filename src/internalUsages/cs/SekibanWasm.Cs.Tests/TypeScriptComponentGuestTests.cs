using System.Diagnostics;
using System.Text.Json;
using Sekiban.Dcb.Primitives;
using Sekiban.Dcb.WasmRuntime.Wasmtime;
using Xunit;

namespace SekibanWasm.Cs.Tests;

[Trait("Category", "PrivateUpstreamComponent")]
public sealed class TypeScriptComponentGuestTests
{
    [Fact]
    public void PinnedTypeScriptComponent_ShouldMatchPinnedDomainReference()
    {
        string repositoryRoot = FindRepositoryRoot();
        string componentPath = Path.Combine(
            repositoryRoot,
            "src",
            "wasm-projectors",
            "typescript",
            "build",
            "module.wasm");
        string referencePath = Path.Combine(
            repositoryRoot,
            "src",
            "wasm-projectors",
            "typescript",
            "build",
            "reference-results.json");
        string shimPath = FindPreview2Shim(repositoryRoot);

        Assert.True(File.Exists(componentPath), $"Component artifact not found: {componentPath}");
        Assert.True(File.Exists(referencePath), $"Reference artifact not found: {referencePath}");
        Assert.True(WasmBinaryFormatDetector.IsComponentFile(componentPath));
        Assert.True(WasmBinaryFormatDetector.IsSekibanProjectionComponentFile(componentPath));

        using JsonDocument reference = JsonDocument.Parse(File.ReadAllText(referencePath));
        string previousShimPath = Environment.GetEnvironmentVariable("WASMTIME_PREVIEW2_SHIM_PATH") ?? string.Empty;
        Environment.SetEnvironmentVariable("WASMTIME_PREVIEW2_SHIM_PATH", shimPath);
        try
        {
            using var runtime = new WasmtimeRuntime();
            using var host = new WasmtimePrimitiveProjectionHost(
                runtime,
                new WasmtimeModuleCache(runtime),
                new WasmtimeHostOptions
                {
                    DefaultModulePath = componentPath,
                    EnableInstancePooling = false,
                    MaxPooledInstancesPerProjector = 0,
                });

            using IPrimitiveProjectionInstance room = host.CreateInstance("RoomProjector");
            room.ApplyEvents([
                new PrimitiveProjectionEventEnvelope(
                    "RoomCreated",
                    "{\"roomId\":\"room-1\",\"name\":\"Boardroom\"}",
                    ["room:room-1"],
                    "0001"),
                new PrimitiveProjectionEventEnvelope(
                    "RoomReserved",
                    "{\"roomId\":\"room-1\",\"reservationId\":\"reservation-1\",\"userId\":\"user-1\"}",
                    ["room:room-1", "reservation:reservation-1"],
                    "0002"),
            ]);

            Assert.Equal(reference.RootElement.GetProperty("room").GetProperty("serializedState").GetString(), room.SerializeState());
            Assert.Equal(
                reference.RootElement.GetProperty("room").GetProperty("query").GetString(),
                room.ExecuteQuery("GetRoomStateQuery", "{\"roomId\":\"room-1\"}"));
            Assert.Equal(
                reference.RootElement.GetProperty("room").GetProperty("queryForOtherRoom").GetString(),
                room.ExecuteQuery("GetRoomStateQuery", "{\"roomId\":\"room-2\"}"));

            var component = Assert.IsType<WasmtimeComponentProjectionInstance>(room);
            string roomCreatedPayload = "{\"roomId\":\"room-1\",\"name\":\"Boardroom\"}";
            string serializedEvent = component.SerializeEvent("RoomCreated", roomCreatedPayload);
            Assert.Equal(
                JsonSerializer.Serialize(reference.RootElement.GetProperty("eventSerialization")),
                serializedEvent);
            Assert.Equal(serializedEvent, component.DeserializeEvent("RoomCreated", serializedEvent));
            Assert.Equal(
                reference.RootElement.GetProperty("eventTypes").EnumerateArray().Select(element => element.GetString()),
                component.GetEventTypes().OrderBy(value => value, StringComparer.Ordinal));

            using IPrimitiveProjectionInstance reservation = host.CreateInstance("ReservationProjector");
            reservation.ApplyEvent(
                "RoomReserved",
                "{\"roomId\":\"room-1\",\"reservationId\":\"reservation-1\",\"userId\":\"user-1\"}",
                ["room:room-1", "reservation:reservation-1"],
                "0002");
            reservation.ApplyEvent(
                "ReservationCancelled",
                "{\"reservationId\":\"reservation-1\",\"roomId\":\"room-1\"}",
                ["reservation:reservation-1"],
                "0003");

            Assert.Equal(
                reference.RootElement.GetProperty("reservation").GetProperty("serializedState").GetString(),
                reservation.SerializeState());
            Assert.Equal(
                reference.RootElement.GetProperty("reservation").GetProperty("listQuery").GetString(),
                reservation.ExecuteListQuery("GetReservationListQuery", "{\"roomId\":\"room-1\"}"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                "WASMTIME_PREVIEW2_SHIM_PATH",
                string.IsNullOrEmpty(previousShimPath) ? null : previousShimPath);
        }
    }

    [Fact]
    public void PinnedTypeScriptComponent_ShouldRecordPreview2JsonBridgeMeasurements()
    {
        string repositoryRoot = FindRepositoryRoot();
        string componentPath = Path.Combine(repositoryRoot, "src/wasm-projectors/typescript/build/module.wasm");
        string shimPath = FindPreview2Shim(repositoryRoot);
        string measurementsPath = Path.Combine(repositoryRoot, "src/wasm-projectors/typescript/build/preview2-measurements.json");
        var stopwatch = Stopwatch.StartNew();
        string previousShimPath = Environment.GetEnvironmentVariable("WASMTIME_PREVIEW2_SHIM_PATH") ?? string.Empty;
        Environment.SetEnvironmentVariable("WASMTIME_PREVIEW2_SHIM_PATH", shimPath);
        try
        {
            using var runtime = new WasmtimeRuntime();
            using var host = new WasmtimePrimitiveProjectionHost(
                runtime,
                new WasmtimeModuleCache(runtime),
                new WasmtimeHostOptions { DefaultModulePath = componentPath });
            using IPrimitiveProjectionInstance instance = host.CreateInstance("RoomProjector");
            stopwatch.Stop();
            double coldStartMs = stopwatch.Elapsed.TotalMilliseconds;

            const int callCount = 100;
            stopwatch.Restart();
            for (int index = 0; index < callCount; index++)
            {
                _ = instance.ExecuteQuery("GetRoomStateQuery", "{}");
            }
            stopwatch.Stop();
            double perCallMs = stopwatch.Elapsed.TotalMilliseconds / callCount;
            Directory.CreateDirectory(Path.GetDirectoryName(measurementsPath)!);
            File.WriteAllText(
                measurementsPath,
                JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    preview2Shim = new
                    {
                        coldStartMs,
                        perCallMs,
                        callCount,
                        bridge = "JSON-array P/Invoke; includes shim invocation and JSON encoding/decoding, and is not componentize-js cost",
                    },
                }, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);

            Assert.True(coldStartMs >= 0);
            Assert.True(perCallMs >= 0);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                "WASMTIME_PREVIEW2_SHIM_PATH",
                string.IsNullOrEmpty(previousShimPath) ? null : previousShimPath);
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "global.json")) &&
                Directory.Exists(Path.Combine(current.FullName, "src", "wasm-projectors", "typescript")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the SekibanWasmRuntime repository root.");
    }

    private static string FindPreview2Shim(string repositoryRoot)
    {
        string fileName = OperatingSystem.IsMacOS()
            ? "libwasmtime_preview2_shim.dylib"
            : OperatingSystem.IsWindows()
                ? "wasmtime_preview2_shim.dll"
                : "libwasmtime_preview2_shim.so";
        string path = Path.Combine(
            repositoryRoot,
            "external",
            "wasmtime-dotnet",
            "native",
            "wasmtime-preview2-shim",
            "target",
            "release",
            fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Build the pinned Preview2 shim before running component tests.", path);
        }

        return path;
    }
}
