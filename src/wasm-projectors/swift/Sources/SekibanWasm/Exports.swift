import Foundation

// Base C-ABI exports every Swift WASM module shipping through `Sekiban.Dcb.WasmRuntime.Host`
// needs. The primitive-projection / multi-projection entry points
// (`create_instance`/`apply_event`/`serialize_state`/`restore_state`/
// `execute_query`/`execute_list_query`) live in the consuming module because the exports
// must know which projector factories to use. See `PrimitiveHelpers.swift` for the thin
// dispatch helpers the sample wraps in its own `@_cdecl` functions.

@_cdecl("alloc")
public func sekibanAlloc(_ size: Int32) -> Int32 {
    if size <= 0 { return 0 }
    guard let p = malloc(Int(size)) else { return 0 }
    return Int32(truncatingIfNeeded: UInt32(UInt(bitPattern: p)))
}

@_cdecl("dealloc")
public func sekibanDealloc(_ ptr: Int32, _ size: Int32) {
    _ = size
    if ptr == 0 { return }
    let raw = UnsafeMutableRawPointer(bitPattern: Int(UInt32(bitPattern: ptr)))
    free(raw)
}

// `apply_events_batch` is the hot path for MultiProjection catch-up: when a host batch
// contains more than one event, `WasmtimePrimitiveProjectionInstance.ApplyBatchChunkCore`
// calls it and only falls back to per-event apply (through an expensive exception) if this
// export is missing or throws. The dispatcher dispatches against an existing instance id
// so it doesn't need the sample's projector-factory map — instances come from
// `create_instance`, which the sample owns.
@_cdecl("apply_events_batch")
public func sekibanApplyEventsBatch(
    _ instanceId: Int32,
    _ jsonPtr: Int32, _ jsonLen: Int32
) -> Int32 {
    PrimitiveHelpers.applyEventsBatch(instanceId: instanceId, jsonPtr: jsonPtr, jsonLen: jsonLen)
}
