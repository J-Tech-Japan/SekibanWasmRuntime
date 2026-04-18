package mv

import (
	"encoding/json"
	"fmt"

	"github.com/J-Tech-Japan/sekiban-go/wasm"
)

// HostBackedQueryPort routes QueryRows / QuerySingleRow through the `env.mv_host_query_rows`
// host import provided by the wasmserver. The sample projector `ClassRoomEnrollmentMvV1`
// does not query mid-apply so this type exists primarily so future projectors can drop it
// in without re-declaring the import.
type HostBackedQueryPort struct{}

func NewHostBackedQueryPort() HostBackedQueryPort { return HostBackedQueryPort{} }

// QueryRows panics if the host returns a `{"error":...}` envelope. That's the contract the
// C# host relies on so a failing mid-apply query aborts the MV apply and lets Orleans retry —
// silently dropping the error would produce stale projections. Projectors that prefer soft
// handling should call invokeSoft directly.
func (HostBackedQueryPort) QueryRows(sql string, params []MvParam) []MvQueryRowDto {
	result, err := invoke(sql, params, 0x7fffffff)
	if err != nil {
		panic(fmt.Errorf("mv_host_query_rows failed: %w", err))
	}
	if result == nil {
		return nil
	}
	return result.Rows
}

func (HostBackedQueryPort) QuerySingleRow(sql string, params []MvParam) *MvQueryRowDto {
	result, err := invoke(sql, params, 1)
	if err != nil {
		panic(fmt.Errorf("mv_host_query_rows failed: %w", err))
	}
	if result == nil || len(result.Rows) == 0 {
		return nil
	}
	first := result.Rows[0]
	return &first
}

func invoke(sql string, params []MvParam, rowLimit int32) (*MvQueryResultDto, error) {
	sqlBytes := []byte(sql)
	var paramsBytes []byte
	if len(params) > 0 {
		data, err := json.Marshal(params)
		if err != nil {
			return nil, fmt.Errorf("marshal params: %w", err)
		}
		paramsBytes = data
	}

	sqlPtr := bytesPointer(sqlBytes)
	paramsPtr := bytesPointer(paramsBytes)

	packed := hostMvHostQueryRows(sqlPtr, uint32(len(sqlBytes)), paramsPtr, uint32(len(paramsBytes)), rowLimit)
	ptr, length := wasm.UnpackPtrLen(packed)
	if ptr == 0 || length == 0 {
		return nil, nil
	}
	text := wasm.ReadString(ptr, length)
	// `wasm.Dealloc` is intentionally a no-op (TinyGo reactor mode is sensitive to manual
	// free). The allocation registry is cleared between host-driven MV export invocations by
	// `wasm.ResetAllocations()` in ExportHelpers, so repeated mid-apply queries do not
	// accumulate across calls.
	wasm.Dealloc(ptr, length)

	var errEnvelope struct {
		Error string `json:"error,omitempty"`
	}
	if err := json.Unmarshal([]byte(text), &errEnvelope); err == nil && errEnvelope.Error != "" {
		return nil, fmt.Errorf("%s", errEnvelope.Error)
	}
	var result MvQueryResultDto
	if err := json.Unmarshal([]byte(text), &result); err != nil {
		return nil, fmt.Errorf("parse mv_host_query_rows response: %w", err)
	}
	return &result, nil
}

func bytesPointer(b []byte) uint32 {
	if len(b) == 0 {
		return 0
	}
	return wasm.BytesPointer(b)
}
