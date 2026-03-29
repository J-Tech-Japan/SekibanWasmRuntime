use std::{env, net::SocketAddr, sync::Arc};

use anyhow::Result;
use axum::{
    extract::{Query as AxumQuery, State},
    http::StatusCode,
    response::IntoResponse,
    routing::{get, post},
    Json, Router,
};
use sekiban_core::prelude::*;
use sekiban_executor::{ExecuteCommandError, RemoteSekibanExecutor, StaticTagProjectorResolver};
use sekiban_wasm_domain::{
    CountResult, CreateWeatherForecast, DeleteWeatherForecast, GetWeatherForecastCountQuery,
    GetWeatherForecastListQuery, UpdateWeatherForecastLocation, WeatherForecastItem,
};
use serde::{Deserialize, Serialize};
use serde_json::json;

#[derive(Clone)]
struct AppState {
    executor: Arc<RemoteSekibanExecutor>,
}

#[tokio::main]
async fn main() -> Result<()> {
    init_tracing();

    let port = env::var("PORT")
        .ok()
        .and_then(|v| v.parse::<u16>().ok())
        .unwrap_or(8080);

    let wasmserver_base = resolve_wasmserver_base();
    tracing::info!(%wasmserver_base, "resolved wasmserver base");

    let executor = Arc::new(RemoteSekibanExecutor::new(
        wasmserver_base,
        StaticTagProjectorResolver::new().with_tag_group("weather", "WeatherForecastProjector"),
    ));

    let app = Router::new()
        .route("/health", get(health))
        .route(
            "/api/weatherforecast",
            get(get_forecasts).post(create_forecast),
        )
        .route("/api/weatherforecast/count", get(get_forecast_count))
        .route("/api/weatherforecast/delete", post(delete_forecast))
        .route(
            "/api/weatherforecast/update-location",
            post(update_location),
        )
        .with_state(AppState { executor });

    let addr = SocketAddr::from(([0, 0, 0, 0], port));
    tracing::info!(%addr, "Rust ClientApi listening");

    let listener = tokio::net::TcpListener::bind(addr).await?;
    axum::serve(listener, app).await?;
    Ok(())
}

async fn health() -> impl IntoResponse {
    Json(json!({ "message": "WeatherForecast API is running" }))
}

#[derive(Debug, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
struct WeatherListQueryParams {
    wait_for_sortable_id: Option<String>,
    location: Option<String>,
    #[serde(rename = "locationFilter")]
    location_filter: Option<String>,
}

async fn get_forecasts(
    State(state): State<AppState>,
    AxumQuery(query): AxumQuery<WeatherListQueryParams>,
) -> impl IntoResponse {
    let list_query = GetWeatherForecastListQuery {
        location_filter: query.location_filter.or(query.location),
        wait_for_sortable_unique_id: query.wait_for_sortable_id,
    };

    match state
        .executor
        .execute_list_query::<_, Vec<WeatherForecastItem>>(&list_query)
        .await
    {
        Ok(items) => (StatusCode::OK, Json(items)).into_response(),
        Err(err) => {
            tracing::warn!(error = %err, "list query failed");
            (
                StatusCode::BAD_GATEWAY,
                Json(json!({ "error": err.to_string() })),
            )
                .into_response()
        }
    }
}

async fn get_forecast_count(
    State(state): State<AppState>,
    AxumQuery(query): AxumQuery<WeatherListQueryParams>,
) -> impl IntoResponse {
    let count_query = GetWeatherForecastCountQuery {
        location_filter: query.location_filter.or(query.location),
        wait_for_sortable_unique_id: query.wait_for_sortable_id,
    };

    match state
        .executor
        .execute_query::<_, CountResult>(&count_query)
        .await
    {
        Ok(result) => (StatusCode::OK, Json(result)).into_response(),
        Err(err) => {
            tracing::warn!(error = %err, "count query failed");
            (
                StatusCode::BAD_GATEWAY,
                Json(json!({ "error": err.to_string() })),
            )
                .into_response()
        }
    }
}

