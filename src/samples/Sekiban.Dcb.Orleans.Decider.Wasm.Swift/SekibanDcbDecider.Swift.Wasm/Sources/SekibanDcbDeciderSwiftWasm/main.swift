import SekibanMv
import SekibanWasm
import SekibanDcbDeciderSwiftEventSource

// The sample owns its MV + primitive-projection exports directly. Projector arrays are
// rebuilt per call so reactor-mode lazy-init quirks in swift-6.3.1_wasm never matter.

// -- Materialized view projectors (SQL-emitting, value types) ----------------------------
@inline(never)
private func projectors() -> [any WasmMvProjector] {
    [ClassRoomEnrollmentMvV1()]
}

// -- MultiProjection factories (in-memory, class types) ----------------------------------
//
// Map of projector name → factory used by `create_instance` when the host requests a new
// instance. The manifest's `projectors[].projectorName` field must match one of these keys.
@inline(never)
private func primitiveFactories() -> [String: SekibanWasm.PrimitiveHelpers.Factory] {
    [
        ClassRoomListProjection.projectorName: { ClassRoomListProjection() },
        RoomListProjection.projectorName: { RoomListProjection() },
        WeatherForecastListProjection.projectorName: { WeatherForecastListProjection() },
        ReservationListProjection.projectorName: { ReservationListProjection() },
    ]
}

// -- Primitive projection C-ABI entry points --------------------------------------------

@_cdecl("create_instance")
public func create_instance_entry(
    _ namePtr: Int32, _ nameLen: Int32
) -> Int32 {
    SekibanWasm.PrimitiveHelpers.createInstance(
        factories: primitiveFactories(),
        namePtr: namePtr, nameLen: nameLen)
}

@_cdecl("apply_event")
public func apply_event_entry(
    _ instanceId: Int32,
    _ eventTypePtr: Int32, _ eventTypeLen: Int32,
    _ payloadPtr: Int32, _ payloadLen: Int32
) {
    SekibanWasm.PrimitiveHelpers.applyEvent(
        instanceId: instanceId,
        eventTypePtr: eventTypePtr, eventTypeLen: eventTypeLen,
        payloadPtr: payloadPtr, payloadLen: payloadLen)
}

@_cdecl("apply_event_with_metadata")
public func apply_event_with_metadata_entry(
    _ instanceId: Int32,
    _ eventTypePtr: Int32, _ eventTypeLen: Int32,
    _ payloadPtr: Int32, _ payloadLen: Int32,
    _ metaPtr: Int32, _ metaLen: Int32
) {
    SekibanWasm.PrimitiveHelpers.applyEventWithMetadata(
        instanceId: instanceId,
        eventTypePtr: eventTypePtr, eventTypeLen: eventTypeLen,
        payloadPtr: payloadPtr, payloadLen: payloadLen,
        metaPtr: metaPtr, metaLen: metaLen)
}

@_cdecl("serialize_state")
public func serialize_state_entry(_ instanceId: Int32) -> Int64 {
    SekibanWasm.PrimitiveHelpers.serializeState(instanceId: instanceId)
}

@_cdecl("restore_state")
public func restore_state_entry(
    _ instanceId: Int32,
    _ ptr: Int32, _ len: Int32
) {
    SekibanWasm.PrimitiveHelpers.restoreState(instanceId: instanceId, ptr: ptr, len: len)
}

@_cdecl("execute_query")
public func execute_query_entry(
    _ instanceId: Int32,
    _ typePtr: Int32, _ typeLen: Int32,
    _ paramsPtr: Int32, _ paramsLen: Int32
) -> Int64 {
    SekibanWasm.PrimitiveHelpers.executeQuery(
        instanceId: instanceId,
        typePtr: typePtr, typeLen: typeLen,
        paramsPtr: paramsPtr, paramsLen: paramsLen)
}

@_cdecl("execute_list_query")
public func execute_list_query_entry(
    _ instanceId: Int32,
    _ typePtr: Int32, _ typeLen: Int32,
    _ paramsPtr: Int32, _ paramsLen: Int32
) -> Int64 {
    SekibanWasm.PrimitiveHelpers.executeListQuery(
        instanceId: instanceId,
        typePtr: typePtr, typeLen: typeLen,
        paramsPtr: paramsPtr, paramsLen: paramsLen)
}

@_cdecl("mv_metadata")
public func mv_metadata_entry() -> Int64 {
    SekibanMv.MvExportHelpers.metadata(projectors())
}

@_cdecl("mv_initialize")
public func mv_initialize_entry(
    _ viewNamePtr: Int32, _ viewNameLen: Int32,
    _ viewVersion: Int32,
    _ bindingsPtr: Int32, _ bindingsLen: Int32
) -> Int64 {
    SekibanMv.MvExportHelpers.initialize(
        projectors(),
        viewNamePtr: viewNamePtr, viewNameLen: viewNameLen,
        viewVersion: viewVersion,
        bindingsPtr: bindingsPtr, bindingsLen: bindingsLen)
}

@_cdecl("mv_apply_event")
public func mv_apply_event_entry(
    _ viewNamePtr: Int32, _ viewNameLen: Int32,
    _ viewVersion: Int32,
    _ bindingsPtr: Int32, _ bindingsLen: Int32,
    _ eventPtr: Int32, _ eventLen: Int32
) -> Int64 {
    SekibanMv.MvExportHelpers.applyEvent(
        projectors(),
        viewNamePtr: viewNamePtr, viewNameLen: viewNameLen,
        viewVersion: viewVersion,
        bindingsPtr: bindingsPtr, bindingsLen: bindingsLen,
        eventPtr: eventPtr, eventLen: eventLen)
}
