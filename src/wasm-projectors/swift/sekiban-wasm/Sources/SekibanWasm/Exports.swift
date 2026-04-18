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

// `apply_events_batch` is not used by the MultiProjectionGrain catch-up path today but the
// host probes for it. A stub that returns 0 keeps `WasmtimePrimitiveProjectionInstance`
// from surfacing "missing export" warnings when the runtime starts up.
@_cdecl("apply_events_batch")
public func sekibanApplyEventsBatch(
    _ instanceId: Int32,
    _ jsonPtr: Int32, _ jsonLen: Int32
) -> Int32 {
    _ = instanceId
    _ = readString(ptr: jsonPtr, len: jsonLen)
    return 0
}
