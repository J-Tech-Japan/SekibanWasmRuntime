use std::{env, net::SocketAddr};

use axum::{
    extract::State,
    http::{header, HeaderMap, HeaderValue, StatusCode},
    response::IntoResponse,
    routing::{get, post},
    Json, Router,
};
use reqwest::Client;
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
    proxy_post(&state, "/api/weatherforecast", body).await
}

async fn delete_forecast(
    State(state): State<AppState>,
    Json(body): Json<Value>,
) -> impl IntoResponse {
    proxy_post(&state, "/api/weatherforecast/delete", body).await
}

async fn update_location(
    State(state): State<AppState>,
    Json(body): Json<Value>,
) -> impl IntoResponse {
    proxy_post(&state, "/api/weatherforecast/update-location", body).await
}

async fn proxy_post(state: &AppState, path: &str, body: Value) -> impl IntoResponse {
    let url = format!("{}{}", state.wasmserver_base, path);

    let result = state.client.post(url).json(&body).send().await;
    let response = match result {
        Ok(response) => response,
        Err(err) => {
            tracing::error!(error = %err, "forward request failed");
            return (
                StatusCode::BAD_GATEWAY,
                Json(json!({ "error": format!("forward request failed: {err}") })),
            )
                .into_response();
        }
    };

    let status = response.status();
    let content_type = response
        .headers()
        .get(header::CONTENT_TYPE)
        .and_then(|value| value.to_str().ok())
        .unwrap_or("application/json")
        .to_string();

    let bytes = match response.bytes().await {
        Ok(bytes) => bytes,
        Err(err) => {
            tracing::error!(error = %err, "failed to read upstream response");
            return (
                StatusCode::BAD_GATEWAY,
                Json(json!({ "error": format!("failed to read upstream response: {err}") })),
            )
                .into_response();
        }
    };

    let mut headers = HeaderMap::new();
    headers.insert(
        header::CONTENT_TYPE,
        HeaderValue::from_str(&content_type)
            .unwrap_or_else(|_| HeaderValue::from_static("application/json")),
    );

    (status, headers, bytes).into_response()
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
