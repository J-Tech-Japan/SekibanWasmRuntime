// WeatherForecast Decider projector module for the mooncakes.io external-
// consumer proof (SWR-G065). Built exclusively on the public
// sekiban/sekiban-wasm-runtime package; mirrors the crates.io Rust and Go
// published-artifact samples (same events, states, queries, and MV SQL) so
// the evidence is comparable across SDK languages.

// ---------------------------------------------------------------------------
// Events / states / queries
// ---------------------------------------------------------------------------

let event_weather_created = "WeatherForecastCreated"

let event_weather_location_updated = "WeatherForecastLocationUpdated"

let query_weather_list = "GetWeatherForecastListQuery"

let query_weather_count = "GetWeatherForecastCountQuery"

let projector_weather_tag = "WeatherForecastProjector"

let projector_weather_list = "WeatherForecastMultiProjection"

struct WeatherForecastCreated {
  forecastId : String
  location : String
  temperatureC : Int
  summary : String
  createdAt : String
} derive(ToJson, FromJson, Show, Eq)

struct WeatherForecastLocationUpdated {
  forecastId : String
  newLocation : String
  updatedAt : String
} derive(ToJson, FromJson, Show, Eq)

struct WeatherForecastState {
  forecastId : String
  location : String
  temperatureC : Int
  summary : String
  createdAt : String
  updatedAt : String?
} derive(ToJson, FromJson, Show, Eq)

struct WeatherForecastListState {
  items : Map[String, WeatherForecastState]
} derive(ToJson, FromJson, Show, Eq)

struct WeatherListQuery {
  locationFilter : String?
  forecastId : String?
  waitForSortableUniqueId : String?
  pageNumber : Int?
  pageSize : Int?
} derive(ToJson, FromJson, Show, Eq)

struct CountResult {
  count : Int
} derive(ToJson, FromJson, Show, Eq)

fn empty_weather_state() -> WeatherForecastState {
  {
    forecastId: "",
    location: "",
    temperatureC: 0,
    summary: "",
    createdAt: "",
    updatedAt: None,
  }
}

fn empty_weather_list_state() -> WeatherForecastListState {
  { items: {} }
}

fn[T : @json.FromJson] parse_json(s : String, fallback : T) -> T {
  @core.parse_json(s, fallback)
}

fn json_stringify(j : Json) -> String {
  @core.json_stringify(j)
}

// ---------------------------------------------------------------------------
// Projector instances
// ---------------------------------------------------------------------------

enum Kind {
  Unknown
  WeatherTag
  WeatherList
} derive(Show, Eq)

struct Instance {
  kind : Kind
  weatherTag : WeatherForecastState
  weatherList : WeatherForecastListState
}

fn instance_new(kind : Kind) -> Instance {
  { kind, weatherTag: empty_weather_state(), weatherList: empty_weather_list_state() }
}

let instances : Map[UInt, Instance] = {}

let next_id : Ref[UInt] = { val: 1 }

fn take_next_id() -> UInt {
  let id = next_id.val
  next_id.val = id + 1
  id
}

fn resolve_kind(name : String) -> Kind {
  if name == projector_weather_tag {
    WeatherTag
  } else if name == projector_weather_list {
    WeatherList
  } else {
    Unknown
  }
}

fn apply_weather_tag(state : WeatherForecastState, event_type : String, payload : String) -> WeatherForecastState {
  if event_type == event_weather_created {
    let ev = parse_json(payload, WeatherForecastCreated::{
      forecastId: "",
      location: "",
      temperatureC: 0,
      summary: "",
      createdAt: "",
    })
    {
      forecastId: ev.forecastId,
      location: ev.location,
      temperatureC: ev.temperatureC,
      summary: ev.summary,
      createdAt: ev.createdAt,
      updatedAt: None,
    }
  } else if event_type == event_weather_location_updated {
    let ev = parse_json(payload, WeatherForecastLocationUpdated::{ forecastId: "", newLocation: "", updatedAt: "" })
    { ..state, location: ev.newLocation, updatedAt: Some(ev.updatedAt) }
  } else {
    state
  }
}

