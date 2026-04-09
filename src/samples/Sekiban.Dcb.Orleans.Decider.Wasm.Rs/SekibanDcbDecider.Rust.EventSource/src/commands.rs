use async_trait::async_trait;
use chrono::Utc;
use sekiban_core::prelude::*;
use sekiban_derive::Command;
use serde::{Deserialize, Serialize};
use uuid::Uuid;

use crate::events::*;
use crate::states::*;
use crate::tags::*;

fn single_output<E: EventPayload, T: Tag>(
    event: E,
    tag: T,
    version: Option<i32>,
) -> Result<Option<CommandOutput>, CommandError> {
    let mut output = CommandOutput::single(event, tag.clone())
        .map_err(|err| CommandError::Serialization(err.to_string()))?;
    if let Some(version) = version {
        output = output.with_expected_version(tag, version);
    }
    Ok(Some(output))
}

fn multi_tag_output<E: EventPayload>(
    event: E,
    tags: Vec<String>,
    expected_versions: Vec<(String, i32)>,
) -> Result<Option<CommandOutput>, CommandError> {
    Ok(Some(CommandOutput {
        events: vec![EventOutput {
            event_type: E::EVENT_TYPE.to_string(),
            payload: serde_json::to_string(&event)
                .map_err(|err| CommandError::Serialization(err.to_string()))?,
        }],
        consistency_tags: tags.clone(),
        tags,
        expected_versions: expected_versions.into_iter().collect(),
    }))
}

fn multi_event_output(
    events: Vec<EventOutput>,
    tags: Vec<String>,
    consistency_tags: Vec<String>,
    expected_versions: Vec<(String, i32)>,
) -> Result<Option<CommandOutput>, CommandError> {
    Ok(Some(CommandOutput {
        events,
        consistency_tags,
        tags,
        expected_versions: expected_versions.into_iter().collect(),
    }))
}

#[derive(Debug, Clone, Serialize, Deserialize, Command)]
#[serde(rename_all = "camelCase")]
pub struct CreateWeatherForecast {
    pub forecast_id: Option<Uuid>,
    pub location: String,
    pub date: String,
    pub temperature_c: i32,
    pub summary: String,
}

#[async_trait]
impl CommandHandler for CreateWeatherForecast {
    async fn handle<C: CommandContext + ?Sized>(&self, ctx: &C) -> Result<Option<CommandOutput>, CommandError> {
        let forecast_id = self.forecast_id.unwrap_or_else(Uuid::now_v7);
        let tag = WeatherForecastTag::new(forecast_id);
        let (state, _version): (WeatherForecastState, i32) = ctx.get_state(&tag).await?;
        if !state.is_empty() {
            return Err(CommandError::AlreadyExists(forecast_id.to_string()));
        }

        single_output(
            WeatherForecastCreated {
                forecast_id,
                location: self.location.clone(),
                date: self.date.clone(),
                temperature_c: self.temperature_c,
                summary: self.summary.clone(),
                created_at: Utc::now().to_rfc3339(),
            },
            tag,
            None,
        )
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, Command)]
#[serde(rename_all = "camelCase")]
pub struct UpdateWeatherForecastLocation {
    pub forecast_id: Uuid,
    pub new_location: String,
}

#[async_trait]
impl CommandHandler for UpdateWeatherForecastLocation {
    async fn handle<C: CommandContext + ?Sized>(&self, ctx: &C) -> Result<Option<CommandOutput>, CommandError> {
        let tag = WeatherForecastTag::new(self.forecast_id);
        let (state, version): (WeatherForecastState, i32) = ctx.get_state(&tag).await?;
        if state.is_empty() {
            return Err(CommandError::NotFound(self.forecast_id.to_string()));
        }
        if state.is_deleted {
            return Err(CommandError::Deleted(self.forecast_id.to_string()));
        }
        if state.location == self.new_location {
            return Ok(None);
        }

        single_output(
            WeatherForecastLocationUpdated {
                forecast_id: self.forecast_id,
                new_location: self.new_location.clone(),
                updated_at: Utc::now().to_rfc3339(),
            },
            tag,
            Some(version),
        )
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, Command)]
#[serde(rename_all = "camelCase")]
pub struct DeleteWeatherForecast {
    pub forecast_id: Uuid,
}

