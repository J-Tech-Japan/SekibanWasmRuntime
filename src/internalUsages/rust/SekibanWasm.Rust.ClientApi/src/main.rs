use std::{env, net::SocketAddr, time::Duration};

use axum::{
    extract::State,
    http::StatusCode,
    response::IntoResponse,
    routing::{get, post},
    Json, Router,
};
use reqwest::Client;
use serde::{Deserialize, Serialize};
use serde_json::{json, Value};
use sekiban_wasm_domain::WeatherForecastItem;

#[derive(Clone)]
struct AppState {
    wasmserver_base: String,
    client: Client,
}

#[tokio::main]
async fn main() -> anyhow::Result<()> {
    init_tracing();

    let port = env::var("PORT")
        .ok()
        .and_then(|v| v.parse::<u16>().ok())
        .unwrap_or(8080);

    let wasmserver_base = resolve_wasmserver_base();
    tracing::info!(%wasmserver_base, "resolved wasmserver base");

    let app = Router::new()
        .route("/health", get(health))
        .route("/api/weatherforecast", get(get_forecasts).post(create_forecast))
        .route("/api/weatherforecast/delete", post(delete_forecast))
        .route(
            "/api/weatherforecast/update-location",
            post(update_location),
        )
        .with_state(AppState {
            wasmserver_base,
            client: Client::new(),
        });

    let addr = SocketAddr::from(([0, 0, 0, 0], port));
    tracing::info!(%addr, "Rust ClientApi listening");

    let listener = tokio::net::TcpListener::bind(addr).await?;
    axum::serve(listener, app).await?;
    Ok(())
}

async fn health() -> impl IntoResponse {
    Json(json!({ "message": "WeatherForecast API is running" }))
}

async fn get_forecasts(State(state): State<AppState>) -> impl IntoResponse {
    let url = format!("{}/api/weatherforecast", state.wasmserver_base);
    match tokio::time::timeout(Duration::from_secs(2), state.client.get(&url).send()).await {
        Ok(Ok(resp)) => {
            if resp.status().is_success() {
                let body: Vec<WeatherForecastItem> = resp.json().await.unwrap_or_default();
                (StatusCode::OK, Json(body)).into_response()
            } else {
                (StatusCode::OK, Json(json!([]))).into_response()
            }
        }
        Ok(Err(_)) => (StatusCode::OK, Json(json!([]))).into_response(),
        Err(_) => (StatusCode::OK, Json(json!([]))).into_response(),
    }
}

