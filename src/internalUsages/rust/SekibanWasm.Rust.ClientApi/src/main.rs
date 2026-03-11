use std::{collections::HashSet, env, net::SocketAddr, time::Duration};

use async_trait::async_trait;
use axum::{
    extract::{Query as AxumQuery, State},
    http::StatusCode,
    response::IntoResponse,
    routing::{get, post},
    Json, Router,
};
use base64::{engine::general_purpose::STANDARD, Engine as _};
use reqwest::Client;
use sekiban_core::prelude::*;
use sekiban_wasm_domain::{
    CountResult, CreateWeatherForecast, DeleteWeatherForecast, GetWeatherForecastCountQuery,
    GetWeatherForecastListQuery, UpdateWeatherForecastLocation, WeatherForecastItem,
};
use serde::{de::DeserializeOwned, Deserialize, Serialize};
use serde_json::{json, Value};

const WEATHER_PROJECTOR_NAME: &str = "WeatherForecastProjector";

#[derive(Clone)]
struct AppState {
    wasmserver_base: String,
    client: Client,
}

struct HttpCommandContext {
    state: AppState,
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

    match execute_list_query::<_, Vec<WeatherForecastItem>>(&state, &list_query).await {
        Ok(items) => (StatusCode::OK, Json(items)).into_response(),
        Err(err) => {
            tracing::warn!(error = %err, "list query failed");
            (StatusCode::OK, Json(Vec::<WeatherForecastItem>::new())).into_response()
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

    match execute_query::<_, CountResult>(&state, &count_query).await {
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
    execute_and_commit(&state, &command, forecast_id).await
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
    execute_and_commit(&state, &command, Some(body.forecast_id)).await
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
    execute_and_commit(&state, &command, Some(body.forecast_id)).await
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct TagStateRequest {
    tag_state_id: String,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct SerializableTagStateResponse {
    payload: String,
    version: i32,
    last_sorted_unique_id: String,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct SerializedQueryRequest {
    query_type: String,
    query_params_json: String,
    wait_for_sortable_unique_id: Option<String>,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct SerializedQueryResponse {
    result_json: String,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
#[allow(dead_code)]
struct SerializedListQueryResponse {
    items_json: String,
    total_count: Option<i32>,
    total_pages: Option<i32>,
    current_page: Option<i32>,
    page_size: Option<i32>,
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

#[derive(Clone, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
struct ConsistencyTag {
    tag: String,
    last_sortable_unique_id: String,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct CommandResponse {
    success: bool,
    error: Option<String>,
    sortable_unique_id: Option<String>,
    forecast_id: Option<String>,
}

impl HttpCommandContext {
    fn new(state: AppState) -> Self {
        Self { state }
    }

    async fn get_tag_state(&self, tag: &str) -> Result<SerializableTagStateResponse, CommandError> {
        let (tag_group, tag_id) = parse_tag(tag)
            .ok_or_else(|| CommandError::Validation(format!("invalid tag format: {tag}")))?;
        let request = TagStateRequest {
            tag_state_id: format!("{tag_group}:{tag_id}:{WEATHER_PROJECTOR_NAME}"),
        };
        let url = format!(
            "{}/api/sekiban/serialized/tag-state",
            self.state.wasmserver_base
        );
        let response = post_with_retry(&self.state.client, &url, &request)
            .await
            .map_err(|err| CommandError::Validation(format!("tag-state request failed: {err}")))?;

        if !response.status().is_success() {
            let status = response.status();
            let body = response
                .text()
                .await
                .unwrap_or_else(|err| format!("<failed to read error body: {err}>"));
            return Err(CommandError::Validation(format!(
                "tag-state request failed with status {}: {}",
                status, body
            )));
        }

        response
            .json::<SerializableTagStateResponse>()
            .await
            .map_err(|err| {
                CommandError::Serialization(format!("invalid tag-state response: {err}"))
            })
    }
}

#[async_trait]
impl CommandContext for HttpCommandContext {
    async fn get_state<S: StatePayload, T: Tag>(&self, tag: &T) -> Result<(S, i32), CommandError> {
        let response = self.get_tag_state(&tag.to_tag_string()).await?;
        if response.payload.is_empty() {
            return Ok((S::default(), response.version));
        }

        let bytes = STANDARD
            .decode(response.payload)
            .map_err(|err| CommandError::Serialization(format!("invalid state payload: {err}")))?;
        if bytes.is_empty() {
            return Ok((S::default(), response.version));
        }

        let state = serde_json::from_slice::<S>(&bytes)
            .map_err(|err| CommandError::Serialization(format!("invalid state json: {err}")))?;
        Ok((state, response.version))
    }

    async fn tag_exists<T: Tag>(&self, tag: &T) -> Result<bool, CommandError> {
        let response = self.get_tag_state(&tag.to_tag_string()).await?;
        Ok(!response.payload.is_empty())
    }
}

async fn execute_and_commit<T>(
    state: &AppState,
    command: &T,
    forecast_id: Option<String>,
) -> axum::response::Response
where
    T: CommandHandler + Serialize + Sync,
{
    let context = HttpCommandContext::new(state.clone());
    let output = match command.handle(&context).await {
        Ok(Some(output)) => output,
        Ok(None) => {
            return (
                StatusCode::OK,
                Json(CommandResponse {
                    success: true,
                    error: None,
                    sortable_unique_id: None,
                    forecast_id,
                }),
            )
                .into_response();
        }
        Err(err) => {
            return (
                StatusCode::BAD_REQUEST,
                Json(CommandResponse {
                    success: false,
                    error: Some(err.to_string()),
                    sortable_unique_id: None,
                    forecast_id,
                }),
            )
                .into_response();
        }
    };

    let commit_req = match build_commit_request(&context, output).await {
        Ok(req) => req,
        Err(err) => {
            return (
                StatusCode::BAD_REQUEST,
                Json(CommandResponse {
                    success: false,
                    error: Some(err.to_string()),
                    sortable_unique_id: None,
                    forecast_id,
                }),
            )
                .into_response();
        }
    };

    let commit_url = format!("{}/api/sekiban/serialized/commit", state.wasmserver_base);
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
    let sortable_unique_id = extract_sortable_unique_id(&body);

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

async fn build_commit_request(
    context: &HttpCommandContext,
    output: CommandOutput,
) -> Result<CommitRequest, CommandError> {
    let event_candidates = output
        .events
        .into_iter()
        .map(|event| CommitEventCandidate {
            payload: STANDARD.encode(event.payload.as_bytes()),
            event_payload_name: event.event_type,
            tags: output.tags.clone(),
        })
        .collect();

    let mut seen = HashSet::new();
    let mut consistency_tags = Vec::new();
    for tag in output.consistency_tags {
        if !seen.insert(tag.clone()) {
            continue;
        }
        let state = context.get_tag_state(&tag).await?;
        consistency_tags.push(ConsistencyTag {
            tag,
            last_sortable_unique_id: state.last_sorted_unique_id,
        });
    }

    Ok(CommitRequest {
        event_candidates,
        consistency_tags,
    })
}

async fn execute_query<Q, R>(state: &AppState, query: &Q) -> anyhow::Result<R>
where
    Q: sekiban_core::query::Query + Serialize,
    R: DeserializeOwned,
{
    let request = SerializedQueryRequest {
        query_type: Q::QUERY_TYPE.to_string(),
        query_params_json: serde_json::to_string(query)?,
        wait_for_sortable_unique_id: query.wait_for_sortable_id().map(ToOwned::to_owned),
    };
    let url = format!("{}/api/sekiban/serialized/query", state.wasmserver_base);
    let response = post_with_retry(&state.client, &url, &request).await?;
    let response = response.error_for_status()?;
    let body: SerializedQueryResponse = response.json().await?;
    Ok(serde_json::from_str(&body.result_json)?)
}

async fn execute_list_query<Q, R>(state: &AppState, query: &Q) -> anyhow::Result<R>
where
    Q: sekiban_core::query::ListQuery + Serialize,
    R: DeserializeOwned,
{
    let request = SerializedQueryRequest {
        query_type: Q::QUERY_TYPE.to_string(),
        query_params_json: serde_json::to_string(query)?,
        wait_for_sortable_unique_id: query.wait_for_sortable_id().map(ToOwned::to_owned),
    };
    let url = format!(
        "{}/api/sekiban/serialized/list-query",
        state.wasmserver_base
    );
    let response = post_with_retry(&state.client, &url, &request).await?;
    let response = response.error_for_status()?;
    let body: SerializedListQueryResponse = response.json().await?;
    Ok(serde_json::from_str(&body.items_json)?)
}

fn extract_sortable_unique_id(body: &Value) -> Option<String> {
    body.get("sortableUniqueId")
        .and_then(Value::as_str)
        .map(ToOwned::to_owned)
        .or_else(|| {
            body.get("writtenEvents")
                .and_then(Value::as_array)
                .and_then(|events| events.first())
                .and_then(|event| event.get("sortableUniqueIdValue"))
                .and_then(Value::as_str)
                .map(ToOwned::to_owned)
        })
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