fn apply_weather_list(state : WeatherForecastListState, event_type : String, payload : String) -> WeatherForecastListState {
  let items = state.items
  if event_type == event_weather_created {
    let ev = parse_json(payload, WeatherForecastCreated::{
      forecastId: "",
      location: "",
      temperatureC: 0,
      summary: "",
      createdAt: "",
    })
    items[ev.forecastId] = {
      forecastId: ev.forecastId,
      location: ev.location,
      temperatureC: ev.temperatureC,
      summary: ev.summary,
      createdAt: ev.createdAt,
      updatedAt: None,
    }
  } else if event_type == event_weather_location_updated {
    let ev = parse_json(payload, WeatherForecastLocationUpdated::{ forecastId: "", newLocation: "", updatedAt: "" })
    match items.get(ev.forecastId) {
      None => ()
      Some(cur) => items[ev.forecastId] = { ..cur, location: ev.newLocation, updatedAt: Some(ev.updatedAt) }
    }
  }
  { items, }
}

fn apply_event_to_instance(inst : Instance, event_type : String, payload_json : String) -> Instance {
  match inst.kind {
    WeatherTag => { ..inst, weatherTag: apply_weather_tag(inst.weatherTag, event_type, payload_json) }
    WeatherList => { ..inst, weatherList: apply_weather_list(inst.weatherList, event_type, payload_json) }
    Unknown => inst
  }
}

fn serialize_instance_state(inst : Instance) -> String {
  match inst.kind {
    WeatherTag => if inst.weatherTag.forecastId == "" { "{}" } else { json_stringify(inst.weatherTag.to_json()) }
    WeatherList => json_stringify(inst.weatherList.to_json())
    Unknown => "{}"
  }
}

fn restore_instance_state(inst : Instance, state_json : String) -> Instance {
  if @core.is_empty_json(state_json) {
    return instance_new(inst.kind)
  }
  match inst.kind {
    WeatherTag => { ..inst, weatherTag: parse_json(state_json, empty_weather_state()) }
    WeatherList => { ..inst, weatherList: parse_json(state_json, empty_weather_list_state()) }
    Unknown => inst
  }
}

fn weather_items(state : WeatherForecastListState, query : WeatherListQuery) -> Array[WeatherForecastState] {
  let items : Array[WeatherForecastState] = []
  for kv in state.items.iter() {
    let (_k, v) = kv
    match query.forecastId {
      None => ()
      Some(forecast_id) => if v.forecastId != forecast_id { continue }
    }
    if !@projector.matches_optional_filter(v.location, query.locationFilter) {
      continue
    }
    items.push(v)
  }
  items.sort_by((left, right) => right.createdAt.compare(left.createdAt))
  @projector.apply_paging(items, query.pageNumber, query.pageSize)
}

fn execute_query_json(inst : Instance, query_type : String, params_json : String) -> String {
  if inst.kind == WeatherList && query_type == query_weather_count {
    let query = parse_json(params_json, WeatherListQuery::{
      locationFilter: None,
      forecastId: None,
      waitForSortableUniqueId: None,
      pageNumber: None,
      pageSize: None,
    })
    let items = weather_items(inst.weatherList, query)
    return json_stringify(CountResult::{ count: items.length() }.to_json())
  }
  "null"
}

fn execute_list_query_json(inst : Instance, query_type : String, params_json : String) -> String {
  if inst.kind == WeatherList && query_type == query_weather_list {
    let query = parse_json(params_json, WeatherListQuery::{
      locationFilter: None,
      forecastId: None,
      waitForSortableUniqueId: None,
      pageNumber: None,
      pageSize: None,
    })
    return json_stringify(weather_items(inst.weatherList, query).to_json())
  }
  "[]"
}

