use async_trait::async_trait;
use chrono::Utc;
use sekiban_core::prelude::*;
use sekiban_derive::{Command, Event, MultiProjector, State, Tag, TagProjector};
use sekiban_mv::{
    dto::{MvSerializableEventDto, MvSqlStatementDto, MvTableBindingsDto},
    param_builder::MvParamBuilder,
    projector::WasmMvProjector,
    query_port::MvQueryPort,
};
use serde::{Deserialize, Serialize};
use uuid::Uuid;

domain_types!(WeatherForecastDomain {
    events: [WeatherForecastCreated, WeatherForecastLocationUpdated],
    tags: [WeatherForecastTag,],
    tag_projectors: [WeatherForecastProjector,],
    multi_projectors: [WeatherForecastListProjector,],
    commands: [CreateWeatherForecast, UpdateWeatherForecastLocation],
    queries: [GetWeatherForecastCountQuery,],
    list_queries: [GetWeatherForecastListQuery,],
});

#[derive(Debug, Clone, Serialize, Deserialize, Event)]
#[serde(rename_all = "camelCase")]
pub struct WeatherForecastCreated {
    pub forecast_id: Uuid,
    pub location: String,
    pub temperature_c: i32,
    pub summary: String,
    pub created_at: String,
}

#[derive(Debug, Clone, Serialize, Deserialize, Event)]
#[serde(rename_all = "camelCase")]
pub struct WeatherForecastLocationUpdated {
    pub forecast_id: Uuid,
    pub new_location: String,
    pub updated_at: String,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize, State, PartialEq)]
