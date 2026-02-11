use sekiban_core::prelude::*;
use serde::{Deserialize, Serialize};

/// Location filter query params.
#[derive(Debug, Clone, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct LocationQuery {
    pub location_filter: Option<String>,
}

/// List weather forecasts query.
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

/// Count weather forecasts query.
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