// ---------------------------------------------------------------------------
// Register callbacks with the sekiban/sekiban-wasm-runtime projector dispatch
// ---------------------------------------------------------------------------

fn kind_to_int(kind : Kind) -> Int {
  match kind {
    Unknown => 0
    WeatherTag => 1
    WeatherList => 2
  }
}

fn int_to_kind(id : Int) -> Kind {
  match id {
    1 => WeatherTag
    2 => WeatherList
    _ => Unknown
  }
}

fn init {
  @projector.register_callbacks(@projector.ProjectorCallbacks::{
    resolve_kind: fn(name) {
      let kind = resolve_kind(name)
      if kind == Unknown {
        0
      } else {
        kind_to_int(kind)
      }
    },
    create_instance: fn(kind_id) {
      let kind = int_to_kind(kind_id)
      let id = take_next_id()
      instances[id] = instance_new(kind)
      id
    },
    apply_event: fn(id, event_type, payload_json) {
      match instances.get(id) {
        None => ()
        Some(inst) => instances[id] = apply_event_to_instance(inst, event_type, payload_json)
      }
    },
    apply_event_with_metadata: fn(id, event_type, payload_json, _metadata_json) {
      match instances.get(id) {
        None => ()
        Some(inst) => instances[id] = apply_event_to_instance(inst, event_type, payload_json)
      }
    },
    serialize_state: fn(id) {
      match instances.get(id) {
        None => "{}"
        Some(inst) => serialize_instance_state(inst)
      }
    },
    restore_state: fn(id, state_json) {
      match instances.get(id) {
        None => ()
        Some(inst) => instances[id] = restore_instance_state(inst, state_json)
      }
    },
    execute_query: fn(id, query_type, params_json) {
      match instances.get(id) {
        None => "null"
        Some(inst) => execute_query_json(inst, query_type, params_json)
      }
    },
    execute_list_query: fn(id, query_type, params_json) {
      match instances.get(id) {
        None => "[]"
        Some(inst) => execute_list_query_json(inst, query_type, params_json)
      }
    },
  })
}

// ---------------------------------------------------------------------------
// WASM exports (re-exported so they appear in this package's link exports)
// ---------------------------------------------------------------------------

pub fn alloc(size : UInt) -> UInt {
  @core.alloc(size)
}

pub fn dealloc(ptr : UInt, len : UInt) -> Unit {
  @core.dealloc(ptr, len)
}

pub fn create_instance(pt_ptr : UInt, pt_len : UInt) -> UInt {
  @projector.create_instance(pt_ptr, pt_len)
}

pub fn apply_event(id : UInt, et_ptr : UInt, et_len : UInt, p_ptr : UInt, p_len : UInt) -> Unit {
  @projector.apply_event(id, et_ptr, et_len, p_ptr, p_len)
}

pub fn apply_event_with_metadata(
  id : UInt,
  et_ptr : UInt,
  et_len : UInt,
  p_ptr : UInt,
  p_len : UInt,
  m_ptr : UInt,
  m_len : UInt
) -> Unit {
  @projector.apply_event_with_metadata(id, et_ptr, et_len, p_ptr, p_len, m_ptr, m_len)
}

pub fn execute_query(id : UInt, qt_ptr : UInt, qt_len : UInt, p_ptr : UInt, p_len : UInt) -> Int64 {
  @projector.execute_query(id, qt_ptr, qt_len, p_ptr, p_len)
}

pub fn execute_list_query(id : UInt, qt_ptr : UInt, qt_len : UInt, p_ptr : UInt, p_len : UInt) -> Int64 {
  @projector.execute_list_query(id, qt_ptr, qt_len, p_ptr, p_len)
}

pub fn serialize_state(id : UInt) -> Int64 {
  @projector.serialize_state(id)
}

pub fn restore_state(id : UInt, s_ptr : UInt, s_len : UInt) -> Unit {
  @projector.restore_state(id, s_ptr, s_len)
}
