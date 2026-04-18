// Package mv holds the Go companion types for the Sekiban WASM materialized-view wire
// contract. The DTOs here are byte-for-byte compatible with the C# host-side DTOs in
// `Sekiban.Dcb.WasmRuntime.Host.MaterializedView.WasmMvBoundaryContracts` and the Rust
// `sekiban_mv::dto::*` types: camelCase JSON keys, `Kind` encoded as an integer 0..9.
package mv

// MvParamKind values must stay aligned with the C# enum on the host side. A projector uses
// these constants through MvParamBuilder rather than passing them directly.
type MvParamKind int

const (
	ParamKindNull           MvParamKind = 0
	ParamKindString         MvParamKind = 1
	ParamKindInt32          MvParamKind = 2
	ParamKindInt64          MvParamKind = 3
	ParamKindBoolean        MvParamKind = 4
	ParamKindGuid           MvParamKind = 5
	ParamKindDateTimeOffset MvParamKind = 6
	ParamKindDecimal        MvParamKind = 7
	ParamKindDouble         MvParamKind = 8
	ParamKindBytes          MvParamKind = 9
)

// MvParam is a named SQL parameter. ValueJSON is a raw JSON token whose representation is
// dictated by Kind — a JSON string for String/Guid/DateTimeOffset/Bytes (base64), a JSON
// number for Int32/Int64/Decimal/Double, a JSON bool for Boolean, and absent/nil for Null.
type MvParam struct {
	Name      string      `json:"name"`
	Kind      MvParamKind `json:"kind"`
	ValueJSON *string     `json:"valueJson,omitempty"`
}

// MvSqlStatementDto is one SQL statement a projector emits for the host to run inside the
// apply transaction.
type MvSqlStatementDto struct {
	Sql        string    `json:"sql"`
	Parameters []MvParam `json:"parameters"`
}

// MvStatementBatchDto is the response envelope returned by mv_initialize / mv_apply_event.
type MvStatementBatchDto struct {
	Statements []MvSqlStatementDto `json:"statements"`
}

// MvTableBindingEntry maps a logical table name to the registry-resolved physical name.
type MvTableBindingEntry struct {
	Logical  string `json:"logical"`
	Physical string `json:"physical"`
}

// MvTableBindingsDto groups the logical→physical map that the host writes into every
// `mv_*` call.
type MvTableBindingsDto struct {
	Bindings []MvTableBindingEntry `json:"bindings"`
}

// PhysicalName returns the physical table name registered for a logical table, or a sentinel
// string if the binding is missing. Matches the Rust impl's failure mode so projection SQL
// fails with a recognizable identifier rather than silently substituting an empty string.
func (b MvTableBindingsDto) PhysicalName(logical string) string {
	for _, entry := range b.Bindings {
		if entry.Logical == logical {
			return entry.Physical
		}
	}
	return "__missing_binding_" + logical + "__"
}

// MvSerializableEventDto is the host→wasm event envelope passed to mv_apply_event.
type MvSerializableEventDto struct {
	EventType        string   `json:"eventType"`
	PayloadJSON      string   `json:"payloadJson"`
	SortableUniqueId string   `json:"sortableUniqueId"`
	Tags             []string `json:"tags"`
}

// WasmMvMetadata is one entry returned by mv_metadata so the host can enumerate the
// projectors a module ships with.
type WasmMvMetadata struct {
	ViewName      string   `json:"viewName"`
	ViewVersion   int32    `json:"viewVersion"`
	LogicalTables []string `json:"logicalTables"`
}

// MvQueryRowDto is a single row returned by the mv_host_query_rows host import. Columns are
// serialized as JSON tokens stringified into strings, so projectors can cast each column as
// needed.
type MvQueryRowDto struct {
	Columns map[string]*string `json:"columns"`
}

// MvQueryResultDto is the response body of mv_host_query_rows.
type MvQueryResultDto struct {
	Rows []MvQueryRowDto `json:"rows"`
}