#[async_trait]
impl CommandHandler for DeleteWeatherForecast {
    async fn handle<C: CommandContext + ?Sized>(&self, ctx: &C) -> Result<Option<CommandOutput>, CommandError> {
        let tag = WeatherForecastTag::new(self.forecast_id);
        let (state, version): (WeatherForecastState, i32) = ctx.get_state(&tag).await?;
        if state.is_empty() {
            return Err(CommandError::NotFound(self.forecast_id.to_string()));
        }
        if state.is_deleted {
            return Ok(None);
        }

        single_output(
            WeatherForecastDeleted {
                forecast_id: self.forecast_id,
                deleted_at: Utc::now().to_rfc3339(),
            },
            tag,
            Some(version),
        )
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, Command)]
#[serde(rename_all = "camelCase")]
pub struct CreateStudent {
    pub student_id: Uuid,
    pub name: String,
    pub max_class_count: i32,
}

#[async_trait]
impl CommandHandler for CreateStudent {
    async fn handle<C: CommandContext + ?Sized>(&self, ctx: &C) -> Result<Option<CommandOutput>, CommandError> {
        let tag = StudentTag {
            student_id: self.student_id,
        };
        if ctx.tag_exists(&tag).await? {
            return Err(CommandError::AlreadyExists(self.student_id.to_string()));
        }

        single_output(
            StudentCreated {
                student_id: self.student_id,
                name: self.name.clone(),
                max_class_count: self.max_class_count,
            },
            tag,
            None,
        )
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, Command)]
#[serde(rename_all = "camelCase")]
pub struct CreateClassRoom {
    pub class_room_id: Uuid,
    pub name: String,
    pub max_students: i32,
}

#[async_trait]
impl CommandHandler for CreateClassRoom {
    async fn handle<C: CommandContext + ?Sized>(&self, ctx: &C) -> Result<Option<CommandOutput>, CommandError> {
        let tag = ClassRoomTag {
            class_room_id: self.class_room_id,
        };
        if ctx.tag_exists(&tag).await? {
            return Err(CommandError::AlreadyExists(self.class_room_id.to_string()));
        }

        single_output(
            ClassRoomCreated {
                class_room_id: self.class_room_id,
                name: self.name.clone(),
                max_students: self.max_students,
            },
            tag,
            None,
        )
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, Command)]
#[serde(rename_all = "camelCase")]
pub struct EnrollStudentInClassRoom {
    pub student_id: Uuid,
    pub class_room_id: Uuid,
}

#[async_trait]
impl CommandHandler for EnrollStudentInClassRoom {
    async fn handle<C: CommandContext + ?Sized>(&self, ctx: &C) -> Result<Option<CommandOutput>, CommandError> {
        let student_tag = StudentTag {
            student_id: self.student_id,
        };
        let classroom_tag = ClassRoomTag {
            class_room_id: self.class_room_id,
        };
        let (student, student_version): (StudentState, i32) = ctx.get_state(&student_tag).await?;
        let (classroom, classroom_version): (ClassRoomState, i32) = ctx.get_state(&classroom_tag).await?;
        if student.is_empty() {
            return Err(CommandError::NotFound(self.student_id.to_string()));
        }
        if classroom.class_room_id == Uuid::nil() {
            return Err(CommandError::NotFound(self.class_room_id.to_string()));
        }
        if student.enrolled_class_room_ids.contains(&self.class_room_id)
            || classroom.enrolled_student_ids.contains(&self.student_id)
        {
            return Ok(None);
        }
        if student.enrolled_class_room_ids.len() as i32 >= student.max_class_count {
            return Err(CommandError::Validation("Student reached max class count".to_string()));
        }
        if classroom.enrolled_student_ids.len() as i32 >= classroom.max_students {
            return Err(CommandError::Validation("Class room is full".to_string()));
        }

        let event = StudentEnrolledInClassRoom {
            student_id: self.student_id,
            class_room_id: self.class_room_id,
        };
        multi_tag_output(
            event,
            vec![student_tag.to_tag_string(), classroom_tag.to_tag_string()],
            vec![
                (student_tag.to_tag_string(), student_version),
                (classroom_tag.to_tag_string(), classroom_version),
            ],
        )
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, Command)]
#[serde(rename_all = "camelCase")]
pub struct DropStudentFromClassRoom {
    pub student_id: Uuid,
    pub class_room_id: Uuid,
}

