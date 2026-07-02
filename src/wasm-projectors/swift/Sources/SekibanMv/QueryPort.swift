import Foundation
import SekibanWasm

// Host import from the wasmserver. Signature: (sql_ptr, sql_len, params_ptr, params_len,
// row_limit) -> packed i64 pointing at a UTF-8 JSON MvQueryResultDto (or `{"error":"..."}`).
//
// `@_silgen_name` alone would emit Swift's native calling convention (7 i32 params including
// the implicit error slot) which wasm-ld records as the import signature. wasmtime then refuses
// to link because the host declares `(i32 i32 i32 i32 i32) -> i64`. `@_extern(c, ...)` forces
// the C ABI so the signatures match.
#if arch(wasm32)
@_extern(wasm, module: "env", name: "mv_host_query_rows")
@_extern(c)
func host_mv_host_query_rows(
    _ sqlPtr: Int32, _ sqlLen: Int32,
    _ paramsPtr: Int32, _ paramsLen: Int32,
    _ rowLimit: Int32
) -> Int64
#else
// Native builds (unit tests, host-side tooling) have no wasm host to satisfy the
// import; report "no result" so the port degrades to empty rows instead of
// failing at link time.
func host_mv_host_query_rows(
    _ sqlPtr: Int32, _ sqlLen: Int32,
    _ paramsPtr: Int32, _ paramsLen: Int32,
    _ rowLimit: Int32
) -> Int64 { 0 }
#endif

/// Default query port that routes through the `env.mv_host_query_rows` host import.
/// The sample's `ClassRoomEnrollmentMvV1` doesn't query mid-apply so this exists primarily to
/// let future projectors call into the host without each one having to re-declare the `extern`.
public struct HostBackedMvQueryPort: MvQueryPort {
    public init() {}

    public func queryRows(_ sql: String, _ params: [MvParam]) -> [MvQueryRowDto] {
        invoke(sql: sql, params: params, rowLimit: Int32.max)?.rows ?? []
    }

    public func querySingleRow(_ sql: String, _ params: [MvParam]) -> MvQueryRowDto? {
        invoke(sql: sql, params: params, rowLimit: 1)?.rows.first
    }

    private func invoke(sql: String, params: [MvParam], rowLimit: Int32) -> MvQueryResultDto? {
        let sqlBytes = Array(sql.utf8)
        let paramsJson: [UInt8]
        if params.isEmpty {
            paramsJson = []
        } else if let data = try? JSONEncoder().encode(params) {
            paramsJson = Array(data)
        } else {
            return nil
        }

        // Temporarily hold the bytes in arrays so their pointers stay valid across the call.
        return sqlBytes.withUnsafeBufferPointer { sqlBuf in
            paramsJson.withUnsafeBufferPointer { paramsBuf in
                let sqlPtr: Int32
                let sqlLen: Int32
                if let base = sqlBuf.baseAddress, !sqlBytes.isEmpty {
                    sqlPtr = Int32(truncatingIfNeeded:
                        UInt32(UInt(bitPattern: UnsafeRawPointer(base))))
                    sqlLen = Int32(sqlBytes.count)
                } else {
                    sqlPtr = 0
                    sqlLen = 0
                }

                let paramsPtr: Int32
                let paramsLen: Int32
                if let base = paramsBuf.baseAddress, !paramsJson.isEmpty {
                    paramsPtr = Int32(truncatingIfNeeded:
                        UInt32(UInt(bitPattern: UnsafeRawPointer(base))))
                    paramsLen = Int32(paramsJson.count)
                } else {
                    paramsPtr = 0
                    paramsLen = 0
                }

                let packed = host_mv_host_query_rows(sqlPtr, sqlLen, paramsPtr, paramsLen, rowLimit)
                let (ptr, len) = unpackPtrLen(packed)
                if ptr == 0 || len == 0 { return nil }
                let text = readString(ptr: ptr, len: len)
                // Host allocated the buffer via our module's exported allocator (`alloc` →
                // malloc), so release it via the matching exported deallocator instead of
                // calling `free` directly. Keeps the allocator contract stable if the
                // module ever swaps malloc/free for a custom pair.
                sekibanDealloc(ptr, len)
                if let errData = text.data(using: .utf8),
                   let obj = try? JSONSerialization.jsonObject(with: errData) as? [String: Any],
                   obj["error"] != nil {
                    return nil
                }
                guard let data = text.data(using: .utf8) else { return nil }
                return try? JSONDecoder().decode(MvQueryResultDto.self, from: data)
            }
        }
    }
}