async fn create_forecast(
    State(state): State<AppState>,
    Json(command): Json<CreateWeatherForecastCommand>,
) -> impl IntoResponse {
    let forecast_id = command.forecast_id.map(|id| id.to_string());
    execute_and_commit(&state, "CreateWeatherForecast", &command, forecast_id).await
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct DeleteRequest {
    forecast_id: String,
}

#[derive(Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
struct CreateWeatherForecastCommand {
    forecast_id: Option<uuid::Uuid>,
    location: String,
    temperature_c: i32,
    summary: String,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct DeleteWeatherForecastCommand {
    forecast_id: uuid::Uuid,
}

async fn delete_forecast(
    State(state): State<AppState>,
    Json(body): Json<DeleteRequest>,
) -> impl IntoResponse {
    let forecast_id = match parse_uuid_field(&body.forecast_id, "forecastId") {
        Ok(value) => value,
        Err(response) => return response,
    };
    let command = DeleteWeatherForecastCommand { forecast_id };
    execute_and_commit(
        &state,
        "DeleteWeatherForecast",
        &command,
        Some(body.forecast_id),
    )
    .await
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct UpdateLocationReq {
    forecast_id: String,
    new_location: String,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct UpdateWeatherForecastLocationCommand {
    forecast_id: uuid::Uuid,
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
    let command = UpdateWeatherForecastLocationCommand {
        forecast_id,
        new_location: body.new_location,
    };
    execute_and_commit(
        &state,
        "UpdateWeatherForecastLocation",
        &command,
        Some(body.forecast_id),
    )
    .await
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct CommandExecuteRequest {
    command_name: String,
    command_json: String,
    consistency_tags: Option<Vec<Value>>,
    options: Option<Value>,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct CommandExecuteResponse {
    event_candidates: Vec<EventCandidate>,
    consistency_tags: Vec<ConsistencyTag>,
    _command_result_json: Option<String>,
}

#[derive(Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
struct EventCandidate {
    event_payload_name: String,
    payload_base64: String,
    tags: Vec<String>,
}

#[derive(Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
struct ConsistencyTag {
    tag: String,
    last_sortable_unique_id: String,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct CommitRequest {
    event_candidates: Vec<CommitEventCandidate>,
    consistency_tags: Vec<ConsistencyTag>,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct CommitEventCandidate {
    payload: String,
    event_payload_name: String,
    tags: Vec<String>,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct CommandResponse {
    success: bool,
    error: Option<String>,
    sortable_unique_id: Option<String>,
    forecast_id: Option<String>,
}

async fn execute_and_commit<T: Serialize>(
    state: &AppState,
    command_name: &str,
    command: &T,
    forecast_id: Option<String>,
) -> axum::response::Response {
    let command_json = match serde_json::to_string(command) {
        Ok(json) => json,
        Err(err) => {
            return (
                StatusCode::BAD_REQUEST,
                Json(CommandResponse {
                    success: false,
                    error: Some(format!("failed to serialize command: {err}")),
                    sortable_unique_id: None,
                    forecast_id,
                }),
            )
                .into_response();
        }
    };

    let execute_req = CommandExecuteRequest {
        command_name: command_name.to_string(),
        command_json,
        consistency_tags: None,
        options: None,
    };

    let execute_url = format!(
        "{}/api/sekiban/serialized/command/execute",
        state.wasmserver_base
    );

    let execute_resp = match post_with_retry(&state.client, &execute_url, &execute_req).await {
        Ok(resp) => resp,
        Err(err) => {
            tracing::error!(error = %err, "command/execute request failed");
            return (
                StatusCode::BAD_GATEWAY,
                Json(CommandResponse {
                    success: false,
                    error: Some(format!("command/execute request failed: {err}")),
                    sortable_unique_id: None,
                    forecast_id,
                }),
            )
                .into_response();
        }
    };

    if !execute_resp.status().is_success() {
        let status = execute_resp.status();
        let body = execute_resp.text().await.unwrap_or_default();
        tracing::error!(%status, %body, "command/execute returned error");
        return (
            StatusCode::from_u16(status.as_u16()).unwrap_or(StatusCode::BAD_GATEWAY),
            Json(CommandResponse {
                success: false,
                error: Some(body),
                sortable_unique_id: None,
                forecast_id,
            }),
        )
            .into_response();
    }

    let execute_response: CommandExecuteResponse = match execute_resp.json().await {
        Ok(r) => r,
        Err(err) => {
            tracing::error!(error = %err, "failed to parse command/execute response");
            return (
                StatusCode::BAD_GATEWAY,
                Json(CommandResponse {
                    success: false,
                    error: Some(format!("failed to parse command/execute response: {err}")),
                    sortable_unique_id: None,
                    forecast_id,
                }),
            )
                .into_response();
        }
    };

    let commit_candidates: Vec<CommitEventCandidate> = execute_response
        .event_candidates
        .iter()
        .map(|ec| CommitEventCandidate {
            payload: ec.payload_base64.clone(),
            event_payload_name: ec.event_payload_name.clone(),
            tags: ec.tags.clone(),
        })
        .collect();

    let commit_req = CommitRequest {
        event_candidates: commit_candidates,
        consistency_tags: execute_response.consistency_tags,
    };

    let commit_url = format!(
        "{}/api/sekiban/serialized/commit",
        state.wasmserver_base
    );

    let commit_resp = match post_with_retry(&state.client, &commit_url, &commit_req).await {
        Ok(resp) => resp,
        Err(err) => {
            tracing::error!(error = %err, "commit request failed");
            return (
                StatusCode::BAD_GATEWAY,
                Json(CommandResponse {
                    success: false,
                    error: Some(format!("commit request failed: {err}")),
                    sortable_unique_id: None,
                    forecast_id,
                }),
            )
                .into_response();
        }
    };

    let status = commit_resp.status();
    let body: Value = commit_resp
        .json()
        .await
        .unwrap_or(json!({ "error": "failed to read commit response" }));
    let sortable_unique_id = body
        .get("sortableUniqueId")
        .and_then(Value::as_str)
        .map(ToOwned::to_owned);

    (
        StatusCode::from_u16(status.as_u16()).unwrap_or(StatusCode::INTERNAL_SERVER_ERROR),
        Json(CommandResponse {
            success: status.is_success(),
            error: (!status.is_success()).then(|| body.to_string()),
            sortable_unique_id,
            forecast_id,
        }),
    )
        .into_response()
}

async fn post_with_retry<T: Serialize>(
    client: &Client,
    url: &str,
    body: &T,
) -> Result<reqwest::Response, reqwest::Error> {
    let max_attempts = 5;
    let mut last_err: Option<reqwest::Error> = None;

    for attempt in 1..=max_attempts {
        match client.post(url).json(body).send().await {
            Ok(resp) => return Ok(resp),
            Err(err) => {
                last_err = Some(err);
                if attempt < max_attempts {
                    tokio::time::sleep(Duration::from_millis(250)).await;
                }
            }
        }
    }

    Err(last_err.expect("retry loop should capture at least one error"))
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
