// TinyGo WASI reactor module for the Go published-module consumer proof.
// Exposes the Sekiban projector ABI (create_instance / apply_event /
// serialize_state / restore_state / execute_query / execute_list_query plus
// the mv_* materialized-view exports) for the WeatherForecast domain, built
// exclusively on the public sekiban-go module.
package main

import (
	"encoding/json"
	"sort"
	"strings"

	"sekiban-wasm-runtime-gomodule-godecider/domain"

	sekiban "github.com/J-Tech-Japan/SekibanWasmRuntime/src/lib/sekiban-go/domain"
	"github.com/J-Tech-Japan/SekibanWasmRuntime/src/lib/sekiban-go/mv"
	"github.com/J-Tech-Japan/SekibanWasmRuntime/src/lib/sekiban-go/wasm"
)

// ---------------------------------------------------------------------------
// Instance storage
// ---------------------------------------------------------------------------

const (
	kindUnknown     = 0
	kindWeatherTag  = 1
	kindWeatherList = 2
)

type instance struct {
	kind        int
	weatherTag  domain.WeatherForecastState
	weatherList domain.WeatherForecastListState
}

var (
	instances       = make(map[int32]*instance)
	nextID    int32 = 1
)

func newInstance(kind int) *instance {
	inst := &instance{kind: kind}
	inst.weatherList.Items = make(map[string]domain.WeatherForecastItem)
	return inst
}

func resolveKind(name string) int {
	switch strings.ToLower(name) {
	case strings.ToLower(domain.WeatherTagProjector):
		return kindWeatherTag
	case strings.ToLower(domain.WeatherListProjector):
		return kindWeatherList
	default:
		return kindUnknown
	}
}

// ---------------------------------------------------------------------------
// Memory exports
// ---------------------------------------------------------------------------

//export alloc
func alloc(size uint32) uint32 { return wasm.Alloc(size) }

//export dealloc
func dealloc(ptr, length uint32) { wasm.Dealloc(ptr, length) }

//export reset_allocations
func reset_allocations() { wasm.ResetAllocations() }

// ---------------------------------------------------------------------------
// Instance lifecycle
// ---------------------------------------------------------------------------

//export create_instance
func create_instance(namePtr, nameLen uint32) int32 {
	name := wasm.ReadString(namePtr, nameLen)
	kind := resolveKind(name)
	if kind == kindUnknown {
		return -1
	}
	id := nextID
	nextID++
	instances[id] = newInstance(kind)
	return id
}

// ---------------------------------------------------------------------------
// Event application
// ---------------------------------------------------------------------------

//export apply_event
func apply_event(instanceId int32, etPtr, etLen, pPtr, pLen uint32) {
	inst, ok := instances[instanceId]
	if !ok {
		return
	}
	eventType := wasm.ReadString(etPtr, etLen)
	payload := wasm.ReadString(pPtr, pLen)
	switch inst.kind {
	case kindWeatherTag:
		applyWeatherTag(&inst.weatherTag, eventType, payload)
	case kindWeatherList:
		applyWeatherList(&inst.weatherList, eventType, payload)
	}
}

//export apply_event_with_metadata
func apply_event_with_metadata(instanceId int32, etPtr, etLen, pPtr, pLen, mPtr, mLen uint32) {
	// Event metadata is not needed by this domain.
	apply_event(instanceId, etPtr, etLen, pPtr, pLen)
}

func applyWeatherTag(state *domain.WeatherForecastState, eventType, payload string) {
	switch eventType {
	case "WeatherForecastCreated":
		var ev domain.WeatherForecastCreated
		if json.Unmarshal([]byte(payload), &ev) != nil {
			return
		}
		*state = domain.WeatherForecastState{
			ForecastId:   ev.ForecastId,
			Location:     ev.Location,
			TemperatureC: ev.TemperatureC,
			Summary:      ev.Summary,
			CreatedAt:    ev.CreatedAt,
		}
	case "WeatherForecastLocationUpdated":
		var ev domain.WeatherForecastLocationUpdated
		if json.Unmarshal([]byte(payload), &ev) != nil {
			return
		}
		state.Location = ev.NewLocation
		state.UpdatedAt = ev.UpdatedAt
	}
}

func applyWeatherList(state *domain.WeatherForecastListState, eventType, payload string) {
	switch eventType {
	case "WeatherForecastCreated":
		var ev domain.WeatherForecastCreated
		if json.Unmarshal([]byte(payload), &ev) != nil {
			return
		}
		state.Items[ev.ForecastId] = domain.WeatherForecastItem{
			ForecastId:   ev.ForecastId,
			Location:     ev.Location,
			TemperatureC: ev.TemperatureC,
			Summary:      ev.Summary,
			CreatedAt:    ev.CreatedAt,
		}
	case "WeatherForecastLocationUpdated":
		var ev domain.WeatherForecastLocationUpdated
		if json.Unmarshal([]byte(payload), &ev) != nil {
			return
		}
		if item, ok := state.Items[ev.ForecastId]; ok {
			item.Location = ev.NewLocation
			item.UpdatedAt = ev.UpdatedAt
			state.Items[ev.ForecastId] = item
		}
	}
}