#[async_trait]
impl CommandHandler for DropStudentFromClassRoom {
    async fn handle<C: CommandContext + ?Sized>(&self, ctx: &C) -> Result<Option<CommandOutput>, CommandError> {
        let student_tag = StudentTag {
            student_id: self.student_id,
        };
        let classroom_tag = ClassRoomTag {
            class_room_id: self.class_room_id,
        };
        let (student, student_version): (StudentState, i32) = ctx.get_state(&student_tag).await?;
        let (classroom, classroom_version): (ClassRoomState, i32) = ctx.get_state(&classroom_tag).await?;
        if student.is_empty() || classroom.class_room_id == Uuid::nil() {
            return Err(CommandError::NotFound("Enrollment not found".to_string()));
        }
        if !student.enrolled_class_room_ids.contains(&self.class_room_id) {
            return Ok(None);
        }

        let event = StudentDroppedFromClassRoom {
            student_id: self.student_id,
            class_room_id: self.class_room_id,
        };
        multi_tag_output(
            event,
            vec![student_tag.to_tag_string(), classroom_tag.to_tag_string()],
            vec![
                (student_tag.to_tag_string(), student_version),
                (classroom_tag.to_tag_string(), classroom_version),
            ],
        )
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, Command)]
#[serde(rename_all = "camelCase")]
pub struct RegisterUser {
    pub user_id: Uuid,
    pub display_name: String,
    pub email: String,
    pub department: Option<String>,
    pub monthly_reservation_limit: i32,
}

#[async_trait]
impl CommandHandler for RegisterUser {
    async fn handle<C: CommandContext + ?Sized>(&self, ctx: &C) -> Result<Option<CommandOutput>, CommandError> {
        let tag = UserTag { user_id: self.user_id };
        if ctx.tag_exists(&tag).await? {
            return Err(CommandError::AlreadyExists(self.user_id.to_string()));
        }

        single_output(
            UserRegistered {
                user_id: self.user_id,
                display_name: self.display_name.clone(),
                email: self.email.clone(),
                department: self.department.clone(),
                registered_at: Utc::now().to_rfc3339(),
                monthly_reservation_limit: self.monthly_reservation_limit.max(1),
            },
            tag,
            None,
        )
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, Command)]
#[serde(rename_all = "camelCase")]
pub struct UpdateUserMonthlyReservationLimit {
    pub user_id: Uuid,
    pub monthly_reservation_limit: i32,
}

#[async_trait]
impl CommandHandler for UpdateUserMonthlyReservationLimit {
    async fn handle<C: CommandContext + ?Sized>(&self, ctx: &C) -> Result<Option<CommandOutput>, CommandError> {
        let tag = UserTag { user_id: self.user_id };
        let (state, version): (UserDirectoryState, i32) = ctx.get_state(&tag).await?;
        if state.user_id == Uuid::nil() || !state.is_active {
            return Err(CommandError::NotFound(self.user_id.to_string()));
        }

        single_output(
            UserProfileUpdated {
                user_id: self.user_id,
                display_name: state.display_name,
                email: state.email,
                department: state.department,
                monthly_reservation_limit: self.monthly_reservation_limit.max(1),
            },
            tag,
            Some(version),
        )
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, Command)]
#[serde(rename_all = "camelCase")]
pub struct GrantUserAccess {
    pub user_id: Uuid,
    pub initial_role: String,
}

#[async_trait]
impl CommandHandler for GrantUserAccess {
    async fn handle<C: CommandContext + ?Sized>(&self, ctx: &C) -> Result<Option<CommandOutput>, CommandError> {
        let tag = UserAccessTag { user_id: self.user_id };
        if ctx.tag_exists(&tag).await? {
            return Err(CommandError::AlreadyExists(self.user_id.to_string()));
        }

        single_output(
            UserAccessGranted {
                user_id: self.user_id,
                initial_role: self.initial_role.clone(),
                granted_at: Utc::now().to_rfc3339(),
            },
            tag,
            None,
        )
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, Command)]
#[serde(rename_all = "camelCase")]
pub struct GrantUserRole {
    pub user_id: Uuid,
    pub role: String,
}

