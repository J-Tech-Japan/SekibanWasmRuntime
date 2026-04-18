//! Read-only `/api/mv/*` endpoints that talk to the Sekiban materialized view Postgres database
//! directly via `sqlx`. Keeps the WASM runtime host (`wasmserver`) strictly generic — its only
//! job is to run WASM modules and own the MV apply pipeline. Any language-specific read API
//! (this one for Rust) lives in the host-language ClientApi.
//!
//! Table layout is driven by Sekiban's `sekiban_mv_registry` which maps
//! `(service_id, view_name, view_version, logical_table)` to a physical Postgres table name.
//! We resolve the physical name at request time (the registry is tiny — three rows for the
//! ClassRoomEnrollment view — and this keeps the code immune to view re-initialization).

use anyhow::{anyhow, Context, Result};
use axum::{
    extract::{Query, State},
    http::StatusCode,
    response::{IntoResponse, Response},
    Json,
};
use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};
use sqlx::{postgres::PgPoolOptions, PgPool, Row};
use uuid::Uuid;

pub const VIEW_NAME: &str = "ClassRoomEnrollment";
pub const VIEW_VERSION: i32 = 1;
pub const CLASSROOMS_LOGICAL: &str = "classrooms";
pub const STUDENTS_LOGICAL: &str = "students";
pub const ENROLLMENTS_LOGICAL: &str = "enrollments";

/// Service id used to scope `sekiban_mv_registry` lookups. Matches Sekiban's
/// `DefaultServiceIdProvider.DefaultServiceId` (`"default"`). Override at startup via
/// `SEKIBAN_SERVICE_ID` to match a non-default deployment so read APIs never cross-contaminate
/// entries across tenants that happen to share the same MV Postgres.
fn current_service_id() -> String {
    std::env::var("SEKIBAN_SERVICE_ID")
        .ok()
        .filter(|v| !v.trim().is_empty())
        .unwrap_or_else(|| "default".to_string())
}

/// Connect lazily on startup. Accepts the Aspire-provided `DCBMATERIALIZEDVIEWPOSTGRES_URI`
/// (a ready-to-use `postgresql://…` URL) as the primary source, with
/// `ConnectionStrings__DcbMaterializedViewPostgres` (Npgsql key/value form) as the fallback
/// that we parse into the same URL.
pub async fn connect_from_env() -> Result<Option<PgPool>> {
    let url = match resolve_connection_url() {
        Some(u) => u,
        None => {
            tracing::warn!(
                "materialized view: no DCBMATERIALIZEDVIEWPOSTGRES_URI / \
                 ConnectionStrings__DcbMaterializedViewPostgres env var — \
                 /api/mv/* will return 503"
            );
            return Ok(None);
        }
    };
    let pool = PgPoolOptions::new()
        .max_connections(4)
        .acquire_timeout(std::time::Duration::from_secs(5))
        .connect(&url)
        .await
        .context("connect to DcbMaterializedViewPostgres")?;
    Ok(Some(pool))
}

fn resolve_connection_url() -> Option<String> {
    if let Ok(url) = std::env::var("DCBMATERIALIZEDVIEWPOSTGRES_URI") {
        if !url.trim().is_empty() {
            return Some(url);
        }
    }
    // Aspire also writes "ConnectionStrings__DcbMaterializedViewPostgres" as a
    // Host=...;Port=...;Username=...;Password=...;Database=... Npgsql string. Convert on the fly.
    let raw = std::env::var("ConnectionStrings__DcbMaterializedViewPostgres").ok()?;
    npgsql_to_url(&raw)
}

fn npgsql_to_url(raw: &str) -> Option<String> {
    let mut host = None;
    let mut port = None;
    let mut username = None;
    let mut password = None;
    let mut database = None;
    for pair in raw.split(';') {
        let pair = pair.trim();
        if pair.is_empty() {
            continue;
        }
        let (k, v) = pair.split_once('=')?;
        match k.trim().to_ascii_lowercase().as_str() {
            "host" | "server" => host = Some(v.trim().to_string()),
            "port" => port = Some(v.trim().to_string()),
            "username" | "user id" | "uid" => username = Some(v.trim().to_string()),
            "password" | "pwd" => password = Some(v.trim().to_string()),
            "database" | "db" => database = Some(v.trim().to_string()),
            _ => {}
        }
    }
    let host = host?;
    let port = port.unwrap_or_else(|| "5432".to_string());
    let username = username.unwrap_or_else(|| "postgres".to_string());
    let password = password.unwrap_or_default();
    let database = database?;
    Some(format!(
        "postgresql://{user}:{pass}@{host}:{port}/{db}",
        user = urlencoding::encode(&username),
        pass = urlencoding::encode(&password),
        host = host,
        port = port,
        db = database,
    ))
}

