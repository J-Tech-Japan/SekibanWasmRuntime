use sekiban_derive::Tag;
use serde::{Deserialize, Serialize};
use uuid::Uuid;

/// Weather forecast tag.
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
