use sekiban_derive::State;
use serde::{Deserialize, Serialize};
use uuid::Uuid;

/// Weather forecast state (tag projector).
#[derive(Debug, Clone, Default, Serialize, Deserialize, State, PartialEq)]
#[serde(rename_all = "camelCase")]
#[state(empty_check = "forecast_id")]
pub struct WeatherForecastState {
    pub forecast_id: Uuid,
    pub location: String,
    pub date: String,
    pub temperature_c: i32,
    pub summary: String,
    pub created_at: String,
    pub is_deleted: bool,
    pub deleted_at: Option<String>,
}

impl WeatherForecastState {
    pub fn with_deleted(mut self, deleted: bool, deleted_at: Option<String>) -> Self {
        self.is_deleted = deleted;
        self.deleted_at = deleted_at;
        self
    }
}

/// Weather forecast item (multi projector list).
#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct WeatherForecastItem {
    pub forecast_id: Uuid,
    pub location: String,
    pub date: String,
    pub temperature_c: i32,
    pub summary: String,
}

/// Weather forecast list state (multi projector).
#[derive(Debug, Clone, Default, Serialize, Deserialize, State, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct WeatherForecastListState {
    pub items: Vec<WeatherForecastItem>,
}

/// Count result for queries.
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CountResult {
    pub count: i32,
}