// --------------------------------------------------------------------------------------------
// HTTP handlers
// --------------------------------------------------------------------------------------------

#[derive(Clone)]
pub struct MvState {
    pub pool: Option<PgPool>,
}

#[derive(Debug, Deserialize)]
pub struct PagingQuery {
    #[serde(default)]
    pub page_number: Option<i32>,
    #[serde(default)]
    pub page_size: Option<i32>,
}

fn resolve_paging(p: &PagingQuery) -> (i64, i64) {
    let size = p.page_size.filter(|v| *v > 0).unwrap_or(20) as i64;
    let page = p.page_number.filter(|v| *v > 0).unwrap_or(1) as i64;
    (size, (page - 1) * size)
}

pub async fn get_status(State(state): State<MvState>) -> Response {
    let pool = match state.pool.as_ref() {
        Some(p) => p,
        None => return mv_disabled(),
    };
    match fetch_status(pool).await {
        Ok(resp) => Json(resp).into_response(),
        Err(e) => server_error(e),
    }
}

pub async fn get_classrooms(
    State(state): State<MvState>,
    Query(paging): Query<PagingQuery>,
) -> Response {
    let pool = match state.pool.as_ref() {
        Some(p) => p,
        None => return mv_disabled(),
    };
    let (limit, offset) = resolve_paging(&paging);
    match fetch_classrooms(pool, limit, offset).await {
        Ok(rows) => Json(rows).into_response(),
        Err(e) => server_error(e),
    }
}

pub async fn get_students(
    State(state): State<MvState>,
    Query(paging): Query<PagingQuery>,
) -> Response {
    let pool = match state.pool.as_ref() {
        Some(p) => p,
        None => return mv_disabled(),
    };
    let (limit, offset) = resolve_paging(&paging);
    match fetch_students(pool, limit, offset).await {
        Ok(rows) => Json(rows).into_response(),
        Err(e) => server_error(e),
    }
}

#[derive(Debug, Deserialize)]
pub struct EnrollmentsFilter {
    #[serde(flatten)]
    pub paging: PagingQuery,
    #[serde(default)]
    pub student_id: Option<Uuid>,
    #[serde(default)]
    pub class_room_id: Option<Uuid>,
}

pub async fn get_enrollments(
    State(state): State<MvState>,
    Query(filter): Query<EnrollmentsFilter>,
) -> Response {
    let pool = match state.pool.as_ref() {
        Some(p) => p,
        None => return mv_disabled(),
    };
    let (limit, offset) = resolve_paging(&filter.paging);
    match fetch_enrollments(pool, limit, offset, filter.student_id, filter.class_room_id).await {
        Ok(rows) => Json(rows).into_response(),
        Err(e) => server_error(e),
    }
}

// --------------------------------------------------------------------------------------------
// Registry + data access
// --------------------------------------------------------------------------------------------

async fn physical_table(pool: &PgPool, logical: &str) -> Result<String> {
    let service_id = current_service_id();
    let row = sqlx::query(
        "SELECT physical_table \
         FROM sekiban_mv_registry \
         WHERE service_id = $1 AND view_name = $2 AND view_version = $3 AND logical_table = $4 \
         LIMIT 1",
    )
    .bind(&service_id)
    .bind(VIEW_NAME)
    .bind(VIEW_VERSION)
    .bind(logical)
    .fetch_optional(pool)
    .await
    .context("lookup physical_table in sekiban_mv_registry")?;
    match row {
        Some(r) => Ok(r.try_get::<String, _>("physical_table")?),
        None => Err(anyhow!(
            "materialized view '{logical}' not registered for {service_id}/{VIEW_NAME}/{VIEW_VERSION}"
        )),
    }
}

