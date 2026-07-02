// @sekiban/as-wasm — AssemblyScript projector SDK for the Sekiban WASM Runtime.
//
// Projector modules import from "@sekiban/as-wasm/assembly" and re-export
// `alloc`/`dealloc` (plus their own projector entry points) as WASM exports.

export { alloc, dealloc, readStr, writeStr } from "./memory";

export {
  MV_PARAM_KIND_STRING,
  MV_PARAM_KIND_INT32,
  MV_PARAM_KIND_GUID,
  WasmMvParam,
  WasmMvSqlStatementDto,
  WasmMvTableBindingEntry,
  WasmMvTableBindingsDto,
  WasmMvMetadataDto,
  WasmMvSerializableEventDto,
  WasmMvStatementBatchDto,
  WasmMvErrorDto,
  errorPayload,
  jsonString,
  statementBatchPayload,
  tableName,
  sqlParam,
  guidParam,
  stringParam,
  int32Param,
  statement,
  buildIndexName,
} from "./mv";

export { applyPaging } from "./query";
