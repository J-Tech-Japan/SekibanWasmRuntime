use chrono::{DateTime, NaiveDate, Utc};
use sekiban_derive::State;
use serde::{Deserialize, Serialize};
use std::collections::HashMap;
use uuid::Uuid;

#[derive(Debug, Clone, Default, Serialize, Deserialize, State, PartialEq)]
#[serde(rename_all = "camelCase")]
#[state(empty_check = "forecast_id")]
pub struct WeatherForecastState {
    pub forecast_id: Uuid,
    pub location: String,
    pub date: String,
    pub temperature_c: i32,
    pub summary: String,
    pub created_at: String,
    pub is_deleted: bool,
    pub deleted_at: Option<String>,
}

impl WeatherForecastState {
    pub fn with_deleted(mut self, deleted: bool, deleted_at: Option<String>) -> Self {
        self.is_deleted = deleted;
        self.deleted_at = deleted_at;
        self
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct WeatherForecastItem {
    pub forecast_id: Uuid,
    pub location: String,
    pub date: String,
    pub temperature_c: i32,
    pub summary: String,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize, State, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct WeatherForecastListState {
    pub items: HashMap<Uuid, WeatherForecastItem>,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize, State, PartialEq)]
#[serde(rename_all = "camelCase")]
#[state(empty_check = "student_id")]
pub struct StudentState {
    pub student_id: Uuid,
    pub name: String,
    pub max_class_count: i32,
    pub enrolled_class_room_ids: Vec<Uuid>,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize, State, PartialEq)]
#[serde(rename_all = "camelCase")]
#[state(empty_check = "class_room_id")]
pub struct ClassRoomState {
    pub class_room_id: Uuid,
    pub name: String,
    pub max_students: i32,
    pub enrolled_student_ids: Vec<Uuid>,
}

impl ClassRoomState {
    pub fn is_full(&self) -> bool {
        self.class_room_id != Uuid::nil() && self.enrolled_student_ids.len() as i32 >= self.max_students
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct ClassRoomItem {
    pub class_room_id: Uuid,
    pub name: String,
    pub max_students: i32,
    pub enrolled_count: i32,
    pub is_full: bool,
    pub remaining_capacity: i32,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize, State, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct StudentListState {
    pub items: HashMap<Uuid, StudentState>,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize, State, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct ClassRoomListState {
    pub items: HashMap<Uuid, ClassRoomItem>,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize, State, PartialEq)]
#[serde(rename_all = "camelCase")]
#[state(empty_check = "user_id")]
pub struct UserDirectoryState {
    pub user_id: Uuid,
    pub display_name: String,
    pub email: String,
    pub department: Option<String>,
    pub registered_at: String,
    pub monthly_reservation_limit: i32,
    pub external_providers: Vec<String>,
    pub is_active: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct UserDirectoryListItem {
    pub user_id: Uuid,
    pub display_name: String,
    pub email: String,
    pub department: Option<String>,
    pub is_active: bool,
    pub registered_at: String,
    pub monthly_reservation_limit: i32,
    pub external_providers: Vec<String>,
    pub roles: Vec<String>,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize, State, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct UserDirectoryListState {
    pub items: HashMap<Uuid, UserDirectoryListItem>,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize, State, PartialEq)]
#[serde(rename_all = "camelCase")]
#[state(empty_check = "user_id")]
pub struct UserAccessState {
    pub user_id: Uuid,
    pub roles: Vec<String>,
    pub granted_at: String,
    pub is_active: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct UserAccessListItem {
    pub user_id: Uuid,
    pub roles: Vec<String>,
    pub is_active: bool,
    pub granted_at: String,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize, State, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct UserAccessListState {
    pub items: HashMap<Uuid, UserAccessListItem>,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize, State, PartialEq)]
#[serde(rename_all = "camelCase")]
#[state(empty_check = "room_id")]
pub struct RoomState {
    pub room_id: Uuid,
    pub name: String,
    pub capacity: i32,
    pub location: String,
    pub equipment: Vec<String>,
    pub requires_approval: bool,
    pub is_active: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct RoomListItem {
    pub room_id: Uuid,
    pub name: String,
    pub capacity: i32,
    pub location: String,
    pub equipment: Vec<String>,
    pub requires_approval: bool,
    pub is_active: bool,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize, State, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct RoomListState {
    pub items: HashMap<Uuid, RoomListItem>,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct ActiveReservationSlot {
    pub start_time: String,
    pub end_time: String,
    pub purpose: String,
    pub organizer_id: Uuid,
    pub status: ReservationSlotStatus,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
#[serde(rename_all = "PascalCase")]
pub enum ReservationSlotStatus {
    Held,
    Confirmed,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize, State, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct RoomReservationsState {
    pub active_reservations: HashMap<Uuid, ActiveReservationSlot>,
}

impl RoomReservationsState {
    pub fn has_conflict(&self, start_time: &str, end_time: &str, exclude_reservation_id: Option<Uuid>) -> bool {
        let Ok(start) = DateTime::parse_from_rfc3339(start_time).map(|dt| dt.with_timezone(&Utc)) else {
            return false;
        };
        let Ok(end) = DateTime::parse_from_rfc3339(end_time).map(|dt| dt.with_timezone(&Utc)) else {
            return false;
        };

        self.active_reservations.iter().any(|(reservation_id, slot)| {
            if exclude_reservation_id.is_some_and(|exclude| exclude == *reservation_id) {
                return false;
            }

            let Ok(slot_start) = DateTime::parse_from_rfc3339(&slot.start_time).map(|dt| dt.with_timezone(&Utc)) else {
                return false;
            };
            let Ok(slot_end) = DateTime::parse_from_rfc3339(&slot.end_time).map(|dt| dt.with_timezone(&Utc)) else {
                return false;
            };

            start < slot_end && slot_start < end
        })
    }

    pub fn add_or_update_reservation(
        &self,
        reservation_id: Uuid,
        start_time: String,
        end_time: String,
        purpose: String,
        organizer_id: Uuid,
        status: ReservationSlotStatus,
    ) -> Self {
        let mut next = self.clone();
        next.active_reservations.insert(
            reservation_id,
            ActiveReservationSlot {
                start_time,
                end_time,
                purpose,
                organizer_id,
                status,
            },
        );
        next
    }

    pub fn remove_reservation(&self, reservation_id: Uuid) -> Self {
        let mut next = self.clone();
        next.active_reservations.remove(&reservation_id);
        next
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
#[serde(rename_all = "PascalCase")]
pub enum ApprovalDecision {
    Approved,
    Rejected,
}

#[derive(Debug, Clone, Serialize, Deserialize, State, PartialEq)]
#[serde(rename_all = "camelCase")]
#[state(empty_check = "reservation_id")]
pub struct ReservationState {
    pub reservation_id: Uuid,
    pub room_id: Uuid,
    pub organizer_id: Uuid,
    pub organizer_name: String,
    pub start_time: String,
    pub end_time: String,
    pub purpose: String,
    pub selected_equipment: Vec<String>,
    pub status: String,
    pub requires_approval: bool,
    pub approval_request_id: Option<Uuid>,
    pub approval_request_comment: Option<String>,
    pub approval_decision_comment: Option<String>,
    pub confirmed_at: Option<String>,
    pub reason: Option<String>,
}

impl Default for ReservationState {
    fn default() -> Self {
        Self {
            reservation_id: Uuid::nil(),
            room_id: Uuid::nil(),
            organizer_id: Uuid::nil(),
            organizer_name: String::new(),
            start_time: String::new(),
            end_time: String::new(),
            purpose: String::new(),
            selected_equipment: Vec::new(),
            status: String::new(),
            requires_approval: false,
            approval_request_id: None,
            approval_request_comment: None,
            approval_decision_comment: None,
            confirmed_at: None,
            reason: None,
        }
    }
}

impl ReservationState {
    pub fn is_empty(&self) -> bool {
        self.reservation_id == Uuid::nil()
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct ReservationListItem {
    pub reservation_id: Uuid,
    pub room_id: Uuid,
    pub organizer_id: Uuid,
    pub organizer_name: String,
    pub start_time: String,
    pub end_time: String,
    pub purpose: String,
    pub selected_equipment: Vec<String>,
    pub status: String,
    pub requires_approval: bool,
    pub approval_request_id: Option<Uuid>,
    pub approval_request_comment: Option<String>,
    pub approval_decision_comment: Option<String>,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize, State, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct ReservationListState {
    pub items: HashMap<Uuid, ReservationListItem>,
}

#[derive(Debug, Clone, Serialize, Deserialize, State, PartialEq)]
#[serde(rename_all = "camelCase")]
#[state(empty_check = "approval_request_id")]
pub struct ApprovalRequestState {
    pub approval_request_id: Uuid,
    pub reservation_id: Uuid,
    pub room_id: Uuid,
    pub requester_id: Uuid,
    pub approver_ids: Vec<Uuid>,
    pub requested_at: String,
    pub request_comment: Option<String>,
    pub status: String,
}

impl Default for ApprovalRequestState {
    fn default() -> Self {
        Self {
            approval_request_id: Uuid::nil(),
            reservation_id: Uuid::nil(),
            room_id: Uuid::nil(),
            requester_id: Uuid::nil(),
            approver_ids: Vec::new(),
            requested_at: String::new(),
            request_comment: None,
            status: String::new(),
        }
    }
}

impl ApprovalRequestState {
    pub fn is_empty(&self) -> bool {
        self.approval_request_id == Uuid::nil()
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct ApprovalInboxItem {
    pub approval_request_id: Uuid,
    pub reservation_id: Uuid,
    pub room_id: Uuid,
    pub requester_id: Uuid,
    pub request_comment: Option<String>,
    pub approver_ids: Vec<Uuid>,
    pub requested_at: String,
    pub status: String,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize, State, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct ApprovalInboxState {
    pub items: HashMap<Uuid, ApprovalInboxItem>,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize, State, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct RoomDailyActivityState {
    pub room_id: Uuid,
    pub date: String,
    pub confirmed_reservations: HashMap<Uuid, ReservationSlot>,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct ReservationSlot {
    pub start_time: String,
    pub end_time: String,
    pub purpose: String,
    pub organizer_id: Uuid,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CountResult {
    pub count: i32,
}

pub fn parse_datetime(value: &str) -> Option<DateTime<Utc>> {
    DateTime::parse_from_rfc3339(value)
        .ok()
        .map(|dt| dt.with_timezone(&Utc))
}

pub fn parse_date(value: &str) -> Option<NaiveDate> {
    NaiveDate::parse_from_str(value, "%Y-%m-%d").ok()
}