#[async_trait]
impl CommandHandler for GrantUserRole {
    async fn handle<C: CommandContext + ?Sized>(&self, ctx: &C) -> Result<Option<CommandOutput>, CommandError> {
        let tag = UserAccessTag { user_id: self.user_id };
        let (state, version): (UserAccessState, i32) = ctx.get_state(&tag).await?;
        if state.user_id == Uuid::nil() || !state.is_active {
            return Err(CommandError::NotFound(self.user_id.to_string()));
        }
        if state.roles.iter().any(|role| role == &self.role) {
            return Ok(None);
        }

        single_output(
            UserRoleGranted {
                user_id: self.user_id,
                role: self.role.clone(),
                granted_at: Utc::now().to_rfc3339(),
            },
            tag,
            Some(version),
        )
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, Command)]
#[serde(rename_all = "camelCase")]
pub struct CreateRoom {
    pub room_id: Uuid,
    pub name: String,
    pub capacity: i32,
    pub location: String,
    pub equipment: Vec<String>,
    pub requires_approval: bool,
}

#[async_trait]
impl CommandHandler for CreateRoom {
    async fn handle<C: CommandContext + ?Sized>(&self, ctx: &C) -> Result<Option<CommandOutput>, CommandError> {
        let tag = RoomTag { room_id: self.room_id };
        if ctx.tag_exists(&tag).await? {
            return Err(CommandError::AlreadyExists(self.room_id.to_string()));
        }
        single_output(
            RoomCreated {
                room_id: self.room_id,
                name: self.name.clone(),
                capacity: self.capacity,
                location: self.location.clone(),
                equipment: self.equipment.clone(),
                requires_approval: self.requires_approval,
            },
            tag,
            None,
        )
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, Command)]
#[serde(rename_all = "camelCase")]
pub struct UpdateRoom {
    pub room_id: Uuid,
    pub name: String,
    pub capacity: i32,
    pub location: String,
    pub equipment: Vec<String>,
    pub requires_approval: bool,
}

#[async_trait]
impl CommandHandler for UpdateRoom {
    async fn handle<C: CommandContext + ?Sized>(&self, ctx: &C) -> Result<Option<CommandOutput>, CommandError> {
        let tag = RoomTag { room_id: self.room_id };
        let (state, version): (RoomState, i32) = ctx.get_state(&tag).await?;
        if state.room_id == Uuid::nil() {
            return Err(CommandError::NotFound(self.room_id.to_string()));
        }
        single_output(
            RoomUpdated {
                room_id: self.room_id,
                name: self.name.clone(),
                capacity: self.capacity,
                location: self.location.clone(),
                equipment: self.equipment.clone(),
                requires_approval: self.requires_approval,
            },
            tag,
            Some(version),
        )
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, Command)]
#[serde(rename_all = "camelCase")]
pub struct CreateReservationDraft {
    pub reservation_id: Uuid,
    pub room_id: Uuid,
    pub organizer_id: Uuid,
    pub organizer_name: String,
    pub start_time: String,
    pub end_time: String,
    pub purpose: String,
    pub selected_equipment: Vec<String>,
}

#[async_trait]
impl CommandHandler for CreateReservationDraft {
    async fn handle<C: CommandContext + ?Sized>(&self, ctx: &C) -> Result<Option<CommandOutput>, CommandError> {
        let reservation_tag = ReservationTag {
            reservation_id: self.reservation_id,
        };
        let room_tag = RoomTag { room_id: self.room_id };
        if ctx.tag_exists(&reservation_tag).await? {
            return Err(CommandError::AlreadyExists(self.reservation_id.to_string()));
        }
        let (room, _room_version): (RoomState, i32) = ctx.get_state(&room_tag).await?;
        if room.room_id == Uuid::nil() {
            return Err(CommandError::NotFound(self.room_id.to_string()));
        }

        single_output(
            ReservationDraftCreated {
                reservation_id: self.reservation_id,
                room_id: self.room_id,
                organizer_id: self.organizer_id,
                organizer_name: self.organizer_name.clone(),
                start_time: self.start_time.clone(),
                end_time: self.end_time.clone(),
                purpose: self.purpose.clone(),
                selected_equipment: self.selected_equipment.clone(),
            },
            reservation_tag,
            None,
        )
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, Command)]
#[serde(rename_all = "camelCase")]
pub struct CreateQuickReservation {
    pub reservation_id: Uuid,
    pub room_id: Uuid,
    pub organizer_id: Uuid,
    pub organizer_name: String,
    pub start_time: String,
    pub end_time: String,
    pub purpose: String,
    pub approval_request_id: Option<Uuid>,
    pub approval_request_comment: Option<String>,
    pub selected_equipment: Vec<String>,
}

