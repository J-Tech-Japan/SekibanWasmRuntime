// WeatherForecast materialized-view projector for the mooncakes.io external-
// consumer proof: identical SQL and parameters to the Rust/Go/Swift samples so
// the registry rows and physical tables line up across SDK languages.

let mv_view_name = "WeatherForecast"

let mv_view_version = 1

let mv_weather_logical = "weather_forecast"

let mv_kind_string = 1

let mv_kind_int32 = 2

let mv_kind_guid = 5

struct WasmMvParam {
  name : String
  kind : Int
  valueJson : String?
} derive(ToJson, FromJson, Show, Eq)

struct WasmMvSqlStatementDto {
  sql : String
  parameters : Array[WasmMvParam]
} derive(ToJson, FromJson, Show, Eq)

struct WasmMvTableBindingEntry {
  logical : String
  physical : String
} derive(ToJson, FromJson, Show, Eq)

struct WasmMvTableBindingsDto {
  bindings : Array[WasmMvTableBindingEntry]
} derive(ToJson, FromJson, Show, Eq)

struct WasmMvMetadataDto {
  viewName : String
  viewVersion : Int
  logicalTables : Array[String]
} derive(ToJson, FromJson, Show, Eq)

struct WasmMvSerializableEventDto {
  eventType : String
  payloadJson : String
  sortableUniqueId : String
  tags : Array[String]
} derive(ToJson, FromJson, Show, Eq)

struct WasmMvStatementBatchDto {
  statements : Array[WasmMvSqlStatementDto]
} derive(ToJson, FromJson, Show, Eq)

fn error_payload(message : String) -> String {
  let payload : Json = { "error": message.to_json() }
  json_stringify(payload)
}

fn statement_batch_payload(statements : Array[WasmMvSqlStatementDto]) -> String {
  json_stringify(WasmMvStatementBatchDto::{ statements, }.to_json())
}

fn table_name(bindings : WasmMvTableBindingsDto, logical : String) -> String {
  for binding in bindings.bindings {
    if binding.logical == logical {
      return binding.physical
    }
  }
  abort("Missing physical table binding for logical table: " + logical)
}

fn sql_param(name : String, kind : Int, value_json : String) -> WasmMvParam {
  { name, kind, valueJson: Some(value_json) }
}

fn guid_param(name : String, value : String) -> WasmMvParam {
  sql_param(name, mv_kind_guid, json_stringify(value.to_json()))
}

fn string_param(name : String, value : String) -> WasmMvParam {
  sql_param(name, mv_kind_string, json_stringify(value.to_json()))
}

fn int32_param(name : String, value : Int) -> WasmMvParam {
  sql_param(name, mv_kind_int32, json_stringify(value.to_json()))
}

fn statement(sql : String, parameters : Array[WasmMvParam]) -> WasmMvSqlStatementDto {
  { sql, parameters }
}

fn initialize_statements(bindings : WasmMvTableBindingsDto) -> Array[WasmMvSqlStatementDto] {
  let table = table_name(bindings, mv_weather_logical)
  [
    statement(
      "CREATE TABLE IF NOT EXISTS " +
      table +
      " (forecast_id UUID PRIMARY KEY, location TEXT NOT NULL, temperature_c INT NOT NULL, summary TEXT NOT NULL, created_at TEXT NOT NULL, updated_at TEXT NULL, _last_sortable_unique_id TEXT NOT NULL, _last_applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW());",
      [],
    ),
  ]
}

fn insert_forecast(
  bindings : WasmMvTableBindingsDto,
  created : WeatherForecastCreated,
  sortable_unique_id : String
) -> Array[WasmMvSqlStatementDto] {
  let table = table_name(bindings, mv_weather_logical)
  [
    statement(
      "INSERT INTO " +
      table +
      " (forecast_id, location, temperature_c, summary, created_at, updated_at, _last_sortable_unique_id, _last_applied_at) VALUES (@ForecastId, @Location, @TemperatureC, @Summary, @CreatedAt, NULL, @SortableUniqueId, NOW()) ON CONFLICT (forecast_id) DO UPDATE SET location = EXCLUDED.location, temperature_c = EXCLUDED.temperature_c, summary = EXCLUDED.summary, created_at = EXCLUDED.created_at, updated_at = NULL, _last_sortable_unique_id = EXCLUDED._last_sortable_unique_id, _last_applied_at = EXCLUDED._last_applied_at WHERE " +
      table +
      "._last_sortable_unique_id < EXCLUDED._last_sortable_unique_id;",
      [
        guid_param("ForecastId", created.forecastId),
        string_param("Location", created.location),
        int32_param("TemperatureC", created.temperatureC),
        string_param("Summary", created.summary),
        string_param("CreatedAt", created.createdAt),
        string_param("SortableUniqueId", sortable_unique_id),
      ],
    ),
  ]
}

