import { JSON } from "json-as/assembly";
import {
  readStr,
  writeStr,
  WasmMvSqlStatementDto,
  WasmMvTableBindingsDto,
  WasmMvMetadataDto,
  WasmMvSerializableEventDto,
  errorPayload,
  statementBatchPayload,
  statement,
  tableName,
  guidParam,
  stringParam,
  int32Param,
} from "@sekiban/as-wasm/assembly";

// Mirrors the crates.io Rust decider sample's WeatherForecastMvV1
// (src/samples/Sekiban.Dcb.WasmRuntime.CratesIo.RsDecider/Domain/src/lib.rs):
// same view name, logical table, columns, and optimistic
// _last_sortable_unique_id guard.

const VIEW_NAME = "WeatherForecast";
const VIEW_VERSION: i32 = 1;
const WEATHER_FORECAST_LOGICAL = "weather_forecast";

@json
class WeatherForecastCreatedEv {
  forecastId: string = "";
  location: string = "";
  temperatureC: i32 = 0;
  summary: string = "";
  createdAt: string = "";
}

@json
class WeatherForecastLocationUpdatedEv {
  forecastId: string = "";
  newLocation: string = "";
  updatedAt: string = "";
}

function initializeStatements(bindings: WasmMvTableBindingsDto): WasmMvSqlStatementDto[] {
  const table = tableName(bindings, WEATHER_FORECAST_LOGICAL);
  return [
    statement(
      "CREATE TABLE IF NOT EXISTS " + table +
      " (forecast_id UUID PRIMARY KEY, location TEXT NOT NULL, temperature_c INT NOT NULL, summary TEXT NOT NULL, created_at TEXT NOT NULL, updated_at TEXT NULL, _last_sortable_unique_id TEXT NOT NULL, _last_applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW());",
    ),
  ];
}

function insertForecast(bindings: WasmMvTableBindingsDto, ev: WeatherForecastCreatedEv, sortableUniqueId: string): WasmMvSqlStatementDto {
  const table = tableName(bindings, WEATHER_FORECAST_LOGICAL);
  return statement(
    "INSERT INTO " + table +
    " (forecast_id, location, temperature_c, summary, created_at, updated_at, _last_sortable_unique_id, _last_applied_at)" +
    " VALUES (@ForecastId, @Location, @TemperatureC, @Summary, @CreatedAt, NULL, @SortableUniqueId, NOW())" +
    " ON CONFLICT (forecast_id) DO UPDATE SET" +
    " location = EXCLUDED.location, temperature_c = EXCLUDED.temperature_c, summary = EXCLUDED.summary," +
    " created_at = EXCLUDED.created_at, updated_at = NULL," +
    " _last_sortable_unique_id = EXCLUDED._last_sortable_unique_id, _last_applied_at = EXCLUDED._last_applied_at" +
    " WHERE " + table + "._last_sortable_unique_id < EXCLUDED._last_sortable_unique_id;",
    [
      guidParam("ForecastId", ev.forecastId),
      stringParam("Location", ev.location),
      int32Param("TemperatureC", ev.temperatureC),
      stringParam("Summary", ev.summary),
      stringParam("CreatedAt", ev.createdAt),
      stringParam("SortableUniqueId", sortableUniqueId),
    ],
  );
}

function updateLocation(bindings: WasmMvTableBindingsDto, ev: WeatherForecastLocationUpdatedEv, sortableUniqueId: string): WasmMvSqlStatementDto {
  const table = tableName(bindings, WEATHER_FORECAST_LOGICAL);
  return statement(
    "UPDATE " + table +
    " SET location = @Location, updated_at = @UpdatedAt, _last_sortable_unique_id = @SortableUniqueId, _last_applied_at = NOW()" +
    " WHERE forecast_id = @ForecastId AND _last_sortable_unique_id < @SortableUniqueId;",
    [
      guidParam("ForecastId", ev.forecastId),
      stringParam("Location", ev.newLocation),
      stringParam("UpdatedAt", ev.updatedAt),
      stringParam("SortableUniqueId", sortableUniqueId),
    ],
  );
}

function applyEventStatements(bindings: WasmMvTableBindingsDto, ev: WasmMvSerializableEventDto): WasmMvSqlStatementDto[] {
  if (ev.eventType == "WeatherForecastCreated") {
    return [insertForecast(bindings, JSON.parse<WeatherForecastCreatedEv>(ev.payloadJson), ev.sortableUniqueId)];
  }
  if (ev.eventType == "WeatherForecastLocationUpdated") {
    return [updateLocation(bindings, JSON.parse<WeatherForecastLocationUpdatedEv>(ev.payloadJson), ev.sortableUniqueId)];
  }
  return [];
}

export function mv_metadata(): u64 {
  const metadata = new Array<WasmMvMetadataDto>();
  const dto = new WasmMvMetadataDto();
  dto.viewName = VIEW_NAME;
  dto.viewVersion = VIEW_VERSION;
  dto.logicalTables = [WEATHER_FORECAST_LOGICAL];
  metadata.push(dto);
  return writeStr(JSON.stringify<WasmMvMetadataDto[]>(metadata));
}

export function mv_initialize(
  viewNamePtr: u32,
  viewNameLen: u32,
  viewVersion: i32,
  bindingsPtr: u32,
  bindingsLen: u32,
): u64 {
  const viewName = readStr(viewNamePtr, viewNameLen);
  if (viewName != VIEW_NAME || viewVersion != VIEW_VERSION) {
    return writeStr(errorPayload("unknown materialized view: " + viewName));
  }

  const bindings = JSON.parse<WasmMvTableBindingsDto>(readStr(bindingsPtr, bindingsLen));
  if (tableName(bindings, WEATHER_FORECAST_LOGICAL) == "") {
    return writeStr(errorPayload("Missing physical table binding for logical table: " + WEATHER_FORECAST_LOGICAL));
  }

  return writeStr(statementBatchPayload(initializeStatements(bindings)));
}

export function mv_apply_event(
  viewNamePtr: u32,
  viewNameLen: u32,
  viewVersion: i32,
  bindingsPtr: u32,
  bindingsLen: u32,
  serializableEventPtr: u32,
  serializableEventLen: u32,
): u64 {
  const viewName = readStr(viewNamePtr, viewNameLen);
  if (viewName != VIEW_NAME || viewVersion != VIEW_VERSION) {
    return writeStr(errorPayload("unknown materialized view: " + viewName));
  }

  const bindings = JSON.parse<WasmMvTableBindingsDto>(readStr(bindingsPtr, bindingsLen));
  if (tableName(bindings, WEATHER_FORECAST_LOGICAL) == "") {
    return writeStr(errorPayload("Missing physical table binding for logical table: " + WEATHER_FORECAST_LOGICAL));
  }

  const serializableEvent = JSON.parse<WasmMvSerializableEventDto>(readStr(serializableEventPtr, serializableEventLen));
  return writeStr(statementBatchPayload(applyEventStatements(bindings, serializableEvent)));
}
