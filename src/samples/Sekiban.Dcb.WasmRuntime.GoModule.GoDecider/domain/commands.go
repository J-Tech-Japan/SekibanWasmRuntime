package domain

import (
	"encoding/json"
	"fmt"
	"time"

	sekiban "github.com/J-Tech-Japan/SekibanWasmRuntime/src/lib/sekiban-go/domain"
)

// CreateWeatherForecast creates a forecast; fails if the tag already has state.
type CreateWeatherForecast struct {
	ForecastId   string
	Location     string
	TemperatureC int32
	Summary      string
}

func (CreateWeatherForecast) CommandType() string { return "CreateWeatherForecast" }

func (c CreateWeatherForecast) Handle(ctx sekiban.CommandContext) (*sekiban.CommandOutput, error) {
	state, err := ctx.GetTagState(TagGroupWeather, c.ForecastId)
	if err != nil {
		return nil, err
	}
	if !sekiban.IsEmptyJSON(state.StateJson) {
		return nil, fmt.Errorf("weather forecast %s: %w", c.ForecastId, sekiban.ErrAlreadyExists)
	}

	tag := sekiban.TagString(TagGroupWeather, c.ForecastId)
	output, err := sekiban.NewCommandOutput(
		"WeatherForecastCreated",
		WeatherForecastCreated{
			ForecastId:   c.ForecastId,
			Location:     c.Location,
			TemperatureC: c.TemperatureC,
			Summary:      c.Summary,
			CreatedAt:    time.Now().UTC().Format(time.RFC3339),
		},
		[]string{tag},
		[]string{tag},
		map[string]int{tag: state.Version},
	)
	if err != nil {
		return nil, err
	}
	return &output, nil
}

// UpdateWeatherForecastLocation moves an existing forecast to a new location.
type UpdateWeatherForecastLocation struct {
	ForecastId  string
	NewLocation string
}

func (UpdateWeatherForecastLocation) CommandType() string { return "UpdateWeatherForecastLocation" }

func (c UpdateWeatherForecastLocation) Handle(ctx sekiban.CommandContext) (*sekiban.CommandOutput, error) {
	state, err := ctx.GetTagState(TagGroupWeather, c.ForecastId)
	if err != nil {
		return nil, err
	}
	if sekiban.IsEmptyJSON(state.StateJson) {
		return nil, fmt.Errorf("weather forecast %s: %w", c.ForecastId, sekiban.ErrNotFound)
	}
	var current WeatherForecastState
	if err := json.Unmarshal([]byte(state.StateJson), &current); err != nil {
		return nil, fmt.Errorf("decode weather forecast state: %w", err)
	}

	tag := sekiban.TagString(TagGroupWeather, c.ForecastId)
	output, err := sekiban.NewCommandOutput(
		"WeatherForecastLocationUpdated",
		WeatherForecastLocationUpdated{
			ForecastId:  c.ForecastId,
			NewLocation: c.NewLocation,
			UpdatedAt:   time.Now().UTC().Format(time.RFC3339),
		},
		[]string{tag},
		[]string{tag},
		map[string]int{tag: state.Version},
	)
	if err != nil {
		return nil, err
	}
	return &output, nil
}
