package domain

import (
	"encoding/json"
	"fmt"

	"github.com/J-Tech-Japan/SekibanWasmRuntime/src/lib/sekiban-go/mv"
)

// WeatherForecastLogicalTable is the logical table the WeatherForecast view
// binds; the smoke resolves the physical name through sekiban_mv_registry.
const (
	WeatherForecastLogicalTable = "weather_forecast"
	WeatherForecastView         = "WeatherForecast"
	WeatherForecastViewVersion  = int32(1)
)

// WeatherForecastMvV1 is the Go port of the Rust sample's projector of the
// same name: identical SQL and parameters so the registry rows and physical
// tables line up across SDK languages.
type WeatherForecastMvV1 struct{}

func (WeatherForecastMvV1) ViewName() string        { return WeatherForecastView }
func (WeatherForecastMvV1) ViewVersion() int32      { return WeatherForecastViewVersion }
func (WeatherForecastMvV1) LogicalTables() []string { return []string{WeatherForecastLogicalTable} }

func (WeatherForecastMvV1) Initialize(tables mv.MvTableBindingsDto) []mv.MvSqlStatementDto {
	table := tables.PhysicalName(WeatherForecastLogicalTable)
	return []mv.MvSqlStatementDto{{
		Parameters: []mv.MvParam{},
		Sql: fmt.Sprintf(`CREATE TABLE IF NOT EXISTS %s (
forecast_id UUID PRIMARY KEY,
location TEXT NOT NULL,
temperature_c INT NOT NULL,
summary TEXT NOT NULL,
created_at TEXT NOT NULL,
updated_at TEXT NULL,
_last_sortable_unique_id TEXT NOT NULL,
_last_applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);`, table),
	}}
}

func (p WeatherForecastMvV1) ApplyEvent(
	tables mv.MvTableBindingsDto,
	event mv.MvSerializableEventDto,
	_ mv.QueryPort,
) []mv.MvSqlStatementDto {
	switch event.EventType {
	case "WeatherForecastCreated":
		var created WeatherForecastCreated
		if err := json.Unmarshal([]byte(event.PayloadJSON), &created); err != nil {
			return nil
		}
		return []mv.MvSqlStatementDto{p.insertForecast(tables, created, event.SortableUniqueId)}
	case "WeatherForecastLocationUpdated":
		var updated WeatherForecastLocationUpdated
		if err := json.Unmarshal([]byte(event.PayloadJSON), &updated); err != nil {
			return nil
		}
		return []mv.MvSqlStatementDto{p.updateLocation(tables, updated, event.SortableUniqueId)}
	default:
		return nil
	}
}

func (WeatherForecastMvV1) insertForecast(
	tables mv.MvTableBindingsDto,
	created WeatherForecastCreated,
	sortableUniqueId string,
) mv.MvSqlStatementDto {
	table := tables.PhysicalName(WeatherForecastLogicalTable)
	return mv.MvSqlStatementDto{
		Sql: fmt.Sprintf(`INSERT INTO %s
(forecast_id, location, temperature_c, summary, created_at, updated_at, _last_sortable_unique_id, _last_applied_at)
VALUES (@ForecastId, @Location, @TemperatureC, @Summary, @CreatedAt, NULL, @SortableUniqueId, NOW())
ON CONFLICT (forecast_id) DO UPDATE SET
location = EXCLUDED.location,
temperature_c = EXCLUDED.temperature_c,
summary = EXCLUDED.summary,
created_at = EXCLUDED.created_at,
updated_at = NULL,
_last_sortable_unique_id = EXCLUDED._last_sortable_unique_id,
_last_applied_at = EXCLUDED._last_applied_at
WHERE %s._last_sortable_unique_id < EXCLUDED._last_sortable_unique_id;`, table, table),
		Parameters: mv.NewParams().
			Guid("ForecastId", created.ForecastId).
			String("Location", created.Location).
			Int32("TemperatureC", created.TemperatureC).
			String("Summary", created.Summary).
			String("CreatedAt", created.CreatedAt).
			String("SortableUniqueId", sortableUniqueId).
			Build(),
	}
}

func (WeatherForecastMvV1) updateLocation(
	tables mv.MvTableBindingsDto,
	updated WeatherForecastLocationUpdated,
	sortableUniqueId string,
) mv.MvSqlStatementDto {
	table := tables.PhysicalName(WeatherForecastLogicalTable)
	return mv.MvSqlStatementDto{
		Sql: fmt.Sprintf(`UPDATE %s
SET location = @Location,
updated_at = @UpdatedAt,
_last_sortable_unique_id = @SortableUniqueId,
_last_applied_at = NOW()
WHERE forecast_id = @ForecastId
AND _last_sortable_unique_id < @SortableUniqueId;`, table),
		Parameters: mv.NewParams().
			Guid("ForecastId", updated.ForecastId).
			String("Location", updated.NewLocation).
			String("UpdatedAt", updated.UpdatedAt).
			String("SortableUniqueId", sortableUniqueId).
			Build(),
	}
}