fn update_location(
  bindings : WasmMvTableBindingsDto,
  updated : WeatherForecastLocationUpdated,
  sortable_unique_id : String
) -> Array[WasmMvSqlStatementDto] {
  let table = table_name(bindings, mv_weather_logical)
  [
    statement(
      "UPDATE " +
      table +
      " SET location = @Location, updated_at = @UpdatedAt, _last_sortable_unique_id = @SortableUniqueId, _last_applied_at = NOW() WHERE forecast_id = @ForecastId AND _last_sortable_unique_id < @SortableUniqueId;",
      [
        guid_param("ForecastId", updated.forecastId),
        string_param("Location", updated.newLocation),
        string_param("UpdatedAt", updated.updatedAt),
        string_param("SortableUniqueId", sortable_unique_id),
      ],
    ),
  ]
}

fn apply_event_statements(
  bindings : WasmMvTableBindingsDto,
  serializable_event : WasmMvSerializableEventDto
) -> Array[WasmMvSqlStatementDto] {
  let event_type = serializable_event.eventType
  let sortable_unique_id = serializable_event.sortableUniqueId
  if event_type == event_weather_created {
    let payload = parse_json(serializable_event.payloadJson, WeatherForecastCreated::{
      forecastId: "",
      location: "",
      temperatureC: 0,
      summary: "",
      createdAt: "",
    })
    return insert_forecast(bindings, payload, sortable_unique_id)
  }
  if event_type == event_weather_location_updated {
    let payload = parse_json(serializable_event.payloadJson, WeatherForecastLocationUpdated::{
      forecastId: "",
      newLocation: "",
      updatedAt: "",
    })
    return update_location(bindings, payload, sortable_unique_id)
  }
  []
}

// ---------------------------------------------------------------------------
// mv_* exports
// ---------------------------------------------------------------------------

pub fn mv_metadata() -> Int64 {
  let metadata : Array[WasmMvMetadataDto] = [
    WasmMvMetadataDto::{
      viewName: mv_view_name,
      viewVersion: mv_view_version,
      logicalTables: [mv_weather_logical],
    },
  ]
  @core.write_utf8(json_stringify(metadata.to_json()))
}

pub fn mv_initialize(
  view_name_ptr : UInt,
  view_name_len : UInt,
  view_version : Int,
  bindings_ptr : UInt,
  bindings_len : UInt
) -> Int64 {
  let view_name = @core.read_utf8(view_name_ptr.reinterpret_as_int(), view_name_len.reinterpret_as_int())
  if view_name != mv_view_name || view_version != mv_view_version {
    return @core.write_utf8(error_payload("unknown materialized view: " + view_name))
  }
  let bindings_json = @core.read_utf8(bindings_ptr.reinterpret_as_int(), bindings_len.reinterpret_as_int())
  let bindings = parse_json(bindings_json, WasmMvTableBindingsDto::{ bindings: [] })
  @core.write_utf8(statement_batch_payload(initialize_statements(bindings)))
}

pub fn mv_apply_event(
  view_name_ptr : UInt,
  view_name_len : UInt,
  view_version : Int,
  bindings_ptr : UInt,
  bindings_len : UInt,
  event_ptr : UInt,
  event_len : UInt
) -> Int64 {
  let view_name = @core.read_utf8(view_name_ptr.reinterpret_as_int(), view_name_len.reinterpret_as_int())
  if view_name != mv_view_name || view_version != mv_view_version {
    return @core.write_utf8(error_payload("unknown materialized view: " + view_name))
  }
  let bindings_json = @core.read_utf8(bindings_ptr.reinterpret_as_int(), bindings_len.reinterpret_as_int())
  let event_json = @core.read_utf8(event_ptr.reinterpret_as_int(), event_len.reinterpret_as_int())
  let bindings = parse_json(bindings_json, WasmMvTableBindingsDto::{ bindings: [] })
  let serializable_event = parse_json(event_json, WasmMvSerializableEventDto::{
    eventType: "",
    payloadJson: "",
    sortableUniqueId: "",
    tags: [],
  })
  @core.write_utf8(statement_batch_payload(apply_event_statements(bindings, serializable_event)))
}
