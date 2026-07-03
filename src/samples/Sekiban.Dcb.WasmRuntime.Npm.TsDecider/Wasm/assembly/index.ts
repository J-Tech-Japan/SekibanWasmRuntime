import { JSON } from "json-as/assembly";
import { readStr, writeStr, applyPaging } from "@sekiban/as-wasm/assembly";
export { alloc, dealloc } from "@sekiban/as-wasm/assembly";
export { mv_metadata, mv_initialize, mv_apply_event } from "./materialized_view";

// ---------------------------------------------------------------------------
// Weather forecast domain (mirrors the crates.io Rust decider sample:
// src/samples/Sekiban.Dcb.WasmRuntime.CratesIo.RsDecider/Domain/src/lib.rs)
// ---------------------------------------------------------------------------

const PROJECTOR_TAG = "WeatherForecastProjector";
const PROJECTOR_LIST = "WeatherForecastMultiProjection";

const EVENT_CREATED = "WeatherForecastCreated";
const EVENT_LOCATION_UPDATED = "WeatherForecastLocationUpdated";

const KIND_UNKNOWN: i32 = 0;
const KIND_TAG: i32 = 1;
const KIND_LIST: i32 = 2;

@json
class WeatherForecastState {
  forecastId: string = "";
  location: string = "";
  temperatureC: i32 = 0;
  summary: string = "";
  createdAt: string = "";
}

@json
class WeatherForecastItem {
  forecastId: string = "";
  location: string = "";
  temperatureC: i32 = 0;
  summary: string = "";
}

class WeatherForecastListState {
  items: Map<string, WeatherForecastItem> = new Map<string, WeatherForecastItem>();
}

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

// ---------------------------------------------------------------------------
// Projector instance storage
// ---------------------------------------------------------------------------

class Instance {
  kind: i32;
  tagState: WeatherForecastState = new WeatherForecastState();
  listState: WeatherForecastListState = new WeatherForecastListState();

  constructor(kind: i32) {
    this.kind = kind;
  }
}

const instances = new Map<i32, Instance>();
let nextId: i32 = 1;

function resolveKind(name: string): i32 {
  const lower = name.toLowerCase();
  if (lower == PROJECTOR_TAG.toLowerCase()) return KIND_TAG;
  if (lower == PROJECTOR_LIST.toLowerCase()) return KIND_LIST;
  return KIND_UNKNOWN;
}

// ---------------------------------------------------------------------------
// Exported WASM functions
// ---------------------------------------------------------------------------

export function create_instance(namePtr: u32, nameLen: u32): i32 {
  const name = readStr(namePtr, nameLen);
  const kind = resolveKind(name);
  if (kind == KIND_UNKNOWN) return -1;
  const id = nextId;
  nextId++;
  instances.set(id, new Instance(kind));
  return id;
}

function applyTag(s: WeatherForecastState, et: string, p: string): WeatherForecastState {
  if (et == EVENT_CREATED) {
    const ev = JSON.parse<WeatherForecastCreatedEv>(p);
    s.forecastId = ev.forecastId;
    s.location = ev.location;
    s.temperatureC = ev.temperatureC;
    s.summary = ev.summary;
    s.createdAt = ev.createdAt;
  } else if (et == EVENT_LOCATION_UPDATED) {
    const ev = JSON.parse<WeatherForecastLocationUpdatedEv>(p);
    s.location = ev.newLocation;
  }
  return s;
}

function applyList(s: WeatherForecastListState, et: string, p: string): WeatherForecastListState {
  if (et == EVENT_CREATED) {
    const ev = JSON.parse<WeatherForecastCreatedEv>(p);
    const item = new WeatherForecastItem();
    item.forecastId = ev.forecastId;
    item.location = ev.location;
    item.temperatureC = ev.temperatureC;
    item.summary = ev.summary;
    s.items.set(ev.forecastId, item);
  } else if (et == EVENT_LOCATION_UPDATED) {
    const ev = JSON.parse<WeatherForecastLocationUpdatedEv>(p);
    if (s.items.has(ev.forecastId)) {
      const item = s.items.get(ev.forecastId);
      item.location = ev.newLocation;
      s.items.set(ev.forecastId, item);
    }
  }
  return s;
}

export function apply_event(instanceId: i32, eventTypePtr: u32, eventTypeLen: u32, payloadPtr: u32, payloadLen: u32): void {
  if (!instances.has(instanceId)) return;
  const inst = instances.get(instanceId);
  const eventType = readStr(eventTypePtr, eventTypeLen);
  const payload = readStr(payloadPtr, payloadLen);

  if (inst.kind == KIND_TAG) {
    inst.tagState = applyTag(inst.tagState, eventType, payload);
  } else if (inst.kind == KIND_LIST) {
    inst.listState = applyList(inst.listState, eventType, payload);
  }
}

// ---------------------------------------------------------------------------
// serialize_state / restore_state
// ---------------------------------------------------------------------------

function serializeMapState<V>(items: Map<string, V>): string {
  const keys = items.keys();
  let result = '{"items":{';
  for (let i = 0; i < keys.length; i++) {
    if (i > 0) result += ",";
    result += '"' + keys[i] + '":' + JSON.stringify<V>(items.get(keys[i]));
  }
  result += "}}";
  return result;
}

export function serialize_state(instanceId: i32): u64 {
  if (!instances.has(instanceId)) return writeStr("{}");
  const inst = instances.get(instanceId);
  if (inst.kind == KIND_TAG) return writeStr(JSON.stringify<WeatherForecastState>(inst.tagState));
  if (inst.kind == KIND_LIST) return writeStr(serializeMapState<WeatherForecastItem>(inst.listState.items));
  return writeStr("{}");
}

