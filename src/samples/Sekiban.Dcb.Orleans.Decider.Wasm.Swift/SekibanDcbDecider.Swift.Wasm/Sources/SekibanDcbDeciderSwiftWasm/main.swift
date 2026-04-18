import SekibanMv
import SekibanWasm
import SekibanDcbDeciderSwiftEventSource

// The sample owns its MV exports directly. We build the projector array fresh at each call to
// avoid any lazy-init surprises under Swift's reactor-mode `_initialize` (observed to skip
// user top-level code in swift-6.3.1_wasm). Projector types are pure value types with no
// per-instance state, so constructing once per call is cheap and thread-safe.
@inline(never)
private func projectors() -> [any WasmMvProjector] {
    [ClassRoomEnrollmentMvV1()]
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
