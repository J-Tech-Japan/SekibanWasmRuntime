use std::{env, net::SocketAddr};

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
        .route("/api/weatherforecast", get(health).post(create_forecast))
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

async fn create_forecast(
    State(state): State<AppState>,
    Json(body): Json<Value>,
) -> impl IntoResponse {
    execute_and_commit(&state, "CreateWeatherForecast", &body).await
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
    let command = json!({ "forecastId": body.forecast_id });
    execute_and_commit(&state, "DeleteWeatherForecast", &command).await
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
    let command = json!({
        "forecastId": body.forecast_id,
        "newLocation": body.new_location,
    });
    execute_and_commit(&state, "UpdateWeatherForecastLocation", &command).await
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
    command_result_json: Option<String>,
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

async fn execute_and_commit(state: &AppState, command_name: &str, command: &Value) -> impl IntoResponse {
    let command_json = serde_json::to_string(command).unwrap();

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

    let execute_resp = match state.client.post(&execute_url).json(&execute_req).send().await {
        Ok(resp) => resp,
        Err(err) => {
            tracing::error!(error = %err, "command/execute request failed");
            return (
                StatusCode::BAD_GATEWAY,
                Json(json!({ "error": format!("command/execute request failed: {err}") })),
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
            Json(json!({ "error": body })),
        )
            .into_response();
    }

    let execute_response: CommandExecuteResponse = match execute_resp.json().await {
        Ok(r) => r,
        Err(err) => {
            tracing::error!(error = %err, "failed to parse command/execute response");
            return (
                StatusCode::BAD_GATEWAY,
                Json(json!({ "error": format!("failed to parse command/execute response: {err}") })),
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

    let commit_resp = match state.client.post(&commit_url).json(&commit_req).send().await {
        Ok(resp) => resp,
        Err(err) => {
            tracing::error!(error = %err, "commit request failed");
            return (
                StatusCode::BAD_GATEWAY,
                Json(json!({ "error": format!("commit request failed: {err}") })),
            )
                .into_response();
        }
    };

    let status = commit_resp.status();
    let body: Value = commit_resp.json().await.unwrap_or(json!({ "error": "failed to read commit response" }));

    (
        StatusCode::from_u16(status.as_u16()).unwrap_or(StatusCode::INTERNAL_SERVER_ERROR),
        Json(body),
    )
        .into_response()
}

fn resolve_wasmserver_base() -> String {
    let candidates = [
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
