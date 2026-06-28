use std::{env, time::Duration};

use anyhow::{anyhow, Context, Result};
use crates_io_rs_decider_domain::{
    CreateWeatherForecast, GetWeatherForecastCountQuery, GetWeatherForecastListQuery,
    UpdateWeatherForecastLocation, WeatherForecastItem, WeatherForecastState, WeatherForecastTag,
};
use sekiban_core::prelude::CommandContext;
use sekiban_executor::{RemoteSekibanExecutor, StaticTagProjectorResolver};
use serde::Serialize;
use tokio::time::sleep;
use uuid::Uuid;

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct SmokeEvidence {
    forecast_id: Uuid,
    original_location: String,
    updated_location: String,
    sortable_unique_id: Option<String>,
    tag_state_version: i32,
    tag_state_location: String,
    list_query_count: usize,
    count_query_count: i32,
    found_in_list_query: bool,
}

#[tokio::main]
async fn main() -> Result<()> {
    let base_url = env::var("RUNTIME_URL").unwrap_or_else(|_| "http://localhost:8080".to_string());
    let original_location =
        env::var("SAMPLE_FORECAST_LOCATION").unwrap_or_else(|_| "Kyoto".to_string());
    let updated_location =
        env::var("SAMPLE_UPDATED_LOCATION").unwrap_or_else(|_| "Osaka".to_string());
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

    let created = executor
        .execute_command(&CreateWeatherForecast {
            forecast_id: Some(forecast_id),
            location: original_location.clone(),
            temperature_c: 24,
            summary: "crates.io Rust sample".to_string(),
        })
        .await?;
    ensure_success("CreateWeatherForecast", &created)?;

    let updated = executor
        .execute_command(&UpdateWeatherForecastLocation {
            forecast_id,
            new_location: updated_location.clone(),
        })
        .await?;
    ensure_success("UpdateWeatherForecastLocation", &updated)?;

    let tag = WeatherForecastTag::new(forecast_id);
    let (tag_state, tag_state_version): (WeatherForecastState, i32) =
        executor.command_context().get_state(&tag).await?;
    if tag_state.forecast_id != forecast_id || tag_state.location != updated_location {
        return Err(anyhow!(
            "tag-state mismatch: expected {forecast_id}/{updated_location}, got {:?}",
            tag_state
        ));
    }

    let wait_for = updated
        .sortable_unique_id
        .clone()
        .or_else(|| created.sortable_unique_id.clone());
    let mut list_items = Vec::<WeatherForecastItem>::new();
    for _ in 0..30 {
        list_items = executor
            .execute_list_query::<GetWeatherForecastListQuery, Vec<WeatherForecastItem>>(
                &GetWeatherForecastListQuery {
                    location_filter: Some(updated_location.clone()),
                    wait_for_sortable_unique_id: wait_for.clone(),
                },
            )
            .await?;
        if list_items
            .iter()
            .any(|item| item.forecast_id == forecast_id && item.location == updated_location)
        {
            break;
        }
        sleep(Duration::from_secs(2)).await;
    }

    let count = executor
        .execute_query::<GetWeatherForecastCountQuery, crates_io_rs_decider_domain::CountResult>(
            &GetWeatherForecastCountQuery {
                location_filter: Some(updated_location.clone()),
                wait_for_sortable_unique_id: wait_for.clone(),
            },
        )
        .await?;

    let found_in_list_query = list_items
        .iter()
        .any(|item| item.forecast_id == forecast_id && item.location == updated_location);
    if !found_in_list_query {
        return Err(anyhow!(
            "list-query did not return forecast {forecast_id}; count={}",
            list_items.len()
        ));
    }

    let evidence = SmokeEvidence {
        forecast_id,
        original_location,
        updated_location,
        sortable_unique_id: wait_for,
        tag_state_version,
        tag_state_location: tag_state.location,
        list_query_count: list_items.len(),
        count_query_count: count.count,
        found_in_list_query,
    };
    println!("{}", serde_json::to_string_pretty(&evidence)?);
    Ok(())
}

fn ensure_success(name: &str, result: &sekiban_executor::CommandExecutionResult) -> Result<()> {
    if result.success {
        return Ok(());
    }

    Err(anyhow!(
        "{name} failed: {}",
        serde_json::to_string(&result.response_body)?
    ))
}
