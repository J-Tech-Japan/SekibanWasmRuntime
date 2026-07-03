import Foundation
import SekibanMv

// Swift port of the WeatherForecast materialized-view projector shipped by the
// crates.io Rust and Go published-module samples: identical SQL and parameters
// so the registry rows and physical tables line up across SDK languages.
struct WeatherForecastMvV1: WasmMvProjector {
    static let weatherForecastLogicalTable = "weather_forecast"

    var viewName: String { "WeatherForecast" }
    var viewVersion: Int32 { 1 }
    var logicalTables: [String] { [Self.weatherForecastLogicalTable] }

    func initialize(tables: MvTableBindingsDto) -> [MvSqlStatementDto] {
        let table = tables.getPhysicalName(Self.weatherForecastLogicalTable)
        return [MvSqlStatementDto(sql: """
        CREATE TABLE IF NOT EXISTS \(table) (
        forecast_id UUID PRIMARY KEY,
        location TEXT NOT NULL,
        temperature_c INT NOT NULL,
        summary TEXT NOT NULL,
        created_at TEXT NOT NULL,
        updated_at TEXT NULL,
        _last_sortable_unique_id TEXT NOT NULL,
        _last_applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );
        """)]
    }

    func applyEvent(
        tables: MvTableBindingsDto,
        event: MvSerializableEventDto,
        queryPort: MvQueryPort
    ) -> [MvSqlStatementDto] {
        _ = queryPort
        guard let data = event.payloadJson.data(using: .utf8) else { return [] }
        switch event.eventType {
        case "WeatherForecastCreated":
            guard let created = try? JSONDecoder().decode(WeatherForecastCreated.self, from: data),
                  let forecastId = UUID(uuidString: created.forecastId)
            else { return [] }
            return [insertForecast(tables: tables, created: created, forecastId: forecastId,
                                   sortableUniqueId: event.sortableUniqueId)]
        case "WeatherForecastLocationUpdated":
            guard let updated = try? JSONDecoder().decode(WeatherForecastLocationUpdated.self, from: data),
                  let forecastId = UUID(uuidString: updated.forecastId)
            else { return [] }
            return [updateLocation(tables: tables, updated: updated, forecastId: forecastId,
                                   sortableUniqueId: event.sortableUniqueId)]
        default:
            return []
        }
    }

    private func insertForecast(
        tables: MvTableBindingsDto,
        created: WeatherForecastCreated,
        forecastId: UUID,
        sortableUniqueId: String
    ) -> MvSqlStatementDto {
        let table = tables.getPhysicalName(Self.weatherForecastLogicalTable)
        return MvSqlStatementDto(
            sql: """
            INSERT INTO \(table)
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
            WHERE \(table)._last_sortable_unique_id < EXCLUDED._last_sortable_unique_id;
            """,
            parameters: MvParamBuilder()
                .guid("ForecastId", forecastId)
                .string("Location", created.location)
                .int32("TemperatureC", created.temperatureC)
                .string("Summary", created.summary)
                .string("CreatedAt", created.createdAt)
                .string("SortableUniqueId", sortableUniqueId)
                .build())
    }

    private func updateLocation(
        tables: MvTableBindingsDto,
        updated: WeatherForecastLocationUpdated,
        forecastId: UUID,
        sortableUniqueId: String
    ) -> MvSqlStatementDto {
        let table = tables.getPhysicalName(Self.weatherForecastLogicalTable)
        return MvSqlStatementDto(
            sql: """
            UPDATE \(table)
            SET location = @Location,
            updated_at = @UpdatedAt,
            _last_sortable_unique_id = @SortableUniqueId,
            _last_applied_at = NOW()
            WHERE forecast_id = @ForecastId
            AND _last_sortable_unique_id < @SortableUniqueId;
            """,
            parameters: MvParamBuilder()
                .guid("ForecastId", forecastId)
                .string("Location", updated.newLocation)
                .string("UpdatedAt", updated.updatedAt)
                .string("SortableUniqueId", sortableUniqueId)
                .build())
    }
}
