// Typed Go client smoke for the published-module consumer proof. Mirrors the
// crates.io Rust sample's client: create a forecast, update its location,
// read the tag state back, and confirm the in-memory list and count queries —
// all through the public runtime container's HTTP contract via the public
// sekiban-go module.
package main

import (
	"encoding/json"
	"fmt"
	"os"
	"time"

	"sekiban-wasm-runtime-gomodule-godecider/domain"

	"github.com/J-Tech-Japan/SekibanWasmRuntime/src/lib/sekiban-go/client"
	"github.com/google/uuid"
)

type smokeEvidence struct {
	ForecastId       string `json:"forecastId"`
	OriginalLocation string `json:"originalLocation"`
	UpdatedLocation  string `json:"updatedLocation"`
	SortableUniqueId string `json:"sortableUniqueId,omitempty"`
	TagStateVersion  int    `json:"tagStateVersion"`
	TagStateLocation string `json:"tagStateLocation"`
	ListQueryCount   int    `json:"listQueryCount"`
	CountQueryCount  int    `json:"countQueryCount"`
	FoundInListQuery bool   `json:"foundInListQuery"`
}

func fatalf(format string, args ...any) {
	fmt.Fprintf(os.Stderr, format+"\n", args...)
	os.Exit(1)
}

func envOr(key, fallback string) string {
	if value := os.Getenv(key); value != "" {
		return value
	}
	return fallback
}

func main() {
	baseURL := envOr("RUNTIME_URL", "http://localhost:8080")
	originalLocation := envOr("SAMPLE_FORECAST_LOCATION", "Kyoto")
	updatedLocation := envOr("SAMPLE_UPDATED_LOCATION", "Osaka")
	forecastID := envOr("SAMPLE_FORECAST_ID", uuid.NewString())

	runtime := client.NewSekibanRuntimeClient(baseURL, domain.TagProjectorMap)

	created, err := runtime.FinalizeCommand(domain.CreateWeatherForecast{
		ForecastId:   forecastID,
		Location:     originalLocation,
		TemperatureC: 24,
		Summary:      "go module sample",
	})
	if err != nil {
		fatalf("CreateWeatherForecast failed: %v", err)
	}

	updated, err := runtime.FinalizeCommand(domain.UpdateWeatherForecastLocation{
		ForecastId:  forecastID,
		NewLocation: updatedLocation,
	})
	if err != nil {
		fatalf("UpdateWeatherForecastLocation failed: %v", err)
	}

	tagState, err := runtime.GetTagState(domain.TagGroupWeather, forecastID)
	if err != nil {
		fatalf("tag-state read failed: %v", err)
	}
	var state domain.WeatherForecastState
	if err := json.Unmarshal([]byte(tagState.PayloadJson), &state); err != nil {
		fatalf("tag-state decode failed: %v (payload=%s)", err, tagState.PayloadJson)
	}
	if state.ForecastId != forecastID || state.Location != updatedLocation {
		fatalf("tag-state mismatch: expected %s/%s, got %s/%s",
			forecastID, updatedLocation, state.ForecastId, state.Location)
	}

	waitFor := ""
	if updated != nil && updated.SortableUniqueId != nil {
		waitFor = *updated.SortableUniqueId
	}
	if waitFor == "" && created != nil && created.SortableUniqueId != nil {
		waitFor = *created.SortableUniqueId
	}
	var waitForPtr *string
	if waitFor != "" {
		waitForPtr = &waitFor
	}

	// In-memory projection queries; poll until the multi-projection catches up.
	var items []domain.WeatherForecastItem
	found := false
	for attempt := 0; attempt < 30 && !found; attempt++ {
		paramsJSON, _ := json.Marshal(map[string]any{"forecastId": forecastID})
		result, err := runtime.ExecuteListQuery("GetWeatherForecastListQuery", string(paramsJSON), waitForPtr)
		if err != nil {
			fatalf("list-query failed: %v", err)
		}
		items = nil
		if err := json.Unmarshal([]byte(result), &items); err != nil {
			fatalf("list-query decode failed: %v (result=%s)", err, result)
		}
		for _, item := range items {
			if item.ForecastId == forecastID && item.Location == updatedLocation {
				found = true
				break
			}
		}
		if !found {
			time.Sleep(2 * time.Second)
		}
	}
	if !found {
		fatalf("list-query did not return forecast %s with location %s", forecastID, updatedLocation)
	}

	countResult, err := runtime.ExecuteQuery("GetWeatherForecastCountQuery", "{}", waitForPtr)
	if err != nil {
		fatalf("count-query failed: %v", err)
	}
	var count struct {
		Count int `json:"count"`
	}
	if err := json.Unmarshal([]byte(countResult), &count); err != nil {
		fatalf("count-query decode failed: %v (result=%s)", err, countResult)
	}
	if count.Count < 1 {
		fatalf("count-query returned %d, expected >= 1", count.Count)
	}

	evidence := smokeEvidence{
		ForecastId:       forecastID,
		OriginalLocation: originalLocation,
		UpdatedLocation:  updatedLocation,
		SortableUniqueId: waitFor,
		TagStateVersion:  tagState.Version,
		TagStateLocation: state.Location,
		ListQueryCount:   len(items),
		CountQueryCount:  count.Count,
		FoundInListQuery: found,
	}
	output, _ := json.Marshal(evidence)
	fmt.Println(string(output))
}
