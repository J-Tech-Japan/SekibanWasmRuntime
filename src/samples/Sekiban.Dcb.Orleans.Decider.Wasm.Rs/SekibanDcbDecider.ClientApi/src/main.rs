use std::{collections::HashMap, env, net::SocketAddr, sync::Arc};

use anyhow::Result;
use axum::{
    extract::{Path, Query as AxumQuery, State},
    http::StatusCode,
    response::{IntoResponse, Response},
    routing::{get, post, put},
    Json, Router,
};
use chrono::{DateTime, Duration, FixedOffset, NaiveDate, TimeZone, Utc};
use sekiban_core::prelude::*;
use sekiban_dcb_decider_rust_eventsource::{
    ApprovalDecision, ApprovalRequestState, ClassRoomItem, ClassRoomState, CommitReservationHold,
    ConfirmReservation, CountResult, CreateClassRoom, CreateReservationDraft, CreateRoom,
    CreateStudent, CreateWeatherForecast, GetApprovalInboxQuery, GetClassRoomListQuery,
    GetReservationListQuery, GetRoomListQuery, GetStudentListQuery, GetUserAccessListQuery,
    GetUserDirectoryListQuery, GetWeatherForecastCountQuery, GetWeatherForecastListQuery,
    RegisterUser, ReservationState, RoomState, StudentState, UpdateRoom,
    UpdateUserMonthlyReservationLimit, UpdateWeatherForecastLocation, WeatherForecastItem, ApprovalRequestTag,
    CancelReservation, ClassRoomTag, DeleteWeatherForecast, DropStudentFromClassRoom,
    EnrollStudentInClassRoom, RecordApprovalDecision,
    RejectReservation, ReservationTag, RoomTag, StartApprovalFlow, StudentTag,
};
use sekiban_executor::{
    CommandExecutionResult, ExecuteCommandError, RemoteSekibanExecutor, StaticTagProjectorResolver,
};
use serde::{de::DeserializeOwned, Deserialize, Serialize};
use serde_json::{json, Value};
use uuid::Uuid;

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
        StaticTagProjectorResolver::new()
            .with_tag_group("weather", "WeatherForecastProjector")
            .with_tag_group("Student", "StudentProjector")
            .with_tag_group("ClassRoom", "ClassRoomProjector")
            .with_tag_group("User", "UserDirectoryProjector")
            .with_tag_group("UserAccess", "UserAccessProjector")
            .with_tag_group("Room", "RoomProjector")
            .with_tag_group("Reservation", "ReservationProjector")
            .with_tag_group("ApprovalRequest", "ApprovalRequestProjector"),
    ));

    let app = Router::new()
        .route("/health", get(health))
        .route("/api/weatherforecast", get(get_forecasts).post(create_forecast))
        .route("/api/weatherforecast/count", get(get_forecast_count))
        .route("/api/weatherforecast/delete", post(delete_forecast))
        .route("/api/weatherforecast/update-location", post(update_location))
        .route("/api/students", get(get_students).post(create_student))
        .route("/api/students/:student_id", get(get_student))
        .route("/api/classrooms", get(get_classrooms).post(create_classroom))
        .route("/api/classrooms/:class_room_id", get(get_classroom))
        .route("/api/enrollments", get(get_enrollments))
        .route("/api/enrollments/add", post(enroll_student))
        .route("/api/enrollments/drop", post(drop_student))
        .route("/api/users", get(get_users))
        .route("/api/users/:user_id/monthly-limit", post(update_monthly_limit))
        .route("/api/rooms", get(get_rooms).post(create_room))
        .route("/api/rooms/:room_id", put(update_room))
        .route("/api/reservations", get(get_reservations))
        .route("/api/reservations/by-room/:room_id", get(get_reservations_by_room))
        .route("/api/reservations/draft", post(create_reservation_draft))
        .route("/api/reservations/quick", post(quick_reservation))
        .route("/api/reservations/:reservation_id/hold", post(commit_reservation_hold))
        .route("/api/reservations/:reservation_id/confirm", post(confirm_reservation))
        .route("/api/reservations/:reservation_id/cancel", post(cancel_reservation))
        .route("/api/reservations/:reservation_id/reject", post(reject_reservation))
        .route("/api/approvals", get(get_approval_inbox))
        .route("/api/approvals/:approval_request_id/decision", post(record_approval_decision))
        .route("/api/test-data/generate", post(generate_test_data))
        .route("/api/test-data/generate-rooms", post(generate_rooms_only))
        .route("/api/test-data/generate-reservations", post(generate_reservations_only))
        .with_state(AppState { executor });

    let addr = SocketAddr::from(([0, 0, 0, 0], port));
    tracing::info!(%addr, "Rust ClientApi listening");
    let listener = tokio::net::TcpListener::bind(addr).await?;
    axum::serve(listener, app).await?;
    Ok(())
}

async fn health() -> impl IntoResponse {
    Json(json!({ "message": "Sekiban decider Rust ClientApi is running" }))
}

#[derive(Debug, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
struct PagingQuery {
    page_number: Option<i32>,
    page_size: Option<i32>,
    wait_for_sortable_unique_id: Option<String>,
}

#[derive(Debug, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
struct ReservationListParams {
    page_number: Option<i32>,
    page_size: Option<i32>,
    wait_for_sortable_unique_id: Option<String>,
    room_id: Option<Uuid>,
}

