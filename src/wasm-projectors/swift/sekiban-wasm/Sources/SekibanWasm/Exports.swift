import Foundation

// Primitive ABI exports. These mirror the Rust sample's `export_domain!` set so the Swift
// WASM module satisfies the host's export surface in
// `WasmtimePrimitiveProjectionInstance`. The Swift sample currently ships zero primitive
// projectors in its manifest (MV only), so these stubs exist purely to keep the host happy
// and are never exercised at runtime. If a future Swift sample introduces primitive
// projectors, replace these with a registry-driven dispatch similar to the MV exports.

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

@_cdecl("create_instance")
public func sekibanCreateInstance(_ namePtr: Int32, _ nameLen: Int32) -> Int32 {
    _ = readString(ptr: namePtr, len: nameLen)
    return 0
}

@_cdecl("apply_event")
public func sekibanApplyEvent(
    _ instanceId: Int32,
    _ typePtr: Int32, _ typeLen: Int32,
    _ payloadPtr: Int32, _ payloadLen: Int32
) {
    _ = instanceId
    _ = readString(ptr: typePtr, len: typeLen)
    _ = readString(ptr: payloadPtr, len: payloadLen)
}

@_cdecl("apply_event_with_metadata")
public func sekibanApplyEventWithMetadata(
    _ instanceId: Int32,
    _ typePtr: Int32, _ typeLen: Int32,
    _ payloadPtr: Int32, _ payloadLen: Int32,
    _ metaPtr: Int32, _ metaLen: Int32
) {
    _ = instanceId
    _ = readString(ptr: typePtr, len: typeLen)
    _ = readString(ptr: payloadPtr, len: payloadLen)
    _ = readString(ptr: metaPtr, len: metaLen)
}

@_cdecl("apply_events_batch")
public func sekibanApplyEventsBatch(
    _ instanceId: Int32,
    _ jsonPtr: Int32, _ jsonLen: Int32
) -> Int32 {
    _ = instanceId
    _ = readString(ptr: jsonPtr, len: jsonLen)
    return 0
}

@_cdecl("serialize_state")
public func sekibanSerializeState(_ instanceId: Int32) -> Int64 {
    _ = instanceId
    return writeStringToMemory("{}")
}

@_cdecl("restore_state")
public func sekibanRestoreState(
    _ instanceId: Int32,
    _ ptr: Int32, _ len: Int32
) {
    _ = instanceId
    _ = readString(ptr: ptr, len: len)
}

@_cdecl("execute_query")
public func sekibanExecuteQuery(
    _ instanceId: Int32,
    _ typePtr: Int32, _ typeLen: Int32,
    _ paramsPtr: Int32, _ paramsLen: Int32
) -> Int64 {
    _ = instanceId
    _ = readString(ptr: typePtr, len: typeLen)
    _ = readString(ptr: paramsPtr, len: paramsLen)
    return writeStringToMemory("{}")
}

@_cdecl("execute_list_query")
public func sekibanExecuteListQuery(
    _ instanceId: Int32,
    _ typePtr: Int32, _ typeLen: Int32,
    _ paramsPtr: Int32, _ paramsLen: Int32
) -> Int64 {
    _ = instanceId
    _ = readString(ptr: typePtr, len: typeLen)
    _ = readString(ptr: paramsPtr, len: paramsLen)
    return writeStringToMemory("[]")
}
