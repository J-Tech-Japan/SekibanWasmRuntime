// Materialized-view SQL statement protocol shared by Sekiban AssemblyScript
// projector modules. The host calls the module's `mv_initialize` /
// `mv_apply_event` exports and expects JSON payloads shaped like the DTOs
// below; projector modules build them with the statement/param helpers.

import { JSON } from "json-as/assembly";

export const MV_PARAM_KIND_STRING: i32 = 1;
export const MV_PARAM_KIND_INT32: i32 = 2;
export const MV_PARAM_KIND_GUID: i32 = 5;

@json
export class WasmMvParam {
  name: string = "";
  kind: i32 = 0;
  valueJson: string | null = null;
}

@json
export class WasmMvSqlStatementDto {
  sql: string = "";
  parameters: WasmMvParam[] = [];
}

@json
export class WasmMvTableBindingEntry {
  logical: string = "";
  physical: string = "";
}

@json
export class WasmMvTableBindingsDto {
  bindings: WasmMvTableBindingEntry[] = [];
}

@json
export class WasmMvMetadataDto {
  viewName: string = "";
  viewVersion: i32 = 0;
  logicalTables: string[] = [];
}

@json
export class WasmMvSerializableEventDto {
  eventType: string = "";
  payloadJson: string = "";
  sortableUniqueId: string = "";
  tags: string[] = [];
}

@json
export class WasmMvStatementBatchDto {
  statements: WasmMvSqlStatementDto[] = [];
}

@json
export class WasmMvErrorDto {
  error: string = "";
}

export function errorPayload(message: string): string {
  const error = new WasmMvErrorDto();
  error.error = message;
  return JSON.stringify<WasmMvErrorDto>(error);
}

export function jsonString(value: string): string {
  return JSON.stringify<string>(value);
}

export function statementBatchPayload(statements: WasmMvSqlStatementDto[]): string {
  const batch = new WasmMvStatementBatchDto();
  batch.statements = statements;
  return JSON.stringify<WasmMvStatementBatchDto>(batch);
}

export function tableName(bindings: WasmMvTableBindingsDto, logical: string): string {
  for (let i = 0; i < bindings.bindings.length; i++) {
    if (bindings.bindings[i].logical == logical) {
      return bindings.bindings[i].physical;
    }
  }
  return "";
}

export function sqlParam(name: string, kind: i32, valueJson: string): WasmMvParam {
  const param = new WasmMvParam();
  param.name = name;
  param.kind = kind;
  param.valueJson = valueJson;
  return param;
}

export function guidParam(name: string, value: string): WasmMvParam {
  return sqlParam(name, MV_PARAM_KIND_GUID, jsonString(value));
}

export function stringParam(name: string, value: string): WasmMvParam {
  return sqlParam(name, MV_PARAM_KIND_STRING, jsonString(value));
}

export function int32Param(name: string, value: i32): WasmMvParam {
  return sqlParam(name, MV_PARAM_KIND_INT32, value.toString());
}

export function statement(sql: string, parameters: WasmMvParam[] = []): WasmMvSqlStatementDto {
  const dto = new WasmMvSqlStatementDto();
  dto.sql = sql;
  dto.parameters = parameters;
  return dto;
}

// Builds a Postgres index name that stays within the 63-character identifier
// limit regardless of the bound physical table name.
export function buildIndexName(physicalTable: string, suffix: string): string {
  const maxIdentifierLength = 63;
  const prefix = "idx_";
  const tail = "_" + suffix;
  const available = maxIdentifierLength - prefix.length - tail.length;
  if (physicalTable.length <= available) {
    return prefix + physicalTable + tail;
  }
  return prefix + physicalTable.substring(0, available) + tail;
}
