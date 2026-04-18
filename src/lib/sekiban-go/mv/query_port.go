package mv

import (
	"encoding/json"

	"github.com/J-Tech-Japan/sekiban-go/wasm"
)

// HostBackedQueryPort routes QueryRows / QuerySingleRow through the `env.mv_host_query_rows`
// host import provided by the wasmserver. The sample projector `ClassRoomEnrollmentMvV1`
// does not query mid-apply so this type exists primarily so future projectors can drop it
// in without re-declaring the import.
type HostBackedQueryPort struct{}

func NewHostBackedQueryPort() HostBackedQueryPort { return HostBackedQueryPort{} }

func (HostBackedQueryPort) QueryRows(sql string, params []MvParam) []MvQueryRowDto {
	result := invoke(sql, params, 0x7fffffff)
	if result == nil {
		return nil
	}
	return result.Rows
}

func (HostBackedQueryPort) QuerySingleRow(sql string, params []MvParam) *MvQueryRowDto {
	result := invoke(sql, params, 1)
	if result == nil || len(result.Rows) == 0 {
		return nil
	}
	first := result.Rows[0]
	return &first
}

func invoke(sql string, params []MvParam, rowLimit int32) *MvQueryResultDto {
	sqlBytes := []byte(sql)
	var paramsBytes []byte
	if len(params) > 0 {
		data, err := json.Marshal(params)
		if err != nil {
			return nil
		}
		paramsBytes = data
	}

	sqlPtr := bytesPointer(sqlBytes)
	paramsPtr := bytesPointer(paramsBytes)

	packed := hostMvHostQueryRows(sqlPtr, uint32(len(sqlBytes)), paramsPtr, uint32(len(paramsBytes)), rowLimit)
	ptr, length := wasm.UnpackPtrLen(packed)
	if ptr == 0 || length == 0 {
		return nil
	}
	text := wasm.ReadString(ptr, length)
	wasm.Dealloc(ptr, length)

	var errEnvelope struct {
		Error string `json:"error,omitempty"`
	}
	if err := json.Unmarshal([]byte(text), &errEnvelope); err == nil && errEnvelope.Error != "" {
		return nil
	}
	var result MvQueryResultDto
	if err := json.Unmarshal([]byte(text), &result); err != nil {
		return nil
	}
	return &result
}

func bytesPointer(b []byte) uint32 {
	if len(b) == 0 {
		return 0
	}
	return wasm.BytesPointer(b)
}
