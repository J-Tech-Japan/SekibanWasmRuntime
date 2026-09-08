use std::{env, time::Duration};

use anyhow::{anyhow, Context, Result};
use sekiban_core::prelude::CommandContext;
use sekiban_executor::{RemoteSekibanExecutor, StaticTagProjectorResolver};
use sekiban_wasm_domain::{
    CreateWeatherForecast, GetWeatherForecastListQuery, WeatherForecastItem, WeatherForecastState,
    WeatherForecastTag,
};
use serde::Serialize;
use tokio::time::sleep;
use uuid::Uuid;

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct SmokeEvidence {
    forecast_id: Uuid,
    location: String,
    sortable_unique_id: Option<String>,
    tag_state_version: i32,
    tag_state_location: String,
    list_query_count: usize,
    found_in_list_query: bool,
}

#[tokio::main]
async fn main() -> Result<()> {
    let base_url = env::var("RUNTIME_URL").unwrap_or_else(|_| "http://localhost:8080".to_string());
    let location = env::var("SAMPLE_FORECAST_LOCATION").unwrap_or_else(|_| "Kyoto".to_string());
    let forecast_id = env::var("SAMPLE_FORECAST_ID")
        .ok()
        .map(|value| value.parse())
        .transpose()
        .context("SAMPLE_FORECAST_ID must be a UUID")?
        .unwrap_or_else(Uuid::now_v7);

    let executor = RemoteSekibanExecutor::new(
        base_url,
        StaticTagProjectorResolver::new().with_tag_group("weather", "WeatherForecastProjector"),
    );

    let command = CreateWeatherForecast {
        forecast_id: Some(forecast_id),
        location: location.clone(),
        temperature_c: 24,
        summary: "Public container Rust sample".to_string(),
    };
    let command_result = executor.execute_command(&command).await?;
    if !command_result.success {
        return Err(anyhow!(
            "command failed: {}",
            serde_json::to_string(&command_result.response_body)?
        ));
    }

    let tag = WeatherForecastTag::new(forecast_id);
    let (tag_state, tag_state_version): (WeatherForecastState, i32) =
        executor.command_context().get_state(&tag).await?;
    if tag_state.forecast_id != forecast_id || tag_state.location != location {
        return Err(anyhow!(
            "tag-state mismatch: expected {forecast_id}/{location}, got {:?}",
            tag_state
        ));
    }

    let mut list_items = Vec::<WeatherForecastItem>::new();
    for _ in 0..30 {
        list_items = executor
            .execute_list_query::<GetWeatherForecastListQuery, Vec<WeatherForecastItem>>(
                &GetWeatherForecastListQuery {
                    location_filter: Some(location.clone()),
                    wait_for_sortable_unique_id: command_result.sortable_unique_id.clone(),
                },
            )
            .await?;
        if list_items.iter().any(|item| item.forecast_id == forecast_id) {
            break;
        }
        sleep(Duration::from_secs(2)).await;
    }

    let found_in_list_query = list_items.iter().any(|item| item.forecast_id == forecast_id);
    if !found_in_list_query {
        return Err(anyhow!(
            "list-query did not return forecast {forecast_id}; count={}",
            list_items.len()
        ));
    }

    let evidence = SmokeEvidence {
        forecast_id,
        location,
        sortable_unique_id: command_result.sortable_unique_id,
        tag_state_version,
        tag_state_location: tag_state.location,
        list_query_count: list_items.len(),
        found_in_list_query,
    };
    println!("{}", serde_json::to_string_pretty(&evidence)?);
    Ok(())
}
