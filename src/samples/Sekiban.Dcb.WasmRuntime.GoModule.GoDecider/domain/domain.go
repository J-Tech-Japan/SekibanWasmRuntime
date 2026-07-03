// Package domain is the WeatherForecast Decider domain for the Go
// published-module consumer proof. It mirrors the crates.io Rust sample
// (CratesIo.RsDecider): the same events, states, queries, and materialized
// view, so the evidence is comparable across SDK languages.
package domain

// Tag groups and projector names as declared in the runtime manifest.
const (
	TagGroupWeather      = "weather"
	WeatherTagProjector  = "WeatherForecastProjector"
	WeatherListProjector = "WeatherForecastMultiProjection"
)

// TagProjectorMap feeds client.NewSekibanRuntimeClient so tag-state reads
// resolve the projector declared in the manifest.
var TagProjectorMap = map[string]string{
	TagGroupWeather: WeatherTagProjector,
}

// Events (camelCase JSON, identical shape to the Rust sample payloads).

type WeatherForecastCreated struct {
	ForecastId   string `json:"forecastId"`
	Location     string `json:"location"`
	TemperatureC int32  `json:"temperatureC"`
	Summary      string `json:"summary"`
	CreatedAt    string `json:"createdAt"`
}

type WeatherForecastLocationUpdated struct {
	ForecastId  string `json:"forecastId"`
	NewLocation string `json:"newLocation"`
	UpdatedAt   string `json:"updatedAt"`
}

// States.

type WeatherForecastState struct {
	ForecastId   string `json:"forecastId"`
	Location     string `json:"location"`
	TemperatureC int32  `json:"temperatureC"`
	Summary      string `json:"summary"`
	CreatedAt    string `json:"createdAt"`
	UpdatedAt    string `json:"updatedAt,omitempty"`
}

type WeatherForecastItem struct {
	ForecastId   string `json:"forecastId"`
	Location     string `json:"location"`
	TemperatureC int32  `json:"temperatureC"`
	Summary      string `json:"summary"`
	CreatedAt    string `json:"createdAt"`
	UpdatedAt    string `json:"updatedAt,omitempty"`
}

type WeatherForecastListState struct {
	Items map[string]WeatherForecastItem `json:"items"`
}