// ---------------------------------------------------------------------------
// State serialization / restoration
// ---------------------------------------------------------------------------

//export serialize_state
func serialize_state(instanceId int32) int64 {
	inst, ok := instances[instanceId]
	if !ok {
		return wasm.WriteString("{}")
	}
	switch inst.kind {
	case kindWeatherTag:
		if inst.weatherTag.ForecastId == "" {
			return wasm.WriteString("{}")
		}
		return wasm.WriteString(marshalOr(inst.weatherTag, "{}"))
	case kindWeatherList:
		return wasm.WriteString(marshalOr(inst.weatherList, `{"items":{}}`))
	default:
		return wasm.WriteString("{}")
	}
}

//export restore_state
func restore_state(instanceId int32, sPtr, sLen uint32) {
	inst, ok := instances[instanceId]
	if !ok {
		return
	}
	stateJSON := wasm.ReadString(sPtr, sLen)
	if sekiban.IsEmptyJSON(stateJSON) {
		return
	}
	switch inst.kind {
	case kindWeatherTag:
		_ = json.Unmarshal([]byte(stateJSON), &inst.weatherTag)
	case kindWeatherList:
		_ = json.Unmarshal([]byte(stateJSON), &inst.weatherList)
		if inst.weatherList.Items == nil {
			inst.weatherList.Items = make(map[string]domain.WeatherForecastItem)
		}
	}
}

func marshalOr(value any, fallback string) string {
	data, err := json.Marshal(value)
	if err != nil {
		return fallback
	}
	return string(data)
}

// ---------------------------------------------------------------------------
// Queries
// ---------------------------------------------------------------------------

type weatherListQuery struct {
	LocationFilter *string `json:"locationFilter"`
	ForecastId     *string `json:"forecastId"`
	PageNumber     *int    `json:"pageNumber"`
	PageSize       *int    `json:"pageSize"`
}

//export execute_query
func execute_query(instanceId int32, qtPtr, qtLen, pPtr, pLen uint32) int64 {
	inst, ok := instances[instanceId]
	if !ok {
		return wasm.WriteString("null")
	}
	queryType := wasm.ReadString(qtPtr, qtLen)
	if inst.kind == kindWeatherList && queryType == "GetWeatherForecastCountQuery" {
		return wasm.WriteString(marshalOr(sekiban.CountResult{Count: len(inst.weatherList.Items)}, `{"count":0}`))
	}
	return wasm.WriteString("null")
}

//export execute_list_query
func execute_list_query(instanceId int32, qtPtr, qtLen, pPtr, pLen uint32) int64 {
	inst, ok := instances[instanceId]
	if !ok {
		return wasm.WriteString("[]")
	}
	queryType := wasm.ReadString(qtPtr, qtLen)
	paramsJSON := wasm.ReadString(pPtr, pLen)
	if inst.kind != kindWeatherList || queryType != "GetWeatherForecastListQuery" {
		return wasm.WriteString("[]")
	}

	var query weatherListQuery
	_ = json.Unmarshal([]byte(paramsJSON), &query)

	items := make([]domain.WeatherForecastItem, 0, len(inst.weatherList.Items))
	for _, item := range inst.weatherList.Items {
		if query.ForecastId != nil && *query.ForecastId != "" && item.ForecastId != *query.ForecastId {
			continue
		}
		if query.LocationFilter != nil && *query.LocationFilter != "" &&
			!strings.Contains(strings.ToLower(item.Location), strings.ToLower(*query.LocationFilter)) {
			continue
		}
		items = append(items, item)
	}
	sort.Slice(items, func(i, j int) bool { return items[i].CreatedAt > items[j].CreatedAt })
	items = sekiban.ApplyPaging(items, sekiban.PagingQuery{PageNumber: query.PageNumber, PageSize: query.PageSize})
	return wasm.WriteString(marshalOr(items, "[]"))
}

// ---------------------------------------------------------------------------
// Materialized view exports
// ---------------------------------------------------------------------------

func mvProjectors() []mv.Projector {
	return []mv.Projector{domain.WeatherForecastMvV1{}}
}

//export mv_metadata
func mv_metadata() int64 {
	return mv.Metadata(mvProjectors())
}

//export mv_initialize
func mv_initialize(viewNamePtr, viewNameLen uint32, viewVersion int32,
	bindingsPtr, bindingsLen uint32) int64 {
	viewName := wasm.ReadString(viewNamePtr, viewNameLen)
	bindingsJSON := wasm.ReadString(bindingsPtr, bindingsLen)
	return mv.Initialize(mvProjectors(), viewName, viewVersion, bindingsJSON)
}

//export mv_apply_event
func mv_apply_event(viewNamePtr, viewNameLen uint32, viewVersion int32,
	bindingsPtr, bindingsLen uint32,
	eventPtr, eventLen uint32) int64 {
	viewName := wasm.ReadString(viewNamePtr, viewNameLen)
	bindingsJSON := wasm.ReadString(bindingsPtr, bindingsLen)
	eventJSON := wasm.ReadString(eventPtr, eventLen)
	return mv.ApplyEvent(mvProjectors(), viewName, viewVersion, bindingsJSON, eventJSON)
}

// Required for TinyGo WASI reactor builds.
func main() {}