#[derive(Debug, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
struct UsersQuery {
    page_number: Option<i32>,
    page_size: Option<i32>,
    wait_for_sortable_unique_id: Option<String>,
    active_only: Option<bool>,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct ApprovalQuery {
    page_number: Option<i32>,
    page_size: Option<i32>,
    wait_for_sortable_unique_id: Option<String>,
    pending_only: Option<bool>,
}

#[derive(Debug, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
struct WeatherListQueryParams {
    page_number: Option<i32>,
    page_size: Option<i32>,
    wait_for_sortable_id: Option<String>,
    location: Option<String>,
    location_filter: Option<String>,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct UpdateLocationRequest {
    forecast_id: Uuid,
    new_location: String,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct DeleteForecastRequest {
    forecast_id: Uuid,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct UpdateMonthlyLimitRequest {
    monthly_reservation_limit: i32,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct CreateRoomRequest {
    room_id: Option<Uuid>,
    name: String,
    capacity: i32,
    location: String,
    equipment: Vec<String>,
    requires_approval: bool,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct UpdateRoomRequest {
    name: String,
    capacity: i32,
    location: String,
    equipment: Vec<String>,
    requires_approval: bool,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct ReservationDraftRequest {
    reservation_id: Option<Uuid>,
    room_id: Uuid,
    organizer_id: Option<Uuid>,
    organizer_name: Option<String>,
    start_time: String,
    end_time: String,
    purpose: String,
    selected_equipment: Option<Vec<String>>,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct CommitReservationHoldRequest {
    room_id: Uuid,
    requires_approval: bool,
    approval_request_id: Option<Uuid>,
    approval_request_comment: Option<String>,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct ConfirmReservationRequest {
    room_id: Uuid,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct CancelReservationRequest {
    room_id: Uuid,
    reason: String,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct RejectReservationRequest {
    room_id: Uuid,
    approval_request_id: Uuid,
    reason: String,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct QuickReservationRequest {
    room_id: Uuid,
    start_time: String,
    end_time: String,
    purpose: String,
    selected_equipment: Option<Vec<String>>,
    approval_request_comment: Option<String>,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct ApprovalDecisionRequest {
    decision: ApprovalDecision,
    comment: Option<String>,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct TestDataQuery {
    time_zone_offset_minutes: Option<i32>,
    room_id: Option<Uuid>,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct CommandResponse {
    success: bool,
    error: Option<String>,
    sortable_unique_id: Option<String>,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct StudentCommandResponse {
    student_id: Uuid,
    event_id: Option<String>,
    sortable_unique_id: Option<String>,
    message: String,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct ClassRoomCommandResponse {
    class_room_id: Uuid,
    event_id: Option<String>,
    sortable_unique_id: Option<String>,
    message: String,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct EnrollmentCommandResponse {
    student_id: Uuid,
    class_room_id: Uuid,
    event_id: Option<String>,
    sortable_unique_id: Option<String>,
    message: String,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct RoomCommandResponse {
    success: bool,
    room_id: Uuid,
    event_id: Option<String>,
    sortable_unique_id: Option<String>,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct ReservationCommandResponse {
    success: bool,
    reservation_id: Uuid,
    organizer_id: Option<Uuid>,
    organizer_name: Option<String>,
    requires_approval: Option<bool>,
    approval_request_id: Option<Uuid>,
    sortable_unique_id: Option<String>,
    error: Option<String>,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct UserMonthlyLimitResponse {
    success: bool,
    user_id: Uuid,
    monthly_reservation_limit: i32,
    sortable_unique_id: Option<String>,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct ApprovalInboxViewItem {
    approval_request_id: Uuid,
    reservation_id: Uuid,
    room_id: Uuid,
    room_name: Option<String>,
    requester_id: Uuid,
    request_comment: Option<String>,
    organizer_id: Option<Uuid>,
    organizer_name: Option<String>,
    purpose: Option<String>,
    start_time: Option<String>,
    end_time: Option<String>,
    approver_ids: Vec<Uuid>,
    requested_at: String,
    status: String,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct TestDataGenerationResult {
    user_id: Uuid,
    user_name: String,
    rooms_created: usize,
    room_ids: Vec<Uuid>,
    reservations_created: usize,
    reservation_ids: Vec<Uuid>,
    errors: Vec<String>,
    sortable_unique_id: Option<String>,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct EnrollmentsViewItem {
    student_id: Uuid,
    student_name: String,
    class_room_id: Uuid,
    class_name: String,
    enrollment_date: String,
}

#[derive(Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
struct TagStateEnvelope<T> {
    student_id: Option<Uuid>,
    class_room_id: Option<Uuid>,
    payload: T,
    version: i32,
}

async fn get_forecasts(
    State(state): State<AppState>,
    AxumQuery(query): AxumQuery<WeatherListQueryParams>,
) -> Response {
    execute_list_query::<GetWeatherForecastListQuery, Vec<WeatherForecastItem>>(
        &state.executor,
        &GetWeatherForecastListQuery {
            location_filter: query.location_filter.or(query.location),
            forecast_id: None,
            wait_for_sortable_unique_id: query.wait_for_sortable_id,
            page_number: query.page_number,
            page_size: query.page_size,
        },
    )
    .await
}

async fn get_forecast_count(
    State(state): State<AppState>,
    AxumQuery(query): AxumQuery<WeatherListQueryParams>,
) -> Response {
    execute_query::<GetWeatherForecastCountQuery, CountResult>(
        &state.executor,
        &GetWeatherForecastCountQuery {
            location_filter: query.location_filter.or(query.location),
            forecast_id: None,
            wait_for_sortable_unique_id: query.wait_for_sortable_id,
        },
    )
    .await
}

async fn create_forecast(
    State(state): State<AppState>,
    Json(mut command): Json<CreateWeatherForecast>,
) -> Response {
    if command.forecast_id.is_none() {
        command.forecast_id = Some(Uuid::now_v7());
    }
    render_command(
        state.executor.execute_command(&command).await,
        |result| Json(json!({
            "success": result.success,
            "sortableUniqueId": result.sortable_unique_id,
            "forecastId": command.forecast_id,
            "error": if result.success { Value::Null } else { result.response_body }
        }))
        .into_response(),
    )
}

async fn update_location(
    State(state): State<AppState>,
    Json(request): Json<UpdateLocationRequest>,
) -> Response {
    let command = UpdateWeatherForecastLocation {
        forecast_id: request.forecast_id,
        new_location: request.new_location,
    };
    render_command(
        state.executor.execute_command(&command).await,
        |result| Json(json!({
            "success": result.success,
            "sortableUniqueId": result.sortable_unique_id,
            "forecastId": command.forecast_id,
            "error": if result.success { Value::Null } else { result.response_body }
        }))
        .into_response(),
    )
}

async fn delete_forecast(
    State(state): State<AppState>,
    Json(request): Json<DeleteForecastRequest>,
) -> Response {
    let command = DeleteWeatherForecast {
        forecast_id: request.forecast_id,
    };
    render_command(
        state.executor.execute_command(&command).await,
        |result| Json(json!({
            "success": result.success,
            "sortableUniqueId": result.sortable_unique_id,
            "forecastId": command.forecast_id,
            "error": if result.success { Value::Null } else { result.response_body }
        }))
        .into_response(),
    )
}

async fn get_students(
    State(state): State<AppState>,
    AxumQuery(query): AxumQuery<PagingQuery>,
) -> Response {
    execute_list_query::<GetStudentListQuery, Vec<StudentState>>(
        &state.executor,
        &GetStudentListQuery {
            page_number: query.page_number,
            page_size: query.page_size,
            wait_for_sortable_unique_id: query.wait_for_sortable_unique_id,
        },
    )
    .await
}

async fn get_student(
    State(state): State<AppState>,
    Path(student_id): Path<Uuid>,
) -> Response {
    match get_tag_state::<StudentState, StudentTag>(&state.executor, &StudentTag { student_id }).await {
        Ok((payload, version)) => Json(TagStateEnvelope {
            student_id: Some(student_id),
            class_room_id: None,
            payload,
            version,
        })
        .into_response(),
        Err(err) => error_response(StatusCode::BAD_GATEWAY, err),
    }
}

async fn create_student(
    State(state): State<AppState>,
    Json(command): Json<CreateStudent>,
) -> Response {
    render_command(
        state.executor.execute_command(&command).await,
        |result| Json(StudentCommandResponse {
            student_id: command.student_id,
            event_id: extract_event_id(&result.response_body),
            sortable_unique_id: result.sortable_unique_id,
            message: "Student created successfully".to_string(),
        })
        .into_response(),
    )
}

async fn get_classrooms(
    State(state): State<AppState>,
    AxumQuery(query): AxumQuery<PagingQuery>,
) -> Response {
    execute_list_query::<GetClassRoomListQuery, Vec<ClassRoomItem>>(
        &state.executor,
        &GetClassRoomListQuery {
            page_number: query.page_number,
            page_size: query.page_size,
            wait_for_sortable_unique_id: query.wait_for_sortable_unique_id,
        },
    )
    .await
}

async fn get_classroom(
    State(state): State<AppState>,
    Path(class_room_id): Path<Uuid>,
) -> Response {
    match get_tag_state::<ClassRoomState, ClassRoomTag>(&state.executor, &ClassRoomTag { class_room_id }).await {
        Ok((payload, version)) => Json(TagStateEnvelope {
            student_id: None,
            class_room_id: Some(class_room_id),
            payload,
            version,
        })
        .into_response(),
        Err(err) => error_response(StatusCode::BAD_GATEWAY, err),
    }
}

async fn create_classroom(
    State(state): State<AppState>,
    Json(command): Json<CreateClassRoom>,
) -> Response {
    render_command(
        state.executor.execute_command(&command).await,
        |result| Json(ClassRoomCommandResponse {
            class_room_id: command.class_room_id,
            event_id: extract_event_id(&result.response_body),
            sortable_unique_id: result.sortable_unique_id,
            message: "ClassRoom created successfully".to_string(),
        })
        .into_response(),
    )
}

async fn get_enrollments(
    State(state): State<AppState>,
    AxumQuery(query): AxumQuery<PagingQuery>,
) -> Response {
    let students = match state
        .executor
        .execute_list_query::<GetStudentListQuery, Vec<StudentState>>(&GetStudentListQuery {
            page_number: None,
            page_size: None,
            wait_for_sortable_unique_id: query.wait_for_sortable_unique_id.clone(),
        })
        .await
    {
        Ok(items) => items,
        Err(err) => return error_response(StatusCode::BAD_GATEWAY, err),
    };

    let classrooms = match state
        .executor
        .execute_list_query::<GetClassRoomListQuery, Vec<ClassRoomItem>>(&GetClassRoomListQuery {
            page_number: None,
            page_size: None,
            wait_for_sortable_unique_id: query.wait_for_sortable_unique_id,
        })
        .await
    {
        Ok(items) => items,
        Err(err) => return error_response(StatusCode::BAD_GATEWAY, err),
    };

    let classrooms_by_id = classrooms
        .into_iter()
        .map(|item| (item.class_room_id, item))
        .collect::<HashMap<_, _>>();
    let mut enrollments = Vec::new();
    for student in students {
        for class_room_id in student.enrolled_class_room_ids {
            if let Some(classroom) = classrooms_by_id.get(&class_room_id) {
                enrollments.push(EnrollmentsViewItem {
                    student_id: student.student_id,
                    student_name: student.name.clone(),
                    class_room_id,
                    class_name: classroom.name.clone(),
                    enrollment_date: Utc::now().to_rfc3339(),
                });
            }
        }
    }

    Json(enrollments).into_response()
}

async fn enroll_student(
    State(state): State<AppState>,
    Json(command): Json<EnrollStudentInClassRoom>,
) -> Response {
    render_command(
        state.executor.execute_command(&command).await,
        |result| Json(EnrollmentCommandResponse {
            student_id: command.student_id,
            class_room_id: command.class_room_id,
            event_id: extract_event_id(&result.response_body),
            sortable_unique_id: result.sortable_unique_id,
            message: "Student enrolled successfully".to_string(),
        })
        .into_response(),
    )
}

async fn drop_student(
    State(state): State<AppState>,
    Json(command): Json<DropStudentFromClassRoom>,
) -> Response {
    render_command(
        state.executor.execute_command(&command).await,
        |result| Json(EnrollmentCommandResponse {
            student_id: command.student_id,
            class_room_id: command.class_room_id,
            event_id: extract_event_id(&result.response_body),
            sortable_unique_id: result.sortable_unique_id,
            message: "Student dropped successfully".to_string(),
        })
        .into_response(),
    )
}

async fn get_users(
    State(state): State<AppState>,
    AxumQuery(query): AxumQuery<UsersQuery>,
) -> Response {
    let users = match state
        .executor
        .execute_list_query::<GetUserDirectoryListQuery, Vec<sekiban_dcb_decider_rust_eventsource::UserDirectoryListItem>>(
            &GetUserDirectoryListQuery {
                page_number: query.page_number,
                page_size: query.page_size,
                wait_for_sortable_unique_id: query.wait_for_sortable_unique_id.clone(),
                active_only: query.active_only.unwrap_or(false),
            },
        )
        .await
    {
        Ok(items) => items,
        Err(err) => return error_response(StatusCode::BAD_GATEWAY, err),
    };

    let accesses = match state
        .executor
        .execute_list_query::<GetUserAccessListQuery, Vec<sekiban_dcb_decider_rust_eventsource::UserAccessListItem>>(
            &GetUserAccessListQuery {
                page_number: None,
                page_size: None,
                wait_for_sortable_unique_id: query.wait_for_sortable_unique_id,
                active_only: false,
                role_filter: None,
            },
        )
        .await
    {
        Ok(items) => items,
        Err(err) => return error_response(StatusCode::BAD_GATEWAY, err),
    };

    let roles_by_user = accesses
        .into_iter()
        .map(|item| (item.user_id, item.roles))
        .collect::<HashMap<_, _>>();
    let enriched = users
        .into_iter()
        .map(|mut user| {
            user.roles = roles_by_user
                .get(&user.user_id)
                .cloned()
                .unwrap_or_default();
            user
        })
        .collect::<Vec<_>>();
    Json(enriched).into_response()
}

async fn update_monthly_limit(
    State(state): State<AppState>,
    Path(user_id): Path<Uuid>,
    Json(request): Json<UpdateMonthlyLimitRequest>,
) -> Response {
    let command = UpdateUserMonthlyReservationLimit {
        user_id,
        monthly_reservation_limit: request.monthly_reservation_limit,
    };
    render_command(
        state.executor.execute_command(&command).await,
        |result| Json(UserMonthlyLimitResponse {
            success: result.success,
            user_id,
            monthly_reservation_limit: request.monthly_reservation_limit,
            sortable_unique_id: result.sortable_unique_id,
        })
        .into_response(),
    )
}

async fn get_rooms(
    State(state): State<AppState>,
    AxumQuery(query): AxumQuery<PagingQuery>,
) -> Response {
    execute_list_query::<GetRoomListQuery, Vec<sekiban_dcb_decider_rust_eventsource::RoomListItem>>(
        &state.executor,
        &GetRoomListQuery {
            page_number: query.page_number,
            page_size: query.page_size,
            wait_for_sortable_unique_id: query.wait_for_sortable_unique_id,
        },
    )
    .await
}

async fn create_room(
    State(state): State<AppState>,
    Json(request): Json<CreateRoomRequest>,
) -> Response {
    let room_id = request.room_id.unwrap_or_else(Uuid::now_v7);
    let command = CreateRoom {
        room_id,
        name: request.name,
        capacity: request.capacity,
        location: request.location,
        equipment: request.equipment,
        requires_approval: request.requires_approval,
    };
    render_command(
        state.executor.execute_command(&command).await,
        |result| Json(RoomCommandResponse {
            success: result.success,
            room_id,
            event_id: extract_event_id(&result.response_body),
            sortable_unique_id: result.sortable_unique_id,
        })
        .into_response(),
    )
}

async fn update_room(
    State(state): State<AppState>,
    Path(room_id): Path<Uuid>,
    Json(request): Json<UpdateRoomRequest>,
) -> Response {
    let command = UpdateRoom {
        room_id,
        name: request.name,
        capacity: request.capacity,
        location: request.location,
        equipment: request.equipment,
        requires_approval: request.requires_approval,
    };
    render_command(
        state.executor.execute_command(&command).await,
        |result| Json(RoomCommandResponse {
            success: result.success,
            room_id,
            event_id: extract_event_id(&result.response_body),
            sortable_unique_id: result.sortable_unique_id,
        })
        .into_response(),
    )
}

async fn get_reservations(
    State(state): State<AppState>,
    AxumQuery(query): AxumQuery<ReservationListParams>,
) -> Response {
    reservations_response(&state.executor, query.room_id, query).await
}

async fn get_reservations_by_room(
    State(state): State<AppState>,
    Path(room_id): Path<Uuid>,
    AxumQuery(query): AxumQuery<ReservationListParams>,
) -> Response {
    reservations_response(&state.executor, Some(room_id), query).await
}

async fn create_reservation_draft(
    State(state): State<AppState>,
    Json(request): Json<ReservationDraftRequest>,
) -> Response {
    let reservation_id = request.reservation_id.unwrap_or_else(Uuid::now_v7);
    let organizer_id = request.organizer_id.unwrap_or_else(Uuid::now_v7);
    let organizer_name = request
        .organizer_name
        .unwrap_or_else(|| "Sample User".to_string());
    let command = CreateReservationDraft {
        reservation_id,
        room_id: request.room_id,
        organizer_id,
        organizer_name: organizer_name.clone(),
        start_time: request.start_time,
        end_time: request.end_time,
        purpose: request.purpose,
        selected_equipment: request.selected_equipment.unwrap_or_default(),
    };
    render_command(
        state.executor.execute_command(&command).await,
        |result| Json(ReservationCommandResponse {
            success: result.success,
            reservation_id,
            organizer_id: Some(organizer_id),
            organizer_name: Some(organizer_name),
            requires_approval: Some(false),
            approval_request_id: None,
            sortable_unique_id: result.sortable_unique_id,
            error: None,
        })
        .into_response(),
    )
}

async fn quick_reservation(
    State(state): State<AppState>,
    Json(request): Json<QuickReservationRequest>,
) -> Response {
    let room = match get_tag_state::<RoomState, RoomTag>(&state.executor, &RoomTag { room_id: request.room_id }).await {
        Ok((room, _)) if !room.is_empty() => room,
        Ok(_) => return error_response(StatusCode::BAD_REQUEST, "Room not found"),
        Err(err) => return error_response(StatusCode::BAD_GATEWAY, err),
    };

    let reservation_id = Uuid::now_v7();
    let organizer_id = Uuid::now_v7();
    let organizer_name = "Sample User".to_string();
    let selected_equipment = request.selected_equipment.unwrap_or_default();

    let draft = CreateReservationDraft {
        reservation_id,
        room_id: request.room_id,
        organizer_id,
        organizer_name: organizer_name.clone(),
        start_time: request.start_time,
        end_time: request.end_time,
        purpose: request.purpose,
        selected_equipment,
    };
    if let Err(response) = ensure_command_success(state.executor.execute_command(&draft).await, reservation_id) {
        return response;
    }

    let approval_request_id = if room.requires_approval {
        let approval_request_id = Uuid::now_v7();
        let start_approval = StartApprovalFlow {
            approval_request_id,
            reservation_id,
            room_id: room.room_id,
            requester_id: organizer_id,
            approver_ids: Vec::new(),
            request_comment: request.approval_request_comment.clone(),
        };
        if let Err(response) = ensure_command_success(state.executor.execute_command(&start_approval).await, reservation_id) {
            return response;
        }
        Some(approval_request_id)
    } else {
        None
    };

    let hold = CommitReservationHold {
        reservation_id,
        room_id: room.room_id,
        requires_approval: room.requires_approval,
        approval_request_id,
        approval_request_comment: request.approval_request_comment,
    };
    let hold_result = match state.executor.execute_command(&hold).await {
        Ok(result) if result.success => result,
        Ok(result) => return failed_reservation_command_response(reservation_id, result),
        Err(err) => return command_error_response(reservation_id, err),
    };

    let sortable_unique_id = if room.requires_approval {
        hold_result.sortable_unique_id
    } else {
        let confirm = ConfirmReservation {
            reservation_id,
            room_id: room.room_id,
        };
        match state.executor.execute_command(&confirm).await {
            Ok(result) if result.success => result.sortable_unique_id.or(hold_result.sortable_unique_id),
            Ok(result) => return failed_reservation_command_response(reservation_id, result),
            Err(err) => return command_error_response(reservation_id, err),
        }
    };

    Json(ReservationCommandResponse {
        success: true,
        reservation_id,
        organizer_id: Some(organizer_id),
        organizer_name: Some(organizer_name),
        requires_approval: Some(room.requires_approval),
        approval_request_id,
        sortable_unique_id,
        error: None,
    })
    .into_response()
}

async fn commit_reservation_hold(
    State(state): State<AppState>,
    Path(reservation_id): Path<Uuid>,
    Json(request): Json<CommitReservationHoldRequest>,
) -> Response {
    let command = CommitReservationHold {
        reservation_id,
        room_id: request.room_id,
        requires_approval: request.requires_approval,
        approval_request_id: request.approval_request_id,
        approval_request_comment: request.approval_request_comment,
    };
    render_command(
        state.executor.execute_command(&command).await,
        |result| Json(ReservationCommandResponse {
            success: result.success,
            reservation_id,
            organizer_id: None,
            organizer_name: None,
            requires_approval: Some(request.requires_approval),
            approval_request_id: request.approval_request_id,
            sortable_unique_id: result.sortable_unique_id,
            error: None,
        })
        .into_response(),
    )
}

async fn confirm_reservation(
    State(state): State<AppState>,
    Path(reservation_id): Path<Uuid>,
    Json(request): Json<ConfirmReservationRequest>,
) -> Response {
    let command = ConfirmReservation {
        reservation_id,
        room_id: request.room_id,
    };
    render_command(
        state.executor.execute_command(&command).await,
        |result| Json(ReservationCommandResponse {
            success: result.success,
            reservation_id,
            organizer_id: None,
            organizer_name: None,
            requires_approval: None,
            approval_request_id: None,
            sortable_unique_id: result.sortable_unique_id,
            error: None,
        })
        .into_response(),
    )
}

async fn cancel_reservation(
    State(state): State<AppState>,
    Path(reservation_id): Path<Uuid>,
    Json(request): Json<CancelReservationRequest>,
) -> Response {
    let command = CancelReservation {
        reservation_id,
        room_id: request.room_id,
        reason: request.reason,
    };
    render_command(
        state.executor.execute_command(&command).await,
        |result| Json(ReservationCommandResponse {
            success: result.success,
            reservation_id,
            organizer_id: None,
            organizer_name: None,
            requires_approval: None,
            approval_request_id: None,
            sortable_unique_id: result.sortable_unique_id,
            error: None,
        })
        .into_response(),
    )
}

async fn reject_reservation(
    State(state): State<AppState>,
    Path(reservation_id): Path<Uuid>,
    Json(request): Json<RejectReservationRequest>,
) -> Response {
    let command = RejectReservation {
        reservation_id,
        room_id: request.room_id,
        approval_request_id: request.approval_request_id,
        reason: request.reason,
    };
    render_command(
        state.executor.execute_command(&command).await,
        |result| Json(ReservationCommandResponse {
            success: result.success,
            reservation_id,
            organizer_id: None,
            organizer_name: None,
            requires_approval: Some(true),
            approval_request_id: Some(request.approval_request_id),
            sortable_unique_id: result.sortable_unique_id,
            error: None,
        })
        .into_response(),
    )
}

async fn get_approval_inbox(
    State(state): State<AppState>,
    AxumQuery(query): AxumQuery<ApprovalQuery>,
) -> Response {
    let items = match state
        .executor
        .execute_list_query::<GetApprovalInboxQuery, Vec<sekiban_dcb_decider_rust_eventsource::ApprovalInboxItem>>(
            &GetApprovalInboxQuery {
                page_number: query.page_number,
                page_size: query.page_size,
                wait_for_sortable_unique_id: query.wait_for_sortable_unique_id.clone(),
                pending_only: query.pending_only.unwrap_or(true),
            },
        )
        .await
    {
        Ok(items) => items,
        Err(err) => return error_response(StatusCode::BAD_GATEWAY, err),
    };

    let mut views = Vec::with_capacity(items.len());
    for item in items {
        let room_name = get_tag_state::<RoomState, RoomTag>(&state.executor, &RoomTag { room_id: item.room_id })
            .await
            .ok()
            .map(|(room, _)| room.name)
            .filter(|name| !name.is_empty());

        let reservation = get_tag_state::<ReservationState, ReservationTag>(
            &state.executor,
            &ReservationTag {
                reservation_id: item.reservation_id,
            },
        )
        .await
        .ok()
        .map(|(reservation, _)| reservation);

        let organizer_id = reservation.as_ref().map(|reservation| reservation.organizer_id);
        let organizer_name = reservation
            .as_ref()
            .map(|reservation| reservation.organizer_name.clone());
        let purpose = reservation.as_ref().map(|reservation| reservation.purpose.clone());
        let start_time = reservation.as_ref().map(|reservation| reservation.start_time.clone());
        let end_time = reservation.as_ref().map(|reservation| reservation.end_time.clone());
        let mut status = item.status.clone();
        if let Some(reservation) = reservation {
            if matches!(reservation.status.as_str(), "Cancelled" | "Rejected") && status == "Pending" {
                status = "Cancelled".to_string();
            }
        }
        if query.pending_only.unwrap_or(true) && status != "Pending" {
            continue;
        }

        views.push(ApprovalInboxViewItem {
            approval_request_id: item.approval_request_id,
            reservation_id: item.reservation_id,
            room_id: item.room_id,
            room_name,
            requester_id: item.requester_id,
            request_comment: item.request_comment,
            organizer_id,
            organizer_name,
            purpose,
            start_time,
            end_time,
            approver_ids: item.approver_ids,
            requested_at: item.requested_at,
            status,
        });
    }

    Json(views).into_response()
}

async fn record_approval_decision(
    State(state): State<AppState>,
    Path(approval_request_id): Path<Uuid>,
    Json(request): Json<ApprovalDecisionRequest>,
) -> Response {
    let (approval_state, _) = match get_tag_state::<ApprovalRequestState, ApprovalRequestTag>(
        &state.executor,
        &ApprovalRequestTag { approval_request_id },
    )
    .await
    {
        Ok(state) if !state.0.is_empty() => state,
        Ok(_) => return error_response(StatusCode::BAD_REQUEST, "Approval request is not pending"),
        Err(err) => return error_response(StatusCode::BAD_GATEWAY, err),
    };

    if approval_state.status != "Pending" {
        return error_response(StatusCode::BAD_REQUEST, "Approval request is not pending");
    }

    let approver_id = Uuid::now_v7();
    let record = RecordApprovalDecision {
        approval_request_id,
        reservation_id: approval_state.reservation_id,
        approver_id,
        decision: request.decision.clone(),
        comment: request.comment.clone(),
    };
    let record_result = match state.executor.execute_command(&record).await {
        Ok(result) if result.success => result,
        Ok(result) => {
            return (
                StatusCode::BAD_REQUEST,
                Json(json!({
                    "success": false,
                    "approvalRequestId": approval_request_id,
                    "reservationId": approval_state.reservation_id,
                    "error": result.response_body,
                })),
            )
                .into_response()
        }
        Err(err) => return error_response(StatusCode::BAD_REQUEST, err),
    };

    let reservation_result = match request.decision {
        ApprovalDecision::Approved => state
            .executor
            .execute_command(&ConfirmReservation {
                reservation_id: approval_state.reservation_id,
                room_id: approval_state.room_id,
            })
            .await,
        ApprovalDecision::Rejected => state
            .executor
            .execute_command(&RejectReservation {
                reservation_id: approval_state.reservation_id,
                room_id: approval_state.room_id,
                approval_request_id,
                reason: request.comment.clone().unwrap_or_else(|| "Rejected".to_string()),
            })
            .await,
    };

    let reservation_sortable_unique_id = match reservation_result {
        Ok(result) if result.success => result.sortable_unique_id,
        Ok(result) => return error_response(StatusCode::BAD_REQUEST, result.response_body),
        Err(err) => return error_response(StatusCode::BAD_REQUEST, err),
    };

    Json(json!({
        "success": true,
        "approvalRequestId": approval_request_id,
        "reservationId": approval_state.reservation_id,
        "decision": match request.decision {
            ApprovalDecision::Approved => "Approved",
            ApprovalDecision::Rejected => "Rejected",
        },
        "sortableUniqueId": record_result.sortable_unique_id,
        "reservationSortableUniqueId": reservation_sortable_unique_id,
    }))
    .into_response()
}

async fn generate_test_data(
    State(state): State<AppState>,
    AxumQuery(query): AxumQuery<TestDataQuery>,
) -> Response {
    let (user_id, user_name) = match generate_user(&state.executor).await {
        Ok(value) => value,
        Err(err) => return error_response(StatusCode::BAD_REQUEST, err),
    };

    let rooms = match generate_rooms(&state.executor).await {
        Ok(value) => value,
        Err(err) => return error_response(StatusCode::BAD_REQUEST, err),
    };

    let (reservation_ids, errors) = match generate_reservations(
        &state.executor,
        &rooms,
        user_id,
        &user_name,
        query.time_zone_offset_minutes,
    )
    .await
    {
        Ok(value) => value,
        Err(err) => return error_response(StatusCode::BAD_REQUEST, err),
    };

    Json(TestDataGenerationResult {
        user_id,
        user_name,
        rooms_created: rooms.len(),
        room_ids: rooms,
        reservations_created: reservation_ids.len(),
        reservation_ids,
        errors,
        sortable_unique_id: None,
    })
    .into_response()
}

async fn generate_rooms_only(State(state): State<AppState>) -> Response {
    match generate_rooms(&state.executor).await {
        Ok(room_ids) => Json(json!({
            "roomsCreated": room_ids.len(),
            "roomIds": room_ids,
        }))
        .into_response(),
        Err(err) => error_response(StatusCode::BAD_REQUEST, err),
    }
}

async fn generate_reservations_only(
    State(state): State<AppState>,
    AxumQuery(query): AxumQuery<TestDataQuery>,
) -> Response {
    let (user_id, user_name) = match generate_user(&state.executor).await {
        Ok(value) => value,
        Err(err) => return error_response(StatusCode::BAD_REQUEST, err),
    };

    let room_ids = if let Some(room_id) = query.room_id {
        vec![room_id]
    } else {
        match generate_rooms(&state.executor).await {
            Ok(value) => value,
            Err(err) => return error_response(StatusCode::BAD_REQUEST, err),
        }
    };

    let (reservation_ids, errors) = match generate_reservations(
        &state.executor,
        &room_ids,
        user_id,
        &user_name,
        query.time_zone_offset_minutes,
    )
    .await
    {
        Ok(value) => value,
        Err(err) => return error_response(StatusCode::BAD_REQUEST, err),
    };

    Json(json!({
        "reservationsCreated": reservation_ids.len(),
        "reservationIds": reservation_ids,
        "errors": errors,
    }))
    .into_response()
}

async fn reservations_response(
    executor: &RemoteSekibanExecutor,
    room_id: Option<Uuid>,
    query: ReservationListParams,
) -> Response {
    execute_list_query::<GetReservationListQuery, Vec<sekiban_dcb_decider_rust_eventsource::ReservationListItem>>(
        executor,
        &GetReservationListQuery {
            page_number: query.page_number,
            page_size: query.page_size,
            wait_for_sortable_unique_id: query.wait_for_sortable_unique_id,
            room_id: room_id.or(query.room_id),
        },
    )
    .await
}

async fn execute_query<Q, R>(executor: &RemoteSekibanExecutor, query: &Q) -> Response
where
    Q: Query + Serialize + Send + Sync,
    R: DeserializeOwned + Serialize,
{
    match executor.execute_query::<Q, R>(query).await {
        Ok(result) => Json(result).into_response(),
        Err(err) => error_response(StatusCode::BAD_GATEWAY, err),
    }
}

async fn execute_list_query<Q, R>(executor: &RemoteSekibanExecutor, query: &Q) -> Response
where
    Q: ListQuery + Serialize + Send + Sync,
    R: DeserializeOwned + Serialize,
{
    match executor.execute_list_query::<Q, R>(query).await {
        Ok(result) => Json(result).into_response(),
        Err(err) => error_response(StatusCode::BAD_GATEWAY, err),
    }
}

async fn get_tag_state<S, T>(
    executor: &RemoteSekibanExecutor,
    tag: &T,
) -> Result<(S, i32), CommandError>
where
    S: StatePayload,
    T: Tag,
{
    executor.command_context().get_state(tag).await
}

fn render_command<F>(
    result: Result<CommandExecutionResult, ExecuteCommandError>,
    on_success: F,
) -> Response
where
    F: FnOnce(CommandExecutionResult) -> Response,
{
    match result {
        Ok(result) if result.success => on_success(result),
        Ok(result) => {
            let error = extract_error_message(&result.response_body);
            (
                StatusCode::BAD_REQUEST,
                Json(CommandResponse {
                    success: false,
                    error: Some(error),
                    sortable_unique_id: result.sortable_unique_id,
                }),
            )
                .into_response()
        }
        Err(err) => error_response(StatusCode::BAD_REQUEST, err),
    }
}

fn ensure_command_success(
    result: Result<CommandExecutionResult, ExecuteCommandError>,
    reservation_id: Uuid,
) -> Result<(), Response> {
    match result {
        Ok(result) if result.success => Ok(()),
        Ok(result) => Err(failed_reservation_command_response(reservation_id, result)),
        Err(err) => Err(command_error_response(reservation_id, err)),
    }
}

fn failed_reservation_command_response(
    reservation_id: Uuid,
    result: CommandExecutionResult,
) -> Response {
    (
        StatusCode::BAD_REQUEST,
        Json(ReservationCommandResponse {
            success: false,
            reservation_id,
            organizer_id: None,
            organizer_name: None,
            requires_approval: None,
            approval_request_id: None,
            sortable_unique_id: result.sortable_unique_id,
            error: Some(extract_error_message(&result.response_body)),
        }),
    )
        .into_response()
}

fn command_error_response(reservation_id: Uuid, err: ExecuteCommandError) -> Response {
    (
        StatusCode::BAD_REQUEST,
        Json(ReservationCommandResponse {
            success: false,
            reservation_id,
            organizer_id: None,
            organizer_name: None,
            requires_approval: None,
            approval_request_id: None,
            sortable_unique_id: None,
            error: Some(err.to_string()),
        }),
    )
        .into_response()
}

fn error_response(status: StatusCode, error: impl ToString) -> Response {
    (status, Json(json!({ "error": error.to_string() }))).into_response()
}

fn extract_error_message(value: &Value) -> String {
    value
        .get("error")
        .and_then(Value::as_str)
        .map(ToOwned::to_owned)
        .unwrap_or_else(|| value.to_string())
}

fn extract_event_id(value: &Value) -> Option<String> {
    value.get("eventId").and_then(Value::as_str).map(ToOwned::to_owned)
}

async fn generate_user(executor: &RemoteSekibanExecutor) -> Result<(Uuid, String), String> {
    let user_id = Uuid::now_v7();
    let user_name = "Sample User".to_string();
    let email = format!("sample.user.{}@example.com", &user_id.to_string()[..8]);

    executor
        .execute_command(&RegisterUser {
            user_id,
            display_name: user_name.clone(),
            email,
            department: Some("Engineering".to_string()),
            monthly_reservation_limit: 10,
        })
        .await
        .map_err(|err| err.to_string())?;

    let _ = executor
        .execute_command(&sekiban_dcb_decider_rust_eventsource::GrantUserAccess {
            user_id,
            initial_role: "Admin".to_string(),
        })
        .await;

    Ok((user_id, user_name))
}

async fn generate_rooms(executor: &RemoteSekibanExecutor) -> Result<Vec<Uuid>, String> {
    let room_definitions = vec![
        ("Conference Room A", 20, "Building 1, Floor 2", vec!["Projector", "Whiteboard", "Video Conference"], false),
        ("Meeting Room B", 8, "Building 1, Floor 3", vec!["TV Screen", "Whiteboard"], false),
        ("Executive Boardroom", 16, "Building 2, Floor 5", vec!["Projector", "Video Conference", "Sound System", "Recording"], true),
        ("Huddle Space 1", 4, "Building 1, Floor 1", vec!["TV Screen"], false),
        ("Training Room", 30, "Building 3, Floor 1", vec!["Projector", "Multiple Screens", "Recording", "Microphones"], true),
        ("Small Meeting Room C", 6, "Building 1, Floor 2", vec!["Whiteboard"], false),
    ];

    let mut room_ids = Vec::new();
    for (name, capacity, location, equipment, requires_approval) in room_definitions {
        let room_id = Uuid::now_v7();
        executor
            .execute_command(&CreateRoom {
                room_id,
                name: name.to_string(),
                capacity,
                location: location.to_string(),
                equipment: equipment.into_iter().map(str::to_string).collect(),
                requires_approval,
            })
            .await
            .map_err(|err| err.to_string())?;
        room_ids.push(room_id);
    }

    Ok(room_ids)
}

async fn generate_reservations(
    executor: &RemoteSekibanExecutor,
    room_ids: &[Uuid],
    organizer_id: Uuid,
    organizer_name: &str,
    time_zone_offset_minutes: Option<i32>,
) -> Result<(Vec<Uuid>, Vec<String>), String> {
    let (base_date, offset) = resolve_local_base_date(time_zone_offset_minutes)?;
    let base_date = base_date + Duration::days(1);

    let reservation_defs = vec![
        (0usize, 0i64, 9u32, 10u32, "Team Standup"),
        (0usize, 0i64, 14u32, 16u32, "Sprint Planning"),
        (1usize, 1i64, 10u32, 11u32, "1:1 Meeting"),
        (2usize, 1i64, 13u32, 15u32, "Board Meeting"),
        (4usize, 3i64, 9u32, 17u32, "All-hands Training"),
    ];

    let mut reservation_ids = Vec::new();
    let mut errors = Vec::new();

    for (room_index, days_offset, start_hour, end_hour, purpose) in reservation_defs {
        if room_index >= room_ids.len() {
            continue;
        }

        let room_id = room_ids[room_index];
        let start_time = base_date
            .checked_add_signed(Duration::days(days_offset))
            .and_then(|date| date.and_hms_opt(start_hour, 0, 0))
            .ok_or_else(|| "failed to calculate start time".to_string())?;
        let end_time = base_date
            .checked_add_signed(Duration::days(days_offset))
            .and_then(|date| date.and_hms_opt(end_hour, 0, 0))
            .ok_or_else(|| "failed to calculate end time".to_string())?;

        let start_time = offset
            .from_local_datetime(&start_time)
            .single()
            .ok_or_else(|| "failed to create local start time".to_string())?
            .with_timezone(&Utc)
            .to_rfc3339();
        let end_time = offset
            .from_local_datetime(&end_time)
            .single()
            .ok_or_else(|| "failed to create local end time".to_string())?
            .with_timezone(&Utc)
            .to_rfc3339();

        let room = match get_tag_state::<RoomState, RoomTag>(executor, &RoomTag { room_id }).await {
            Ok((room, _)) => room,
            Err(err) => {
                errors.push(format!("Failed to load room for '{purpose}': {err}"));
                continue;
            }
        };
        let reservation_id = Uuid::now_v7();

        let draft = CreateReservationDraft {
            reservation_id,
            room_id,
            organizer_id,
            organizer_name: organizer_name.to_string(),
            start_time,
            end_time,
            purpose: purpose.to_string(),
            selected_equipment: Vec::new(),
        };

        if let Err(err) = executor.execute_command(&draft).await {
            errors.push(format!("Failed to create reservation '{purpose}': {err}"));
            continue;
        }

        let approval_request_id = if room.requires_approval {
            let approval_request_id = Uuid::now_v7();
            if let Err(err) = executor
                .execute_command(&StartApprovalFlow {
                    approval_request_id,
                    reservation_id,
                    room_id,
                    requester_id: organizer_id,
                    approver_ids: Vec::new(),
                    request_comment: None,
                })
                .await
            {
                errors.push(format!("Failed to start approval flow '{purpose}': {err}"));
                continue;
            }
            Some(approval_request_id)
        } else {
            None
        };

        if let Err(err) = executor
            .execute_command(&CommitReservationHold {
                reservation_id,
                room_id,
                requires_approval: room.requires_approval,
                approval_request_id,
                approval_request_comment: None,
            })
            .await
        {
            errors.push(format!("Failed to hold reservation '{purpose}': {err}"));
            continue;
        }

        if !room.requires_approval {
            if let Err(err) = executor
                .execute_command(&ConfirmReservation {
                    reservation_id,
                    room_id,
                })
                .await
            {
                errors.push(format!("Failed to confirm reservation '{purpose}': {err}"));
                continue;
            }
        }

        reservation_ids.push(reservation_id);
    }

    Ok((reservation_ids, errors))
}

fn resolve_local_base_date(time_zone_offset_minutes: Option<i32>) -> Result<(NaiveDate, FixedOffset), String> {
    let offset = FixedOffset::east_opt(time_zone_offset_minutes.unwrap_or(0) * 60)
        .ok_or_else(|| "invalid time zone offset".to_string())?;
    let now_local: DateTime<FixedOffset> = Utc::now().with_timezone(&offset);
    Ok((now_local.date_naive(), offset))
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
