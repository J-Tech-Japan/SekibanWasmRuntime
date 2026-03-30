use std::{env, net::SocketAddr, sync::Arc};

use anyhow::Result;
use axum::{
    extract::{Query as AxumQuery, State},
    http::StatusCode,
    response::IntoResponse,
    routing::{get, post},
    Json, Router,
};
use base64::{engine::general_purpose::STANDARD, Engine as _};
use chrono::Utc;
use reqwest::Client;
use sekiban_core::prelude::*;
use sekiban_dcb_decider_rust_eventsource::{
    CountResult, CreateWeatherForecast, GetWeatherForecastCountQuery, GetWeatherForecastListQuery,
    WeatherForecastCreated, WeatherForecastDeleted, WeatherForecastItem,
    WeatherForecastLocationUpdated, WeatherForecastTag,
};
use sekiban_executor::{
    extract_sortable_unique_id, CommandExecutionResult, CommitEventCandidate, CommitRequest,
    ConsistencyTag, ExecuteCommandError, RemoteSekibanExecutor, StaticTagProjectorResolver,
    TagLatestSortableRequest, TagLatestSortableResponse,
};
use serde::{Deserialize, Serialize};
use serde_json::{json, Value};

#[derive(Clone)]
struct AppState {
    executor: Arc<RemoteSekibanExecutor>,
    http: Client,
    wasmserver_base: String,
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
        wasmserver_base.clone(),
        StaticTagProjectorResolver::new().with_tag_group("weather", "WeatherForecastProjector"),
    ));
    let http = Client::new();

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
        .with_state(AppState {
            executor,
            http,
            wasmserver_base,
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
        forecast_id: None,
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
        forecast_id: None,
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
    let forecast_id = command
        .forecast_id
        .filter(|id| *id != uuid::Uuid::nil())
        .unwrap_or_else(uuid::Uuid::now_v7);
    let normalized = CreateWeatherForecast {
        forecast_id: Some(forecast_id),
        location: command.location,
        temperature_c: command.temperature_c,
        summary: command.summary,
    };
    execute_create_command(&state, normalized).await
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
    execute_delete_command(&state, forecast_id, body.forecast_id).await
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
    execute_update_command(&state, forecast_id, body.forecast_id, body.new_location).await
}

async fn execute_create_command(
    state: &AppState,
    command: CreateWeatherForecast,
) -> axum::response::Response {
    let forecast_id = command
        .forecast_id
        .expect("create command should always contain normalized forecast_id");
    let response_forecast_id = Some(forecast_id.to_string());

    match build_create_commit_request(state, command, forecast_id).await {
        Ok(commit_request) => {
            render_command_result(response_forecast_id, post_commit_request(state, commit_request).await)
        }
        Err(err) => render_command_result(response_forecast_id, Err(err)),
    }
}

async fn execute_update_command(
    state: &AppState,
    forecast_id: uuid::Uuid,
    response_forecast_id: String,
    new_location: String,
) -> axum::response::Response {
    let result = match build_update_commit_request(state, forecast_id, &new_location).await {
        Ok(request) => post_commit_request(state, request).await,
        Err(err) => Err(err),
    };
    render_command_result(Some(response_forecast_id), result)
}

async fn execute_delete_command(
    state: &AppState,
    forecast_id: uuid::Uuid,
    response_forecast_id: String,
) -> axum::response::Response {
    let result = match build_delete_commit_request(state, forecast_id).await {
        Ok(request) => post_commit_request(state, request).await,
        Err(err) => Err(err),
    };
    render_command_result(Some(response_forecast_id), result)
}

fn render_command_result(
    forecast_id: Option<String>,
    result: Result<CommandExecutionResult, ExecuteCommandError>,
) -> axum::response::Response {
    match result {
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

async fn build_create_commit_request(
    state: &AppState,
    command: CreateWeatherForecast,
    forecast_id: uuid::Uuid,
) -> Result<CommitRequest, ExecuteCommandError> {
    let latest_sortable = get_latest_sortable_for_forecast(state, forecast_id).await?;
    if let Some(wait_for_sortable_id) = latest_sortable.clone() {
        if get_forecast(state, forecast_id, Some(wait_for_sortable_id)).await?.is_some() {
            return Err(ExecuteCommandError::Build(CommandError::AlreadyExists(
                forecast_id.to_string(),
            )));
        }
    }

    let event = WeatherForecastCreated {
        forecast_id,
        location: command.location,
        temperature_c: command.temperature_c,
        summary: command.summary,
        created_at: Utc::now().to_rfc3339(),
    };

    build_commit_request(forecast_id, "WeatherForecastCreated", &event, latest_sortable)
}

async fn build_update_commit_request(
    state: &AppState,
    forecast_id: uuid::Uuid,
    new_location: &str,
) -> Result<CommitRequest, ExecuteCommandError> {
    let latest_sortable = require_latest_sortable_for_forecast(state, forecast_id).await?;
    let current = get_forecast(state, forecast_id, Some(latest_sortable.clone()))
        .await?
        .ok_or_else(|| ExecuteCommandError::Build(CommandError::NotFound(forecast_id.to_string())))?;

    if current.location == new_location {
        return Ok(noop_commit_request());
    }

    let event = WeatherForecastLocationUpdated {
        forecast_id,
        new_location: new_location.to_string(),
        updated_at: Utc::now().to_rfc3339(),
    };

    build_commit_request(
        forecast_id,
        "WeatherForecastLocationUpdated",
        &event,
        Some(latest_sortable),
    )
}

async fn build_delete_commit_request(
    state: &AppState,
    forecast_id: uuid::Uuid,
) -> Result<CommitRequest, ExecuteCommandError> {
    let latest_sortable = require_latest_sortable_for_forecast(state, forecast_id).await?;
    get_forecast(state, forecast_id, Some(latest_sortable.clone()))
        .await?
        .ok_or_else(|| ExecuteCommandError::Build(CommandError::NotFound(forecast_id.to_string())))?;

    let event = WeatherForecastDeleted {
        forecast_id,
        deleted_at: Utc::now().to_rfc3339(),
    };

    build_commit_request(
        forecast_id,
        "WeatherForecastDeleted",
        &event,
        Some(latest_sortable),
    )
}

fn build_commit_request<T: Serialize>(
    forecast_id: uuid::Uuid,
    event_name: &str,
    event: &T,
    latest_sortable: Option<String>,
) -> Result<CommitRequest, ExecuteCommandError> {
    let payload = serde_json::to_vec(event)
        .map_err(|err| ExecuteCommandError::Transport(anyhow::anyhow!(err)))?;
    let tag = WeatherForecastTag::new(forecast_id).to_tag_string();

    Ok(CommitRequest {
        event_candidates: vec![CommitEventCandidate {
            payload: STANDARD.encode(payload),
            event_payload_name: event_name.to_string(),
            tags: vec![tag.clone()],
        }],
        consistency_tags: vec![ConsistencyTag {
            tag,
            last_sortable_unique_id: latest_sortable.unwrap_or_default(),
        }],
    })
}

fn noop_commit_request() -> CommitRequest {
    CommitRequest {
        event_candidates: Vec::new(),
        consistency_tags: Vec::new(),
    }
}

async fn post_commit_request(
    state: &AppState,
    commit_request: CommitRequest,
) -> Result<CommandExecutionResult, ExecuteCommandError> {
    if commit_request.event_candidates.is_empty() {
        return Ok(CommandExecutionResult {
            status_code: 200,
            success: true,
            sortable_unique_id: None,
            response_body: json!({ "success": true }),
        });
    }

    let response = state
        .http
        .post(format!(
            "{}/api/sekiban/serialized/commit",
            state.wasmserver_base
        ))
        .json(&commit_request)
        .send()
        .await
        .map_err(|err| ExecuteCommandError::Transport(anyhow::anyhow!(err)))?;

    let status = response.status();
    let response_body: Value = response
        .json()
        .await
        .unwrap_or_else(|_| json!({ "error": "failed to read commit response" }));

    Ok(CommandExecutionResult {
        status_code: status.as_u16(),
        success: status.is_success(),
        sortable_unique_id: extract_sortable_unique_id(&response_body),
        response_body,
    })
}

async fn get_latest_sortable_for_forecast(
    state: &AppState,
    forecast_id: uuid::Uuid,
) -> Result<Option<String>, ExecuteCommandError> {
    let tag = WeatherForecastTag::new(forecast_id).to_tag_string();
    let response = state
        .http
        .post(format!(
            "{}/api/sekiban/serialized/tag-latest-sortable",
            state.wasmserver_base
        ))
        .json(&TagLatestSortableRequest { tag })
        .send()
        .await
        .map_err(|err| ExecuteCommandError::Transport(anyhow::anyhow!(err)))?;

    if !response.status().is_success() {
        let status = response.status();
        let body = response
            .text()
            .await
            .unwrap_or_else(|err| format!("<failed to read error body: {err}>"));
        return Err(ExecuteCommandError::Build(CommandError::Validation(format!(
            "tag-latest-sortable request failed with status {status}: {body}"
        ))));
    }

    let body = response
        .json::<TagLatestSortableResponse>()
        .await
        .map_err(|err| ExecuteCommandError::Build(CommandError::Serialization(err.to_string())))?;

    Ok(body
        .exists
        .then_some(body.last_sortable_unique_id)
        .filter(|value| !value.is_empty()))
}

async fn require_latest_sortable_for_forecast(
    state: &AppState,
    forecast_id: uuid::Uuid,
) -> Result<String, ExecuteCommandError> {
    get_latest_sortable_for_forecast(state, forecast_id)
        .await?
        .ok_or_else(|| ExecuteCommandError::Build(CommandError::NotFound(forecast_id.to_string())))
}

async fn get_forecast(
    state: &AppState,
    forecast_id: uuid::Uuid,
    wait_for_sortable_id: Option<String>,
) -> Result<Option<WeatherForecastItem>, ExecuteCommandError> {
    let list_query = GetWeatherForecastListQuery {
        location_filter: None,
        forecast_id: Some(forecast_id),
        wait_for_sortable_unique_id: wait_for_sortable_id,
    };

    state
        .executor
        .execute_list_query::<_, Vec<WeatherForecastItem>>(&list_query)
        .await
        .map(|items| items.into_iter().next())
        .map_err(|err| ExecuteCommandError::Transport(anyhow::anyhow!(err)))
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
