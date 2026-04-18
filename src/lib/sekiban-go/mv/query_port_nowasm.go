//go:build !wasm
// +build !wasm

package mv

// hostMvHostQueryRows stub for non-wasm builds. The ClientApi host imports this package to
// share DTOs and the ParamBuilder but never calls the host import — when running outside
// wasm there is no host to query against. Returning 0 means `invoke()` short-circuits to
// "no rows / no error".
func hostMvHostQueryRows(sqlPtr, sqlLen, paramsPtr, paramsLen uint32, rowLimit int32) int64 {
	_ = sqlPtr
	_ = sqlLen
	_ = paramsPtr
	_ = paramsLen
	_ = rowLimit
	return 0
}
