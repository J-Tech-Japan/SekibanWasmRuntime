use sekiban_derive::Event;
use serde::{Deserialize, Serialize};
use uuid::Uuid;

/// Weather forecast created.
#[derive(Debug, Clone, Serialize, Deserialize, Event)]
#[serde(rename_all = "camelCase")]
pub struct WeatherForecastCreated {
    pub forecast_id: Uuid,
    pub location: String,
    pub temperature_c: i32,
    pub summary: String,
    pub created_at: String,
}

/// Weather forecast location updated.
#[derive(Debug, Clone, Serialize, Deserialize, Event)]
#[serde(rename_all = "camelCase")]
pub struct WeatherForecastLocationUpdated {
    pub forecast_id: Uuid,
    pub new_location: String,
    pub updated_at: String,
}

/// Weather forecast deleted.
#[derive(Debug, Clone, Serialize, Deserialize, Event)]
#[serde(rename_all = "camelCase")]
pub struct WeatherForecastDeleted {
    pub forecast_id: Uuid,
    pub deleted_at: String,
}