#[async_trait]
impl CommandHandler for CreateQuickReservation {
    async fn handle<C: CommandContext + ?Sized>(&self, ctx: &C) -> Result<Option<CommandOutput>, CommandError> {
        let reservation_tag = ReservationTag {
            reservation_id: self.reservation_id,
        };
        if ctx.tag_exists(&reservation_tag).await? {
            return Err(CommandError::AlreadyExists(self.reservation_id.to_string()));
        }

        let room_tag = RoomTag { room_id: self.room_id };
        let (room, _room_version): (RoomState, i32) = ctx.get_state(&room_tag).await?;
        if room.room_id == Uuid::nil() {
            return Err(CommandError::NotFound(self.room_id.to_string()));
        }

        let room_reservation_tag = RoomReservationTag { room_id: self.room_id };
        let (room_reservations, room_reservations_version): (RoomReservationsState, i32) =
            ctx.get_state(&room_reservation_tag).await?;

        if room_reservations.has_conflict(&self.start_time, &self.end_time, Some(self.reservation_id)) {
            return Err(CommandError::Validation(
                "Reservation time conflicts with another held or confirmed reservation".to_string(),
            ));
        }

        let draft_created = EventOutput {
            event_type: ReservationDraftCreated::EVENT_TYPE.to_string(),
            payload: serde_json::to_string(&ReservationDraftCreated {
                reservation_id: self.reservation_id,
                room_id: self.room_id,
                organizer_id: self.organizer_id,
                organizer_name: self.organizer_name.clone(),
                start_time: self.start_time.clone(),
                end_time: self.end_time.clone(),
                purpose: self.purpose.clone(),
                selected_equipment: self.selected_equipment.clone(),
            })
            .map_err(|err| CommandError::Serialization(err.to_string()))?,
        };

        let hold_committed = EventOutput {
            event_type: ReservationHoldCommitted::EVENT_TYPE.to_string(),
            payload: serde_json::to_string(&ReservationHoldCommitted {
                reservation_id: self.reservation_id,
                room_id: self.room_id,
                organizer_id: self.organizer_id,
                organizer_name: self.organizer_name.clone(),
                start_time: self.start_time.clone(),
                end_time: self.end_time.clone(),
                purpose: self.purpose.clone(),
                selected_equipment: self.selected_equipment.clone(),
                requires_approval: room.requires_approval,
                approval_request_id: self.approval_request_id,
                approval_request_comment: self.approval_request_comment.clone(),
            })
            .map_err(|err| CommandError::Serialization(err.to_string()))?,
        };

        let mut events = vec![draft_created, hold_committed];
        if !room.requires_approval {
            events.push(EventOutput {
                event_type: ReservationConfirmed::EVENT_TYPE.to_string(),
                payload: serde_json::to_string(&ReservationConfirmed {
                    reservation_id: self.reservation_id,
                    room_id: self.room_id,
                    organizer_id: self.organizer_id,
                    organizer_name: self.organizer_name.clone(),
                    start_time: self.start_time.clone(),
                    end_time: self.end_time.clone(),
                    purpose: self.purpose.clone(),
                    selected_equipment: self.selected_equipment.clone(),
                    confirmed_at: Utc::now().to_rfc3339(),
                    approval_request_id: None,
                    approval_request_comment: None,
                    approval_decision_comment: None,
                })
                .map_err(|err| CommandError::Serialization(err.to_string()))?,
            });
        }

        let reservation_tag_string = reservation_tag.to_tag_string();
        let room_reservation_tag_string = room_reservation_tag.to_tag_string();
        multi_event_output(
            events,
            vec![reservation_tag_string.clone(), room_reservation_tag_string.clone()],
            vec![reservation_tag_string, room_reservation_tag_string.clone()],
            vec![(room_reservation_tag_string, room_reservations_version)],
        )
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, Command)]
#[serde(rename_all = "camelCase")]
pub struct CommitReservationHold {
    pub reservation_id: Uuid,
    pub room_id: Uuid,
    pub requires_approval: bool,
    pub approval_request_id: Option<Uuid>,
    pub approval_request_comment: Option<String>,
}