#[serde(rename_all = "camelCase")]
#[state(empty_check = "forecast_id")]
pub struct WeatherForecastState {
    pub forecast_id: Uuid,
    pub location: String,
    pub temperature_c: i32,
    pub summary: String,
    pub created_at: String,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct WeatherForecastItem {
    pub forecast_id: Uuid,
    pub location: String,
    pub temperature_c: i32,
    pub summary: String,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize, State, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct WeatherForecastListState {
    pub items: Vec<WeatherForecastItem>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CountResult {
    pub count: i32,
}

#[derive(Debug, Clone, Serialize, Deserialize, Tag)]
#[tag(group = "weather")]
pub struct WeatherForecastTag {
    pub forecast_id: Uuid,
}

impl WeatherForecastTag {
    pub fn new(forecast_id: Uuid) -> Self {
        Self { forecast_id }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct LocationQuery {
    pub location_filter: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct GetWeatherForecastListQuery {
    pub location_filter: Option<String>,
    pub wait_for_sortable_unique_id: Option<String>,
}

impl ListQuery for GetWeatherForecastListQuery {
    const QUERY_TYPE: &'static str = "GetWeatherForecastListQuery";

    fn wait_for_sortable_id(&self) -> Option<&str> {
        self.wait_for_sortable_unique_id.as_deref()
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct GetWeatherForecastCountQuery {
    pub location_filter: Option<String>,
    pub wait_for_sortable_unique_id: Option<String>,
}

impl Query for GetWeatherForecastCountQuery {
    const QUERY_TYPE: &'static str = "GetWeatherForecastCountQuery";

    fn wait_for_sortable_id(&self) -> Option<&str> {
        self.wait_for_sortable_unique_id.as_deref()
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, Command)]
#[serde(rename_all = "camelCase")]
pub struct CreateWeatherForecast {
    pub forecast_id: Option<Uuid>,
    pub location: String,
    pub temperature_c: i32,
    pub summary: String,
}

#[async_trait]
impl CommandHandler for CreateWeatherForecast {
    async fn handle<C: CommandContext + ?Sized>(
        &self,
        ctx: &C,
    ) -> Result<Option<CommandOutput>, CommandError> {
        let forecast_id = self.forecast_id.unwrap_or_else(Uuid::now_v7);
        let tag = WeatherForecastTag::new(forecast_id);
        let (state, _version): (WeatherForecastState, i32) = ctx.get_state(&tag).await?;
        if !state.is_empty() {
            return Err(CommandError::AlreadyExists(forecast_id.to_string()));
        }

        let event = WeatherForecastCreated {
            forecast_id,
            location: self.location.clone(),
            temperature_c: self.temperature_c,
            summary: self.summary.clone(),
            created_at: Utc::now().to_rfc3339(),
        };

        Ok(Some(CommandOutput::single(event, tag).map_err(|err| {
            CommandError::Serialization(err.to_string())
        })?))
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, Command)]
#[serde(rename_all = "camelCase")]
pub struct UpdateWeatherForecastLocation {
    pub forecast_id: Uuid,
    pub new_location: String,
}

#[async_trait]
impl CommandHandler for UpdateWeatherForecastLocation {
    async fn handle<C: CommandContext + ?Sized>(
        &self,
        ctx: &C,
    ) -> Result<Option<CommandOutput>, CommandError> {
        let tag = WeatherForecastTag::new(self.forecast_id);
        let (state, version): (WeatherForecastState, i32) = ctx.get_state(&tag).await?;

        if state.is_empty() {
            return Err(CommandError::NotFound(self.forecast_id.to_string()));
        }

        let event = WeatherForecastLocationUpdated {
            forecast_id: self.forecast_id,
            new_location: self.new_location.clone(),
            updated_at: Utc::now().to_rfc3339(),
        };

        let output = CommandOutput::single(event, tag.clone())
            .map_err(|err| CommandError::Serialization(err.to_string()))?
            .with_expected_version(tag, version);
        Ok(Some(output))
    }
}

#[derive(TagProjector)]
#[projector(name = "WeatherForecastProjector", version = "1.0.0")]
pub struct WeatherForecastProjector;

impl Projector for WeatherForecastProjector {
    type State = WeatherForecastState;

    fn event_types() -> Vec<&'static str> {
        vec![
            WeatherForecastCreated::EVENT_TYPE,
            WeatherForecastLocationUpdated::EVENT_TYPE,
        ]
    }

    fn project(state: Self::State, event: &Event) -> Self::State {
        match_event!(event, state, {
            WeatherForecastCreated(e) => WeatherForecastState {
                forecast_id: e.forecast_id,
                location: e.location.clone(),
                temperature_c: e.temperature_c,
                summary: e.summary.clone(),
                created_at: e.created_at.clone(),
            },
            WeatherForecastLocationUpdated(e) => {
                let mut new_state = state;
                new_state.location = e.new_location.clone();
                new_state
            },
        })
    }
}

#[derive(MultiProjector)]
#[projector(name = "WeatherForecastMultiProjection", version = "1.0.0")]
pub struct WeatherForecastListProjector;

impl Projector for WeatherForecastListProjector {
    type State = WeatherForecastListState;

    fn event_types() -> Vec<&'static str> {
        vec![
            WeatherForecastCreated::EVENT_TYPE,
            WeatherForecastLocationUpdated::EVENT_TYPE,
        ]
    }

    fn project(state: Self::State, event: &Event) -> Self::State {
        match_event!(event, state, {
            WeatherForecastCreated(e) => {
                let mut new_state = state;
                new_state.items.push(WeatherForecastItem {
                    forecast_id: e.forecast_id,
                    location: e.location.clone(),
                    temperature_c: e.temperature_c,
                    summary: e.summary.clone(),
                });
                new_state
            },
            WeatherForecastLocationUpdated(e) => {
                let mut new_state = state;
                if let Some(item) = new_state.items.iter_mut().find(|item| item.forecast_id == e.forecast_id) {
                    item.location = e.new_location.clone();
                }
                new_state
            },
        })
    }
}

impl MultiProjectorQuery for WeatherForecastListProjector {
    fn execute_query(state: &Self::State, query_type: &str, params: &str) -> Option<String> {
        if query_type != "GetWeatherForecastCountQuery" {
            return None;
        }
        let query: LocationQuery = serde_json::from_str(params).unwrap_or_default();
        let count = filter_items(&state.items, query.location_filter.as_deref()).len() as i32;
        Some(serde_json::to_string(&CountResult { count }).unwrap_or_else(|_| "{}".to_string()))
    }

    fn execute_list_query(state: &Self::State, query_type: &str, params: &str) -> Option<String> {
        if query_type != "GetWeatherForecastListQuery" {
            return None;
        }
        let query: LocationQuery = serde_json::from_str(params).unwrap_or_default();
        let items = filter_items(&state.items, query.location_filter.as_deref());
        Some(serde_json::to_string(&items).unwrap_or_else(|_| "[]".to_string()))
    }
}

fn filter_items(
    items: &[WeatherForecastItem],
    location_filter: Option<&str>,
) -> Vec<WeatherForecastItem> {
    items
        .iter()
        .filter(|item| {
            location_filter
                .map(|filter| item.location.contains(filter))
                .unwrap_or(true)
        })
        .cloned()
        .collect()
}

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
        vec![MvSqlStatementDto {
            sql: format!(
                "CREATE TABLE IF NOT EXISTS {table} (\n\
                 forecast_id UUID PRIMARY KEY,\n\
                 location TEXT NOT NULL,\n\
                 temperature_c INT NOT NULL,\n\
                 summary TEXT NOT NULL,\n\
                 created_at TEXT NOT NULL,\n\
                 updated_at TEXT NULL,\n\
                 _last_sortable_unique_id TEXT NOT NULL,\n\
                 _last_applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()\n\
                 );"
            ),
            parameters: vec![],
        }]
    }

    fn apply_event(
        &self,
        tables: &MvTableBindingsDto,
        event: &MvSerializableEventDto,
        _query_port: &dyn MvQueryPort,
    ) -> Vec<MvSqlStatementDto> {
        match event.event_type.as_str() {
            "WeatherForecastCreated" => {
                serde_json::from_str::<WeatherForecastCreated>(&event.payload_json)
                    .ok()
                    .map(|created| {
                        vec![insert_forecast(tables, &created, &event.sortable_unique_id)]
                    })
                    .unwrap_or_default()
            }
            "WeatherForecastLocationUpdated" => serde_json::from_str::<
                WeatherForecastLocationUpdated,
            >(&event.payload_json)
            .ok()
            .map(|updated| vec![update_location(tables, &updated, &event.sortable_unique_id)])
            .unwrap_or_default(),
            _ => vec![],
        }
    }
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
             (forecast_id, location, temperature_c, summary, created_at, updated_at, _last_sortable_unique_id, _last_applied_at)\n\
             VALUES (@ForecastId, @Location, @TemperatureC, @Summary, @CreatedAt, NULL, @SortableUniqueId, NOW())\n\
             ON CONFLICT (forecast_id) DO UPDATE SET\n\
             location = EXCLUDED.location,\n\
             temperature_c = EXCLUDED.temperature_c,\n\
             summary = EXCLUDED.summary,\n\
             created_at = EXCLUDED.created_at,\n\
             updated_at = NULL,\n\
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
