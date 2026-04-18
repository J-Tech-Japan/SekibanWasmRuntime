import Foundation

/// Pack a (ptr, len) pair into a single i64 return value matching the Rust
/// `sekiban_wasm::memory::pack_ptr_len` convention: upper 32 bits are the pointer,
/// lower 32 bits are the length.
@inlinable public func packPtrLen(_ ptr: Int32, _ len: Int32) -> Int64 {
    let p = Int64(UInt32(bitPattern: ptr)) << 32
    let l = Int64(UInt32(bitPattern: len))
    return p | l
}

/// Inverse of `packPtrLen`.
@inlinable public func unpackPtrLen(_ packed: Int64) -> (Int32, Int32) {
    let ptr = Int32(truncatingIfNeeded: packed >> 32)
    let len = Int32(truncatingIfNeeded: packed & 0xFFFF_FFFF)
    return (ptr, len)
}

/// Read a UTF-8 string from linear memory. Host writes valid UTF-8; if it ever doesn't, we
/// return the empty string rather than trapping so errors surface as JSON decode failures.
public func readString(ptr: Int32, len: Int32) -> String {
    if ptr == 0 || len <= 0 { return "" }
    let base = UnsafeRawPointer(bitPattern: Int(UInt32(bitPattern: ptr)))
    guard let base else { return "" }
    let buf = UnsafeBufferPointer(
        start: base.assumingMemoryBound(to: UInt8.self),
        count: Int(len))
    return String(decoding: buf, as: UTF8.self)
}

/// Write a UTF-8 string into linear memory via `malloc` and return the packed (ptr, len).
/// The host calls our `dealloc` export after consuming the buffer.
public func writeStringToMemory(_ s: String) -> Int64 {
    let bytes = Array(s.utf8)
    return writeBytes(bytes)
}

/// Encode a serializable value to JSON and write it to linear memory.
public func writeJSON<T: Encodable>(_ value: T) -> Int64 {
    let encoder = JSONEncoder()
    encoder.outputFormatting = []
    do {
        let data = try encoder.encode(value)
        return writeData(data)
    } catch {
        return writeErrorEnvelope(error.localizedDescription)
    }
}

/// Emit a `{"error":"..."}` envelope. Host-side deserializers on both C# and Rust sides look
/// for this shape to surface projector failures as meaningful log lines.
public func writeErrorEnvelope(_ message: String) -> Int64 {
    let encoder = JSONEncoder()
    let payload: [String: String] = ["error": message]
    if let data = try? encoder.encode(payload) {
        return writeData(data)
    }
    return 0
}

/// Copy `bytes` into a freshly malloc'd buffer and return the packed (ptr, len). Returns 0
/// when the input is empty or allocation fails.
public func writeBytes(_ bytes: [UInt8]) -> Int64 {
    if bytes.isEmpty { return 0 }
    let len = bytes.count
    guard let raw = malloc(len) else { return 0 }
    bytes.withUnsafeBufferPointer { src in
        raw.copyMemory(from: src.baseAddress!, byteCount: len)
    }
    let ptrU = UInt32(UInt(bitPattern: raw))
    return (Int64(ptrU) << 32) | Int64(UInt32(len))
}

/// Copy `data` into a freshly malloc'd buffer and return the packed (ptr, len).
public func writeData(_ data: Data) -> Int64 {
    if data.isEmpty { return 0 }
    return data.withUnsafeBytes { rawBuf -> Int64 in
        guard let src = rawBuf.baseAddress, let dst = malloc(data.count) else { return 0 }
        dst.copyMemory(from: src, byteCount: data.count)
        let ptrU = UInt32(UInt(bitPattern: dst))
        return (Int64(ptrU) << 32) | Int64(UInt32(data.count))
    }
}