#[derive(Debug, Serialize)]
struct StatusEntry {
    service_id: String,
    view_name: String,
    view_version: i32,
    logical_table: String,
    physical_table: String,
    status: i32,
    applied_event_version: i64,
    current_position: Option<String>,
    last_catch_up_sortable_unique_id: Option<String>,
    last_updated: Option<DateTime<Utc>>,
}

#[derive(Debug, Serialize)]
struct StatusResponse {
    service_id: String,
    view_name: String,
    view_version: i32,
    entries: Vec<StatusEntry>,
}

async fn fetch_status(pool: &PgPool) -> Result<StatusResponse> {
    let service_id = current_service_id();
    let rows = sqlx::query(
        "SELECT service_id, view_name, view_version, logical_table, physical_table, status, \
         applied_event_version, current_position, last_catch_up_sortable_unique_id, last_updated \
         FROM sekiban_mv_registry \
         WHERE service_id = $1 AND view_name = $2 AND view_version = $3 \
         ORDER BY logical_table",
    )
    .bind(&service_id)
    .bind(VIEW_NAME)
    .bind(VIEW_VERSION)
    .fetch_all(pool)
    .await?;
    let entries: Vec<StatusEntry> = rows
        .into_iter()
        .map(|r| StatusEntry {
            service_id: r.try_get("service_id").unwrap_or_default(),
            view_name: r.try_get("view_name").unwrap_or_default(),
            view_version: r.try_get("view_version").unwrap_or_default(),
            logical_table: r.try_get("logical_table").unwrap_or_default(),
            physical_table: r.try_get("physical_table").unwrap_or_default(),
            status: r.try_get("status").unwrap_or_default(),
            applied_event_version: r.try_get("applied_event_version").unwrap_or_default(),
            current_position: r.try_get("current_position").ok(),
            last_catch_up_sortable_unique_id: r.try_get("last_catch_up_sortable_unique_id").ok(),
            last_updated: r.try_get("last_updated").ok(),
        })
        .collect();
    Ok(StatusResponse {
        service_id: entries
            .first()
            .map(|e| e.service_id.clone())
            .unwrap_or_default(),
        view_name: VIEW_NAME.to_string(),
        view_version: VIEW_VERSION,
        entries,
    })
}

#[derive(Debug, Serialize)]
struct ClassRoomMvRow {
    class_room_id: Uuid,
    name: String,
    max_students: i32,
    enrolled_count: i32,
    last_sortable_unique_id: String,
    last_applied_at: DateTime<Utc>,
}

async fn fetch_classrooms(pool: &PgPool, limit: i64, offset: i64) -> Result<Vec<ClassRoomMvRow>> {
    let table = physical_table(pool, CLASSROOMS_LOGICAL).await?;
    // Physical table name comes from the registry, which Sekiban itself controls — safe to
    // interpolate into the SQL.
    let sql = format!(
        "SELECT class_room_id, name, max_students, enrolled_count, \
         _last_sortable_unique_id AS last_sortable_unique_id, \
         _last_applied_at AS last_applied_at \
         FROM {table} \
         ORDER BY name \
         LIMIT $1 OFFSET $2"
    );
    let rows = sqlx::query(&sql)
        .bind(limit)
        .bind(offset)
        .fetch_all(pool)
        .await?;
    Ok(rows
        .into_iter()
        .map(|r| ClassRoomMvRow {
            class_room_id: r.try_get("class_room_id").unwrap_or_else(|_| Uuid::nil()),
            name: r.try_get("name").unwrap_or_default(),
            max_students: r.try_get("max_students").unwrap_or_default(),
            enrolled_count: r.try_get("enrolled_count").unwrap_or_default(),
            last_sortable_unique_id: r.try_get("last_sortable_unique_id").unwrap_or_default(),
            last_applied_at: r.try_get("last_applied_at").unwrap_or_else(|_| Utc::now()),
        })
        .collect())
}

#[derive(Debug, Serialize)]
struct StudentMvRow {
    student_id: Uuid,
    name: String,
    max_class_count: i32,
    enrolled_count: i32,
    last_sortable_unique_id: String,
    last_applied_at: DateTime<Utc>,
}