#[async_trait]
impl CommandHandler for CommitReservationHold {
    async fn handle<C: CommandContext + ?Sized>(&self, ctx: &C) -> Result<Option<CommandOutput>, CommandError> {
        let reservation_tag = ReservationTag {
            reservation_id: self.reservation_id,
        };
        let (state, version): (ReservationState, i32) = ctx.get_state(&reservation_tag).await?;
        if state.is_empty() {
            return Err(CommandError::NotFound(self.reservation_id.to_string()));
        }
        if state.status != "Draft" {
            return Ok(None);
        }

        multi_tag_output(
            ReservationHoldCommitted {
                reservation_id: state.reservation_id,
                room_id: self.room_id,
                organizer_id: state.organizer_id,
                organizer_name: state.organizer_name,
                start_time: state.start_time,
                end_time: state.end_time,
                purpose: state.purpose,
                selected_equipment: state.selected_equipment,
                requires_approval: self.requires_approval,
                approval_request_id: self.approval_request_id,
                approval_request_comment: self.approval_request_comment.clone(),
            },
            vec![
                reservation_tag.to_tag_string(),
                RoomReservationTag { room_id: self.room_id }.to_tag_string(),
            ],
            vec![(reservation_tag.to_tag_string(), version)],
        )
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, Command)]
#[serde(rename_all = "camelCase")]
pub struct ConfirmReservation {
    pub reservation_id: Uuid,
    pub room_id: Uuid,
}

#[async_trait]
impl CommandHandler for ConfirmReservation {
    async fn handle<C: CommandContext + ?Sized>(&self, ctx: &C) -> Result<Option<CommandOutput>, CommandError> {
        let reservation_tag = ReservationTag {
            reservation_id: self.reservation_id,
        };
        let (state, version): (ReservationState, i32) = ctx.get_state(&reservation_tag).await?;
        if state.is_empty() {
            return Err(CommandError::NotFound(self.reservation_id.to_string()));
        }
        if state.status == "Confirmed" {
            return Ok(None);
        }

        multi_tag_output(
            ReservationConfirmed {
                reservation_id: state.reservation_id,
                room_id: self.room_id,
                organizer_id: state.organizer_id,
                organizer_name: state.organizer_name,
                start_time: state.start_time,
                end_time: state.end_time,
                purpose: state.purpose,
                selected_equipment: state.selected_equipment,
                confirmed_at: Utc::now().to_rfc3339(),
                approval_request_id: state.approval_request_id,
                approval_request_comment: state.approval_request_comment,
                approval_decision_comment: state.approval_decision_comment,
            },
            vec![
                reservation_tag.to_tag_string(),
                RoomReservationTag { room_id: self.room_id }.to_tag_string(),
            ],
            vec![(reservation_tag.to_tag_string(), version)],
        )
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, Command)]
#[serde(rename_all = "camelCase")]
pub struct CancelReservation {
    pub reservation_id: Uuid,
    pub room_id: Uuid,
    pub reason: String,
}

#[async_trait]
impl CommandHandler for CancelReservation {
    async fn handle<C: CommandContext + ?Sized>(&self, ctx: &C) -> Result<Option<CommandOutput>, CommandError> {
        let reservation_tag = ReservationTag {
            reservation_id: self.reservation_id,
        };
        let (state, version): (ReservationState, i32) = ctx.get_state(&reservation_tag).await?;
        if state.is_empty() {
            return Err(CommandError::NotFound(self.reservation_id.to_string()));
        }

        multi_tag_output(
            ReservationCancelled {
                reservation_id: state.reservation_id,
                room_id: self.room_id,
                organizer_id: state.organizer_id,
                organizer_name: state.organizer_name,
                start_time: state.start_time,
                end_time: state.end_time,
                purpose: state.purpose,
                selected_equipment: state.selected_equipment,
                approval_request_comment: state.approval_request_comment,
                reason: self.reason.clone(),
                cancelled_at: Utc::now().to_rfc3339(),
            },
            vec![
                reservation_tag.to_tag_string(),
                RoomReservationTag { room_id: self.room_id }.to_tag_string(),
            ],
            vec![(reservation_tag.to_tag_string(), version)],
        )
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, Command)]
#[serde(rename_all = "camelCase")]
pub struct RejectReservation {
    pub reservation_id: Uuid,
    pub room_id: Uuid,
    pub approval_request_id: Uuid,
    pub reason: String,
}