async fn create_forecast(
    State(state): State<AppState>,
    Json(command): Json<CreateWeatherForecast>,
) -> impl IntoResponse {
    let forecast_id = command.forecast_id.map(|id| id.to_string());
    execute_command(&state, forecast_id, &command).await
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct DeleteRequest {
    forecast_id: String,
}

async fn delete_forecast(
    State(state): State<AppState>,
    Json(body): Json<DeleteRequest>,
) -> impl IntoResponse {
    let forecast_id = match parse_uuid_field(&body.forecast_id, "forecastId") {
        Ok(value) => value,
        Err(response) => return response,
    };
    let command = DeleteWeatherForecast { forecast_id };
    execute_command(&state, Some(body.forecast_id), &command).await
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct UpdateLocationReq {
    forecast_id: String,
    new_location: String,
}

async fn update_location(
    State(state): State<AppState>,
    Json(body): Json<UpdateLocationReq>,
) -> impl IntoResponse {
    let forecast_id = match parse_uuid_field(&body.forecast_id, "forecastId") {
        Ok(value) => value,
        Err(response) => return response,
    };
    let command = UpdateWeatherForecastLocation {
        forecast_id,
        new_location: body.new_location,
    };
    execute_command(&state, Some(body.forecast_id), &command).await
}

async fn execute_command<T>(
    state: &AppState,
    forecast_id: Option<String>,
    command: &T,
) -> axum::response::Response
where
    T: Command + Serialize + Send + Sync,
{
    match state.executor.execute_command(command).await {
        Ok(result) => {
            let status = StatusCode::from_u16(result.status_code)
                .unwrap_or(StatusCode::INTERNAL_SERVER_ERROR);
            (
                status,
                Json(CommandResponse {
                    success: result.success,
                    error: (!result.success).then(|| result.response_body.to_string()),
                    sortable_unique_id: result.sortable_unique_id,
                    forecast_id,
                }),
            )
                .into_response()
        }
        Err(ExecuteCommandError::Build(err)) => (
            StatusCode::BAD_REQUEST,
            Json(CommandResponse {
                success: false,
                error: Some(err.to_string()),
                sortable_unique_id: None,
                forecast_id,
            }),
        )
            .into_response(),
        Err(ExecuteCommandError::Transport(err)) => (
            StatusCode::BAD_GATEWAY,
            Json(CommandResponse {
                success: false,
                error: Some(err.to_string()),
                sortable_unique_id: None,
                forecast_id,
            }),
        )
            .into_response(),
    }
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct CommandResponse {
    success: bool,
    error: Option<String>,
    sortable_unique_id: Option<String>,
    forecast_id: Option<String>,
}

fn resolve_wasmserver_base() -> String {
    let candidates = [
        "WASM_SERVER_URL",
        "services__wasmserver__https__0",
        "services__wasmserver__http__0",
        "services__wasmserver__0",
        "WASMSERVER_BASE_URL",
    ];

    for key in candidates {
        if let Ok(value) = env::var(key) {
            let trimmed = value.trim();
            if !trimmed.is_empty() {
                return trimmed.trim_end_matches('/').to_string();
            }
        }
    }

    "http://localhost:5000".to_string()
}

fn init_tracing() {
    let filter = env::var("RUST_LOG").unwrap_or_else(|_| "info".to_string());
    let _ = tracing_subscriber::fmt()
        .with_env_filter(filter)
        .with_target(false)
        .try_init();
}

fn parse_uuid_field(value: &str, field_name: &str) -> Result<uuid::Uuid, axum::response::Response> {
    uuid::Uuid::parse_str(value).map_err(|err| {
        (
            StatusCode::BAD_REQUEST,
            Json(CommandResponse {
                success: false,
                error: Some(format!("invalid {field_name}: {err}")),
                sortable_unique_id: None,
                forecast_id: Some(value.to_string()),
            }),
        )
            .into_response()
    })
}
