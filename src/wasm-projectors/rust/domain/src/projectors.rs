use sekiban_core::prelude::*;
use sekiban_derive::{MultiProjector, TagProjector};

use crate::events::*;
use crate::queries::LocationQuery;
use crate::states::*;

/// Weather forecast tag projector.
#[derive(TagProjector)]
#[projector(name = "WeatherForecastProjector", version = "1.0.0")]
pub struct WeatherForecastProjector;

impl Projector for WeatherForecastProjector {
    type State = WeatherForecastState;

    fn event_types() -> Vec<&'static str> {
        vec![
            WeatherForecastCreated::EVENT_TYPE,
            WeatherForecastLocationUpdated::EVENT_TYPE,
            WeatherForecastDeleted::EVENT_TYPE,
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
                is_deleted: false,
                deleted_at: None,
            },
            WeatherForecastLocationUpdated(e) => {
                let mut new_state = state;
                new_state.location = e.new_location.clone();
                new_state
            },
            WeatherForecastDeleted(e) => state.with_deleted(true, Some(e.deleted_at.clone())),
        })
    }
}

/// Weather forecast list multi projector.
#[derive(MultiProjector)]
#[projector(name = "WeatherForecastMultiProjection", version = "1.0.0")]
pub struct WeatherForecastListProjector;

impl Projector for WeatherForecastListProjector {
    type State = WeatherForecastListState;

    fn event_types() -> Vec<&'static str> {
        vec![
            WeatherForecastCreated::EVENT_TYPE,
            WeatherForecastLocationUpdated::EVENT_TYPE,
            WeatherForecastDeleted::EVENT_TYPE,
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
            WeatherForecastDeleted(e) => {
                let mut new_state = state;
                new_state.items = new_state
                    .items
                    .into_iter()
                    .filter(|item| item.forecast_id != e.forecast_id)
                    .collect();
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
        let count = if let Some(filter) = query.location_filter {
            state
                .items
                .iter()
                .filter(|item| item.location.contains(&filter))
                .count() as i32
        } else {
            state.items.len() as i32
        };
        Some(serde_json::to_string(&CountResult { count }).unwrap_or_else(|_| "{}".to_string()))
    }

    fn execute_list_query(state: &Self::State, query_type: &str, params: &str) -> Option<String> {
        if query_type != "GetWeatherForecastListQuery" && query_type != "WeatherForecastListQuery" {
            return None;
        }
        let query: LocationQuery = serde_json::from_str(params).unwrap_or_default();
        let items: Vec<WeatherForecastItem> = if let Some(filter) = query.location_filter {
            state
                .items
                .iter()
                .cloned()
                .filter(|item| item.location.contains(&filter))
                .collect()
        } else {
            state.items.clone()
        };
        Some(serde_json::to_string(&items).unwrap_or_else(|_| "[]".to_string()))
    }
}