function restoreMapState<V>(stateJson: string): Map<string, V> {
  const m = new Map<string, V>();
  const itemsIdx = stateJson.indexOf('"items"');
  if (itemsIdx < 0) return m;
  let braceStart = stateJson.indexOf("{", itemsIdx + 7);
  if (braceStart < 0) return m;

  let pos = braceStart + 1;
  while (pos < stateJson.length) {
    while (pos < stateJson.length && (stateJson.charCodeAt(pos) == 32 || stateJson.charCodeAt(pos) == 10 || stateJson.charCodeAt(pos) == 13 || stateJson.charCodeAt(pos) == 9)) pos++;
    if (pos >= stateJson.length || stateJson.charCodeAt(pos) == 125) break; // '}'
    if (stateJson.charCodeAt(pos) == 44) { pos++; continue; } // ','

    if (stateJson.charCodeAt(pos) != 34) break; // not '"'
    const keyEnd = stateJson.indexOf('"', pos + 1);
    if (keyEnd < 0) break;
    const key = stateJson.substring(pos + 1, keyEnd);
    pos = keyEnd + 1;

    while (pos < stateJson.length && stateJson.charCodeAt(pos) != 58) pos++;
    pos++; // skip ':'

    while (pos < stateJson.length && stateJson.charCodeAt(pos) != 123) pos++;
    const valStart = pos;
    let depth = 0;
    while (pos < stateJson.length) {
      if (stateJson.charCodeAt(pos) == 123) depth++;
      else if (stateJson.charCodeAt(pos) == 125) { depth--; if (depth == 0) { pos++; break; } }
      pos++;
    }
    const valJson = stateJson.substring(valStart, pos);
    m.set(key, JSON.parse<V>(valJson));
  }
  return m;
}

export function restore_state(instanceId: i32, statePtr: u32, stateLen: u32): void {
  if (!instances.has(instanceId)) return;
  const inst = instances.get(instanceId);
  const stateJson = readStr(statePtr, stateLen);
  if (stateJson.length == 0 || stateJson == "{}" || stateJson == "null") return;

  if (inst.kind == KIND_TAG) {
    inst.tagState = JSON.parse<WeatherForecastState>(stateJson);
  } else if (inst.kind == KIND_LIST) {
    inst.listState.items = restoreMapState<WeatherForecastItem>(stateJson);
  }
}

// ---------------------------------------------------------------------------
// execute_query / execute_list_query exports
// ---------------------------------------------------------------------------

@json
class WeatherListQuery {
  locationFilter: string = "";
  forecastId: string = "";
  waitForSortableUniqueId: string = "";
  pageSize: i32 = 0;
  pageNumber: i32 = 0;
}

@json
class CountResult {
  count: i32 = 0;
}

function matchesFilter(item: WeatherForecastItem, q: WeatherListQuery): bool {
  if (q.locationFilter.length > 0 && !item.location.toLowerCase().includes(q.locationFilter.toLowerCase())) return false;
  if (q.forecastId.length > 0 && item.forecastId != q.forecastId) return false;
  return true;
}

function executeListQuery(inst: Instance, paramsJson: string): string {
  const q = JSON.parse<WeatherListQuery>(paramsJson);
  const items = inst.listState.items;
  const result: WeatherForecastItem[] = [];
  const keys = items.keys();
  for (let i = 0; i < keys.length; i++) {
    const item = items.get(keys[i]);
    if (matchesFilter(item, q)) result.push(item);
  }
  const paged = applyPaging<WeatherForecastItem>(result, q.pageSize, q.pageNumber);
  return JSON.stringify<WeatherForecastItem[]>(paged);
}

function executeCountQuery(inst: Instance, paramsJson: string): string {
  const q = JSON.parse<WeatherListQuery>(paramsJson);
  const items = inst.listState.items;
  let count: i32 = 0;
  const keys = items.keys();
  for (let i = 0; i < keys.length; i++) {
    if (matchesFilter(items.get(keys[i]), q)) count++;
  }
  const r = new CountResult();
  r.count = count;
  return JSON.stringify<CountResult>(r);
}

export function execute_query(instanceId: i32, queryTypePtr: u32, queryTypeLen: u32, paramsPtr: u32, paramsLen: u32): u64 {
  if (!instances.has(instanceId)) return writeStr("{}");
  const inst = instances.get(instanceId);
  const queryType = readStr(queryTypePtr, queryTypeLen);
  const paramsJson = readStr(paramsPtr, paramsLen);

  if (queryType == "GetWeatherForecastCountQuery") return writeStr(executeCountQuery(inst, paramsJson));
  return writeStr("{}");
}

export function execute_list_query(instanceId: i32, queryTypePtr: u32, queryTypeLen: u32, paramsPtr: u32, paramsLen: u32): u64 {
  if (!instances.has(instanceId)) return writeStr("[]");
  const inst = instances.get(instanceId);
  const queryType = readStr(queryTypePtr, queryTypeLen);
  const paramsJson = readStr(paramsPtr, paramsLen);

  if (queryType == "GetWeatherForecastListQuery") return writeStr(executeListQuery(inst, paramsJson));
  return writeStr("[]");
}

// ---------------------------------------------------------------------------
// get_event_types export
// ---------------------------------------------------------------------------

export function get_event_types(): u64 {
  return writeStr('["WeatherForecastCreated","WeatherForecastLocationUpdated"]');
}
