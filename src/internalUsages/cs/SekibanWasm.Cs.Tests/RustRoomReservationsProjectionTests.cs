using System.Text;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Primitives;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.WasmRuntime;
using Sekiban.Dcb.WasmRuntime.Wasmtime;
using SekibanWasm.Cs.Domain;
using Xunit;

namespace SekibanWasm.Cs.Tests;

public class RustRoomReservationsProjectionTests
{
    [Fact]
    public void RoomReservationsProjector_ShouldReplayMultipleQuickReservations()
    {
        string modulePath = GetRustModulePath();
        Assert.True(File.Exists(modulePath), $"Rust WASM module not found: {modulePath}");

        var runtime = new WasmtimeRuntime();
        var moduleCache = new WasmtimeModuleCache(runtime);
        var host = new WasmtimePrimitiveProjectionHost(
            runtime,
            moduleCache,
            new WasmtimeHostOptions
            {
                DefaultModulePath = modulePath
            });

        SerializableTagState? cachedState = null;
        Guid roomId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        for (int index = 0; index < 5; index++)
        {
            using IPrimitiveProjectionInstance instance = host.CreateInstance("RoomReservationsProjector");
            var primitive = new WasmTagStateProjectionPrimitive(
                instance,
                "RoomReservationsProjector",
                "1.0.0",
                DomainType.GetDomainTypes().EventTypes,
                DomainJsonContext.Default.Options);

            Assert.True(primitive.ApplyState(cachedState));

            Guid reservationId = Guid.Parse($"00000000-0000-0000-0000-{index + 1:000000000000}");
            string startTime = DateTimeOffset.Parse("2026-04-10T01:00:00Z")
                .AddMinutes(index * 90)
                .ToString("O");
            string endTime = DateTimeOffset.Parse(startTime).AddHours(1).ToString("O");
            string tagReservation = $"Reservation:{reservationId}";
            string tagRoomReservation = $"RoomReservation:{roomId}";

            var events = new List<SerializableEvent>
            {
                CreateSerializableEvent(
                    "ReservationDraftCreated",
                    $$"""
                    {"reservationId":"{{reservationId}}","roomId":"{{roomId}}","organizerId":"22222222-2222-2222-2222-222222222222","organizerName":"Bench User","startTime":"{{startTime}}","endTime":"{{endTime}}","purpose":"Bench","selectedEquipment":[]}
                    """,
                    tagReservation,
                    tagRoomReservation,
                    $"100{index}1"),
                CreateSerializableEvent(
                    "ReservationHoldCommitted",
                    $$"""
                    {"reservationId":"{{reservationId}}","roomId":"{{roomId}}","organizerId":"22222222-2222-2222-2222-222222222222","organizerName":"Bench User","startTime":"{{startTime}}","endTime":"{{endTime}}","purpose":"Bench","selectedEquipment":[],"requiresApproval":false,"approvalRequestId":null,"approvalRequestComment":null}
                    """,
                    tagReservation,
                    tagRoomReservation,
                    $"100{index}2"),
                CreateSerializableEvent(
                    "ReservationConfirmed",
                    $$"""
                    {"reservationId":"{{reservationId}}","roomId":"{{roomId}}","organizerId":"22222222-2222-2222-2222-222222222222","organizerName":"Bench User","startTime":"{{startTime}}","endTime":"{{endTime}}","purpose":"Bench","selectedEquipment":[],"confirmedAt":"2026-04-09T00:00:00Z","approvalRequestId":null,"approvalRequestComment":null,"approvalDecisionComment":null}
                    """,
                    tagReservation,
                    tagRoomReservation,
                    $"100{index}3")
            };

            Assert.True(primitive.ApplyEvents(events, events[^1].SortableUniqueIdValue));
            cachedState = primitive.GetSerializedState();
            Assert.NotEmpty(cachedState.Payload);
        }
    }

    private static SerializableEvent CreateSerializableEvent(
        string eventType,
        string payloadJson,
        string firstTag,
        string secondTag,
        string sortableUniqueId)
    {
        var id = Guid.NewGuid();
        var metadata = new EventMetadata(id.ToString(), eventType, "test");
        return new SerializableEvent(
            Encoding.UTF8.GetBytes(payloadJson),
            sortableUniqueId,
            id,
            metadata,
            [firstTag, secondTag],
            eventType);
    }

    private static string GetRustModulePath()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(
                current.FullName,
                "src",
                "samples",
                "Sekiban.Dcb.Orleans.Decider.Wasm.Rs",
                "modules",
                "sekiban-dcb-decider-rust.wasm");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Rust WASM module from test base directory.");
    }
}
