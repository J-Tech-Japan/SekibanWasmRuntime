//go:build wasm
// +build wasm

package mv

// hostMvHostQueryRows is the WASM host import. Signature is pinned to the C# host's
// Wasmtime linker declaration: five i32 parameters + i64 packed (ptr, len) return. TinyGo
// recognizes the `go:wasmimport` directive on wasm32-wasip1 builds.
//
//go:wasmimport env mv_host_query_rows
func hostMvHostQueryRows(sqlPtr, sqlLen, paramsPtr, paramsLen uint32, rowLimit int32) int64
