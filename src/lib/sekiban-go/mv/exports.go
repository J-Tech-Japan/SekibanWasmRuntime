package mv

import (
	"encoding/json"
	"fmt"

	"github.com/J-Tech-Japan/SekibanWasmRuntime/src/lib/sekiban-go/wasm"
)

// ExportHelpers bundles the projector-list-driven implementations of the three MV C-ABI
// exports. The sample wasm module wraps these in `//export mv_metadata / mv_initialize /
// mv_apply_event` wrappers because TinyGo's `//export` directive must be declared in the
// main package.

// Metadata returns JSON metadata describing `projectors`, packed into linear memory via
// sekiban-go's WriteString. The return value is the packed (ptr,len) i64 the host consumes.
func Metadata(projectors []Projector) int64 {
	// Clear buffers tracked from the previous MV export call. The MV executor on the host
	// side reads the previous return buffer before calling us again, so resetting at the
	// start of each export is safe and prevents the fixed-size allocation registry from
	// filling up over many MV apply calls.
	wasm.ResetAllocations()
	meta := make([]WasmMvMetadata, 0, len(projectors))
	for _, p := range projectors {
		meta = append(meta, WasmMvMetadata{
			ViewName:      p.ViewName(),
			ViewVersion:   p.ViewVersion(),
			LogicalTables: p.LogicalTables(),
		})
	}
	data, err := json.Marshal(meta)
	if err != nil {
		return wasm.WriteString(errorEnvelope(err))
	}
	return wasm.WriteString(string(data))
}

// Initialize looks up a projector by (viewName, viewVersion) and runs Initialize against the
// decoded bindings. Missing projectors / decode errors are returned as error envelopes so
// the host surfaces them as a meaningful log line instead of a generic WASM trap.
func Initialize(
	projectors []Projector,
	viewName string,
	viewVersion int32,
	bindingsJSON string,
) int64 {
	wasm.ResetAllocations()
	projector := lookup(projectors, viewName, viewVersion)
	if projector == nil {
		return wasm.WriteString(errorEnvelope(fmt.Errorf("unknown view %s/%d", viewName, viewVersion)))
	}
	var bindings MvTableBindingsDto
	if err := json.Unmarshal([]byte(bindingsJSON), &bindings); err != nil {
		return wasm.WriteString(errorEnvelope(fmt.Errorf("parse bindings: %w", err)))
	}
	statements := projector.Initialize(bindings)
	if statements == nil {
		statements = []MvSqlStatementDto{}
	}
	// The host deserializes parameters with a non-null contract; normalize nil
	// slices (e.g. parameterless statements or empty ParamBuilder results) so a
	// projector cannot emit "parameters": null and crash statement conversion.
	for i := range statements {
		if statements[i].Parameters == nil {
			statements[i].Parameters = []MvParam{}
		}
	}
	batch := MvStatementBatchDto{Statements: statements}
	data, err := json.Marshal(batch)
	if err != nil {
		return wasm.WriteString(errorEnvelope(err))
	}
	return wasm.WriteString(string(data))
}

// ApplyEvent looks up the projector, decodes bindings+event, and runs ApplyEvent. A
// HostBackedQueryPort is supplied automatically so projectors that need mid-apply reads do
// not need to know about the transport.
func ApplyEvent(
	projectors []Projector,
	viewName string,
	viewVersion int32,
	bindingsJSON string,
	eventJSON string,
) int64 {
	wasm.ResetAllocations()
	projector := lookup(projectors, viewName, viewVersion)
	if projector == nil {
		return wasm.WriteString(errorEnvelope(fmt.Errorf("unknown view %s/%d", viewName, viewVersion)))
	}
	var bindings MvTableBindingsDto
	if err := json.Unmarshal([]byte(bindingsJSON), &bindings); err != nil {
		return wasm.WriteString(errorEnvelope(fmt.Errorf("parse bindings: %w", err)))
	}
	var event MvSerializableEventDto
	if err := json.Unmarshal([]byte(eventJSON), &event); err != nil {
		return wasm.WriteString(errorEnvelope(fmt.Errorf("parse event: %w", err)))
	}
	statements := projector.ApplyEvent(bindings, event, NewHostBackedQueryPort())
	if statements == nil {
		statements = []MvSqlStatementDto{}
	}
	// Ensure every statement has a non-nil Parameters slice so the host-side lambda
	// `.Parameters.Select(...)` never sees a null. The common DDL path uses stmt(sql) which
	// already sets Parameters to []MvParam{}, but a future projector might leave it nil.
	for i := range statements {
		if statements[i].Parameters == nil {
			statements[i].Parameters = []MvParam{}
		}
	}
	batch := MvStatementBatchDto{Statements: statements}
	data, err := json.Marshal(batch)
	if err != nil {
		return wasm.WriteString(errorEnvelope(err))
	}
	return wasm.WriteString(string(data))
}

func lookup(projectors []Projector, viewName string, viewVersion int32) Projector {
	for _, p := range projectors {
		if p.ViewName() == viewName && p.ViewVersion() == viewVersion {
			return p
		}
	}
	return nil
}

// errorEnvelope renders a `{"error":"..."}` JSON payload the host detects and logs.
func errorEnvelope(err error) string {
	payload, merr := json.Marshal(map[string]string{"error": err.Error()})
	if merr != nil {
		return `{"error":"encode failure"}`
	}
	return string(payload)
}
