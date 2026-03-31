use sekiban_derive::Event;
use serde::{Deserialize, Serialize};
use uuid::Uuid;

#[derive(Debug, Clone, Serialize, Deserialize, Event)]
#[serde(rename_all = "camelCase")]
pub struct WeatherForecastCreated {
    pub forecast_id: Uuid,
    pub location: String,
    pub date: String,
    pub temperature_c: i32,
    pub summary: String,
    pub created_at: String,
}

#[derive(Debug, Clone, Serialize, Deserialize, Event)]
#[serde(rename_all = "camelCase")]
pub struct WeatherForecastLocationUpdated {
    pub forecast_id: Uuid,
    pub new_location: String,
    pub updated_at: String,
}

#[derive(Debug, Clone, Serialize, Deserialize, Event)]
#[serde(rename_all = "camelCase")]
pub struct WeatherForecastDeleted {
    pub forecast_id: Uuid,
    pub deleted_at: String,
}

#[derive(Debug, Clone, Serialize, Deserialize, Event)]
#[serde(rename_all = "camelCase")]
pub struct StudentCreated {
    pub student_id: Uuid,
    pub name: String,
    pub max_class_count: i32,
}

#[derive(Debug, Clone, Serialize, Deserialize, Event)]
#[serde(rename_all = "camelCase")]
pub struct ClassRoomCreated {
    pub class_room_id: Uuid,
    pub name: String,
    pub max_students: i32,
}

#[derive(Debug, Clone, Serialize, Deserialize, Event)]
#[serde(rename_all = "camelCase")]
pub struct StudentEnrolledInClassRoom {
    pub student_id: Uuid,
    pub class_room_id: Uuid,
}

#[derive(Debug, Clone, Serialize, Deserialize, Event)]
#[serde(rename_all = "camelCase")]
pub struct StudentDroppedFromClassRoom {
    pub student_id: Uuid,
    pub class_room_id: Uuid,
}

#[derive(Debug, Clone, Serialize, Deserialize, Event)]
#[serde(rename_all = "camelCase")]
pub struct UserRegistered {
    pub user_id: Uuid,
    pub display_name: String,
    pub email: String,
    pub department: Option<String>,
    pub registered_at: String,
    pub monthly_reservation_limit: i32,
}

#[derive(Debug, Clone, Serialize, Deserialize, Event)]
#[serde(rename_all = "camelCase")]
pub struct UserProfileUpdated {
    pub user_id: Uuid,
    pub display_name: String,
    pub email: String,
    pub department: Option<String>,
    pub monthly_reservation_limit: i32,
}

#[derive(Debug, Clone, Serialize, Deserialize, Event)]
#[serde(rename_all = "camelCase")]
pub struct UserAccessGranted {
    pub user_id: Uuid,
    pub initial_role: String,
    pub granted_at: String,
}

#[derive(Debug, Clone, Serialize, Deserialize, Event)]
#[serde(rename_all = "camelCase")]
pub struct UserRoleGranted {
    pub user_id: Uuid,
    pub role: String,
    pub granted_at: String,
}

#[derive(Debug, Clone, Serialize, Deserialize, Event)]
#[serde(rename_all = "camelCase")]
pub struct RoomCreated {
    pub room_id: Uuid,
    pub name: String,
    pub capacity: i32,
    pub location: String,
    pub equipment: Vec<String>,
    pub requires_approval: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize, Event)]
#[serde(rename_all = "camelCase")]
pub struct RoomUpdated {
    pub room_id: Uuid,
    pub name: String,
    pub capacity: i32,
    pub location: String,
    pub equipment: Vec<String>,
    pub requires_approval: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize, Event)]
#[serde(rename_all = "camelCase")]
pub struct ReservationDraftCreated {
    pub reservation_id: Uuid,
    pub room_id: Uuid,
    pub organizer_id: Uuid,
    pub organizer_name: String,
    pub start_time: String,
    pub end_time: String,
    pub purpose: String,
    pub selected_equipment: Vec<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize, Event)]
#[serde(rename_all = "camelCase")]
pub struct ReservationHoldCommitted {
    pub reservation_id: Uuid,
    pub room_id: Uuid,
    pub organizer_id: Uuid,
    pub organizer_name: String,
    pub start_time: String,
    pub end_time: String,
    pub purpose: String,
    pub selected_equipment: Vec<String>,
    pub requires_approval: bool,
    pub approval_request_id: Option<Uuid>,
    pub approval_request_comment: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize, Event)]
#[serde(rename_all = "camelCase")]
pub struct ReservationConfirmed {
    pub reservation_id: Uuid,
    pub room_id: Uuid,
    pub organizer_id: Uuid,
    pub organizer_name: String,
    pub start_time: String,
    pub end_time: String,
    pub purpose: String,
    pub selected_equipment: Vec<String>,
    pub confirmed_at: String,
    pub approval_request_id: Option<Uuid>,
    pub approval_request_comment: Option<String>,
    pub approval_decision_comment: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize, Event)]
#[serde(rename_all = "camelCase")]
pub struct ReservationCancelled {
    pub reservation_id: Uuid,
    pub room_id: Uuid,
    pub organizer_id: Uuid,
    pub organizer_name: String,
    pub start_time: String,
    pub end_time: String,
    pub purpose: String,
    pub selected_equipment: Vec<String>,
    pub approval_request_comment: Option<String>,
    pub reason: String,
    pub cancelled_at: String,
}

#[derive(Debug, Clone, Serialize, Deserialize, Event)]
#[serde(rename_all = "camelCase")]
pub struct ReservationRejected {
    pub reservation_id: Uuid,
    pub room_id: Uuid,
    pub organizer_id: Uuid,
    pub organizer_name: String,
    pub start_time: String,
    pub end_time: String,
    pub purpose: String,
    pub selected_equipment: Vec<String>,
    pub approval_request_id: Uuid,
    pub approval_request_comment: Option<String>,
    pub reason: String,
    pub rejected_at: String,
}

#[derive(Debug, Clone, Serialize, Deserialize, Event)]
#[serde(rename_all = "camelCase")]
pub struct ApprovalFlowStarted {
    pub approval_request_id: Uuid,
    pub reservation_id: Uuid,
    pub room_id: Uuid,
    pub requester_id: Uuid,
    pub approver_ids: Vec<Uuid>,
    pub requested_at: String,
    pub request_comment: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize, Event)]
#[serde(rename_all = "camelCase")]
pub struct ApprovalDecisionRecorded {
    pub approval_request_id: Uuid,
    pub reservation_id: Uuid,
    pub approver_id: Uuid,
    pub decision: crate::states::ApprovalDecision,
    pub comment: Option<String>,
    pub decided_at: String,
}
