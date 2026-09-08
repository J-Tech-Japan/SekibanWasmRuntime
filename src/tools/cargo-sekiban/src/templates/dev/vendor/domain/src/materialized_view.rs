use sekiban_mv::{
    dto::{MvSerializableEventDto, MvSqlStatementDto, MvTableBindingsDto},
    param_builder::MvParamBuilder,
    projector::WasmMvProjector,
    query_port::MvQueryPort,
};

use crate::events::{
    WeatherForecastCreated, WeatherForecastDeleted, WeatherForecastLocationUpdated,
};

pub const WEATHER_FORECAST_LOGICAL: &str = "weather_forecast";

pub struct WeatherForecastMvV1;

impl WasmMvProjector for WeatherForecastMvV1 {
    fn view_name(&self) -> &'static str {
        "WeatherForecast"
    }

    fn view_version(&self) -> i32 {
        1
    }

    fn logical_tables(&self) -> &'static [&'static str] {
        &[WEATHER_FORECAST_LOGICAL]
    }

    fn initialize(&self, tables: &MvTableBindingsDto) -> Vec<MvSqlStatementDto> {
        let table = tables.get_physical_name(WEATHER_FORECAST_LOGICAL);
        vec![
            stmt(format!(
                "CREATE TABLE IF NOT EXISTS {table} (\n\
                 forecast_id UUID PRIMARY KEY,\n\
                 location TEXT NOT NULL,\n\
                 temperature_c INT NOT NULL,\n\
                 summary TEXT NOT NULL,\n\
                 is_deleted BOOLEAN NOT NULL DEFAULT FALSE,\n\
                 created_at TEXT NOT NULL,\n\
                 updated_at TEXT NULL,\n\
                 deleted_at TEXT NULL,\n\
                 _last_sortable_unique_id TEXT NOT NULL,\n\
                 _last_applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()\n\
                 );"
            )),
        ]
    }

    fn apply_event(
        &self,
        tables: &MvTableBindingsDto,
        event: &MvSerializableEventDto,
        _query_port: &dyn MvQueryPort,
    ) -> Vec<MvSqlStatementDto> {
        match event.event_type.as_str() {
            "WeatherForecastCreated" => {
                let Ok(created) = serde_json::from_str::<WeatherForecastCreated>(&event.payload_json) else {
                    return vec![];
                };
                vec![insert_forecast(tables, &created, &event.sortable_unique_id)]
            }
            "WeatherForecastLocationUpdated" => {
                let Ok(updated) =
                    serde_json::from_str::<WeatherForecastLocationUpdated>(&event.payload_json)
                else {
                    return vec![];
                };
                vec![update_location(tables, &updated, &event.sortable_unique_id)]
            }
            "WeatherForecastDeleted" => {
                let Ok(deleted) = serde_json::from_str::<WeatherForecastDeleted>(&event.payload_json) else {
                    return vec![];
                };
                vec![delete_forecast(tables, &deleted, &event.sortable_unique_id)]
            }
            _ => vec![],
        }
    }
}

fn stmt(sql: String) -> MvSqlStatementDto {
    MvSqlStatementDto { sql, parameters: vec![] }
}

fn insert_forecast(
    tables: &MvTableBindingsDto,
    created: &WeatherForecastCreated,
    sortable_unique_id: &str,
) -> MvSqlStatementDto {
    let table = tables.get_physical_name(WEATHER_FORECAST_LOGICAL);
    MvSqlStatementDto {
        sql: format!(
            "INSERT INTO {table}\n\
             (forecast_id, location, temperature_c, summary, is_deleted, created_at, updated_at, deleted_at, _last_sortable_unique_id, _last_applied_at)\n\
             VALUES (@ForecastId, @Location, @TemperatureC, @Summary, FALSE, @CreatedAt, NULL, NULL, @SortableUniqueId, NOW())\n\
             ON CONFLICT (forecast_id) DO UPDATE SET\n\
             location = EXCLUDED.location,\n\
             temperature_c = EXCLUDED.temperature_c,\n\
             summary = EXCLUDED.summary,\n\
             is_deleted = FALSE,\n\
             created_at = EXCLUDED.created_at,\n\
             updated_at = NULL,\n\
             deleted_at = NULL,\n\
             _last_sortable_unique_id = EXCLUDED._last_sortable_unique_id,\n\
             _last_applied_at = EXCLUDED._last_applied_at\n\
             WHERE {table}._last_sortable_unique_id < EXCLUDED._last_sortable_unique_id;"
        ),
        parameters: MvParamBuilder::new()
            .guid("ForecastId", created.forecast_id)
            .string("Location", created.location.clone())
            .int32("TemperatureC", created.temperature_c)
            .string("Summary", created.summary.clone())
            .string("CreatedAt", created.created_at.clone())
            .string("SortableUniqueId", sortable_unique_id)
            .build(),
    }
}

fn update_location(
    tables: &MvTableBindingsDto,
    updated: &WeatherForecastLocationUpdated,
    sortable_unique_id: &str,
) -> MvSqlStatementDto {
    let table = tables.get_physical_name(WEATHER_FORECAST_LOGICAL);
    MvSqlStatementDto {
        sql: format!(
            "UPDATE {table}\n\
             SET location = @Location,\n\
             updated_at = @UpdatedAt,\n\
             _last_sortable_unique_id = @SortableUniqueId,\n\
             _last_applied_at = NOW()\n\
             WHERE forecast_id = @ForecastId\n\
             AND _last_sortable_unique_id < @SortableUniqueId;"
        ),
        parameters: MvParamBuilder::new()
            .guid("ForecastId", updated.forecast_id)
            .string("Location", updated.new_location.clone())
            .string("UpdatedAt", updated.updated_at.clone())
            .string("SortableUniqueId", sortable_unique_id)
            .build(),
    }
}

fn delete_forecast(
    tables: &MvTableBindingsDto,
    deleted: &WeatherForecastDeleted,
    sortable_unique_id: &str,
) -> MvSqlStatementDto {
    let table = tables.get_physical_name(WEATHER_FORECAST_LOGICAL);
    MvSqlStatementDto {
        sql: format!(
            "UPDATE {table}\n\
             SET is_deleted = TRUE,\n\
             deleted_at = @DeletedAt,\n\
             _last_sortable_unique_id = @SortableUniqueId,\n\
             _last_applied_at = NOW()\n\
             WHERE forecast_id = @ForecastId\n\
             AND _last_sortable_unique_id < @SortableUniqueId;"
        ),
        parameters: MvParamBuilder::new()
            .guid("ForecastId", deleted.forecast_id)
            .string("DeletedAt", deleted.deleted_at.clone())
            .string("SortableUniqueId", sortable_unique_id)
            .build(),
    }
}