#[async_trait]
impl CommandHandler for RejectReservation {
    async fn handle<C: CommandContext + ?Sized>(&self, ctx: &C) -> Result<Option<CommandOutput>, CommandError> {
        let reservation_tag = ReservationTag {
            reservation_id: self.reservation_id,
        };
        let (state, version): (ReservationState, i32) = ctx.get_state(&reservation_tag).await?;
        if state.is_empty() {
            return Err(CommandError::NotFound(self.reservation_id.to_string()));
        }

        multi_tag_output(
            ReservationRejected {
                reservation_id: state.reservation_id,
                room_id: self.room_id,
                organizer_id: state.organizer_id,
                organizer_name: state.organizer_name,
                start_time: state.start_time,
                end_time: state.end_time,
                purpose: state.purpose,
                selected_equipment: state.selected_equipment,
                approval_request_id: self.approval_request_id,
                approval_request_comment: state.approval_request_comment,
                reason: self.reason.clone(),
                rejected_at: Utc::now().to_rfc3339(),
            },
            vec![
                reservation_tag.to_tag_string(),
                RoomReservationTag { room_id: self.room_id }.to_tag_string(),
            ],
            vec![(reservation_tag.to_tag_string(), version)],
        )
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, Command)]
#[serde(rename_all = "camelCase")]
pub struct StartApprovalFlow {
    pub approval_request_id: Uuid,
    pub reservation_id: Uuid,
    pub room_id: Uuid,
    pub requester_id: Uuid,
    pub approver_ids: Vec<Uuid>,
    pub request_comment: Option<String>,
}

#[async_trait]
impl CommandHandler for StartApprovalFlow {
    async fn handle<C: CommandContext + ?Sized>(&self, ctx: &C) -> Result<Option<CommandOutput>, CommandError> {
        let tag = ApprovalRequestTag {
            approval_request_id: self.approval_request_id,
        };
        if ctx.tag_exists(&tag).await? {
            return Err(CommandError::AlreadyExists(self.approval_request_id.to_string()));
        }

        single_output(
            ApprovalFlowStarted {
                approval_request_id: self.approval_request_id,
                reservation_id: self.reservation_id,
                room_id: self.room_id,
                requester_id: self.requester_id,
                approver_ids: self.approver_ids.clone(),
                requested_at: Utc::now().to_rfc3339(),
                request_comment: self.request_comment.clone(),
            },
            tag,
            None,
        )
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, Command)]
#[serde(rename_all = "camelCase")]
pub struct RecordApprovalDecision {
    pub approval_request_id: Uuid,
    pub reservation_id: Uuid,
    pub approver_id: Uuid,
    pub decision: ApprovalDecision,
    pub comment: Option<String>,
}

#[async_trait]
impl CommandHandler for RecordApprovalDecision {
    async fn handle<C: CommandContext + ?Sized>(&self, ctx: &C) -> Result<Option<CommandOutput>, CommandError> {
        let tag = ApprovalRequestTag {
            approval_request_id: self.approval_request_id,
        };
        let (state, version): (ApprovalRequestState, i32) = ctx.get_state(&tag).await?;
        if state.is_empty() {
            return Err(CommandError::NotFound(self.approval_request_id.to_string()));
        }
        if state.status != "Pending" {
            return Ok(None);
        }

        single_output(
            ApprovalDecisionRecorded {
                approval_request_id: self.approval_request_id,
                reservation_id: self.reservation_id,
                approver_id: self.approver_id,
                decision: self.decision.clone(),
                comment: self.comment.clone(),
                decided_at: Utc::now().to_rfc3339(),
            },
            tag,
            Some(version),
        )
    }
}