async fn fetch_students(pool: &PgPool, limit: i64, offset: i64) -> Result<Vec<StudentMvRow>> {
    let table = physical_table(pool, STUDENTS_LOGICAL).await?;
    let sql = format!(
        "SELECT student_id, name, max_class_count, enrolled_count, \
         _last_sortable_unique_id AS last_sortable_unique_id, \
         _last_applied_at AS last_applied_at \
         FROM {table} \
         ORDER BY name \
         LIMIT $1 OFFSET $2"
    );
    let rows = sqlx::query(&sql)
        .bind(limit)
        .bind(offset)
        .fetch_all(pool)
        .await?;
    Ok(rows
        .into_iter()
        .map(|r| StudentMvRow {
            student_id: r.try_get("student_id").unwrap_or_else(|_| Uuid::nil()),
            name: r.try_get("name").unwrap_or_default(),
            max_class_count: r.try_get("max_class_count").unwrap_or_default(),
            enrolled_count: r.try_get("enrolled_count").unwrap_or_default(),
            last_sortable_unique_id: r.try_get("last_sortable_unique_id").unwrap_or_default(),
            last_applied_at: r.try_get("last_applied_at").unwrap_or_else(|_| Utc::now()),
        })
        .collect())
}

#[derive(Debug, Serialize)]
struct EnrollmentMvRow {
    student_id: Uuid,
    class_room_id: Uuid,
    enrolled_at: DateTime<Utc>,
    last_sortable_unique_id: String,
}

async fn fetch_enrollments(
    pool: &PgPool,
    limit: i64,
    offset: i64,
    student_id: Option<Uuid>,
    class_room_id: Option<Uuid>,
) -> Result<Vec<EnrollmentMvRow>> {
    let table = physical_table(pool, ENROLLMENTS_LOGICAL).await?;
    let mut sql = format!(
        "SELECT student_id, class_room_id, enrolled_at, \
         _last_sortable_unique_id AS last_sortable_unique_id \
         FROM {table} \
         WHERE 1=1"
    );
    // Parameter binding order matters — append filters in the same order we bind.
    let mut bind_index = 1;
    if student_id.is_some() {
        sql.push_str(&format!(" AND student_id = ${bind_index}"));
        bind_index += 1;
    }
    if class_room_id.is_some() {
        sql.push_str(&format!(" AND class_room_id = ${bind_index}"));
        bind_index += 1;
    }
    sql.push_str(&format!(
        " ORDER BY enrolled_at DESC LIMIT ${limit_pos} OFFSET ${offset_pos}",
        limit_pos = bind_index,
        offset_pos = bind_index + 1
    ));

    let mut q = sqlx::query(&sql);
    if let Some(sid) = student_id {
        q = q.bind(sid);
    }
    if let Some(cid) = class_room_id {
        q = q.bind(cid);
    }
    q = q.bind(limit).bind(offset);
    let rows = q.fetch_all(pool).await?;
    Ok(rows
        .into_iter()
        .map(|r| EnrollmentMvRow {
            student_id: r.try_get("student_id").unwrap_or_else(|_| Uuid::nil()),
            class_room_id: r.try_get("class_room_id").unwrap_or_else(|_| Uuid::nil()),
            enrolled_at: r.try_get("enrolled_at").unwrap_or_else(|_| Utc::now()),
            last_sortable_unique_id: r.try_get("last_sortable_unique_id").unwrap_or_default(),
        })
        .collect())
}

fn mv_disabled() -> Response {
    (
        StatusCode::SERVICE_UNAVAILABLE,
        Json(serde_json::json!({
            "error": "Materialized view Postgres is not configured. Set DCBMATERIALIZEDVIEWPOSTGRES_URI or ConnectionStrings__DcbMaterializedViewPostgres."
        })),
    )
        .into_response()
}

fn server_error(err: anyhow::Error) -> Response {
    tracing::error!(?err, "materialized view query failed");
    (
        StatusCode::INTERNAL_SERVER_ERROR,
        Json(serde_json::json!({ "error": err.to_string() })),
    )
        .into_response()
}
