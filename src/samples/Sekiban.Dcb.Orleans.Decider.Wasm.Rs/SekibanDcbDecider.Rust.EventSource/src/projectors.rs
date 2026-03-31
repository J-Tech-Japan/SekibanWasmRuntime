use sekiban_core::prelude::*;
use sekiban_derive::{MultiProjector, TagProjector};
use crate::events::*;
use crate::queries::*;
use crate::states::*;

fn sort_json<T: serde::Serialize>(value: &T, empty: &str) -> String {
    serde_json::to_string(value).unwrap_or_else(|_| empty.to_string())
}

fn append_unique(items: &mut Vec<String>, value: &str) {
    if items.iter().all(|item| item != value) {
        items.push(value.to_string());
    }
}

#[derive(TagProjector)]
#[projector(name = "WeatherForecastProjector", version = "1.0.0")]
pub struct WeatherForecastProjector;

impl Projector for WeatherForecastProjector {
    type State = WeatherForecastState;

    fn event_types() -> Vec<&'static str> {
        vec![
            WeatherForecastCreated::EVENT_TYPE,
            WeatherForecastLocationUpdated::EVENT_TYPE,
            WeatherForecastDeleted::EVENT_TYPE,
        ]
    }

    fn project(state: Self::State, event: &Event) -> Self::State {
        match_event!(event, state, {
            WeatherForecastCreated(e) => WeatherForecastState {
                forecast_id: e.forecast_id,
                location: e.location.clone(),
                date: e.date.clone(),
                temperature_c: e.temperature_c,
                summary: e.summary.clone(),
                created_at: e.created_at.clone(),
                is_deleted: false,
                deleted_at: None,
            },
            WeatherForecastLocationUpdated(e) => {
                let mut next = state;
                next.location = e.new_location.clone();
                next
            },
            WeatherForecastDeleted(e) => state.with_deleted(true, Some(e.deleted_at.clone())),
        })
    }
}

#[derive(MultiProjector)]
#[projector(name = "WeatherForecastMultiProjection", version = "1.0.0")]
pub struct WeatherForecastListProjector;

impl Projector for WeatherForecastListProjector {
    type State = WeatherForecastListState;

    fn event_types() -> Vec<&'static str> {
        WeatherForecastProjector::event_types()
    }

    fn project(state: Self::State, event: &Event) -> Self::State {
        match_event!(event, state, {
            WeatherForecastCreated(e) => {
                let mut next = state;
                next.items.insert(e.forecast_id, WeatherForecastItem {
                    forecast_id: e.forecast_id,
                    location: e.location.clone(),
                    date: e.date.clone(),
                    temperature_c: e.temperature_c,
                    summary: e.summary.clone(),
                });
                next
            },
            WeatherForecastLocationUpdated(e) => {
                let mut next = state;
                if let Some(item) = next.items.get_mut(&e.forecast_id) {
                    item.location = e.new_location.clone();
                }
                next
            },
            WeatherForecastDeleted(e) => {
                let mut next = state;
                next.items.remove(&e.forecast_id);
                next
            },
        })
    }
}

impl MultiProjectorQuery for WeatherForecastListProjector {
    fn execute_query(state: &Self::State, query_type: &str, params: &str) -> Option<String> {
        if query_type != GetWeatherForecastCountQuery::QUERY_TYPE {
            return None;
        }
        let query: GetWeatherForecastCountQuery = serde_json::from_str(params).unwrap_or_default();
        let count = state
            .items
            .values()
            .filter(|item| query.forecast_id.is_none_or(|forecast_id| item.forecast_id == forecast_id))
            .filter(|item| {
                query
                    .location_filter
                    .as_ref()
                    .is_none_or(|filter| item.location.contains(filter))
            })
            .count() as i32;
        Some(sort_json(&CountResult { count }, "{}"))
    }

    fn execute_list_query(state: &Self::State, query_type: &str, params: &str) -> Option<String> {
        if query_type != GetWeatherForecastListQuery::QUERY_TYPE {
            return None;
        }
        let query: GetWeatherForecastListQuery = serde_json::from_str(params).unwrap_or_default();
        let mut items: Vec<_> = state
            .items
            .values()
            .filter(|item| query.forecast_id.is_none_or(|forecast_id| item.forecast_id == forecast_id))
            .filter(|item| {
                query
                    .location_filter
                    .as_ref()
                    .is_none_or(|filter| item.location.contains(filter))
            })
            .collect();
        items.sort_by(|left, right| left.location.cmp(&right.location));
        Some(sort_json(&items, "[]"))
    }
}

#[derive(TagProjector)]
#[projector(name = "StudentProjector", version = "1.0.0")]
pub struct StudentProjector;

impl Projector for StudentProjector {
    type State = StudentState;

    fn event_types() -> Vec<&'static str> {
        vec![
            StudentCreated::EVENT_TYPE,
            StudentEnrolledInClassRoom::EVENT_TYPE,
            StudentDroppedFromClassRoom::EVENT_TYPE,
        ]
    }

    fn project(state: Self::State, event: &Event) -> Self::State {
        match_event!(event, state, {
            StudentCreated(e) => StudentState {
                student_id: e.student_id,
                name: e.name.clone(),
                max_class_count: e.max_class_count,
                enrolled_class_room_ids: Vec::new(),
            },
            StudentEnrolledInClassRoom(e) => {
                let mut next = state;
                if !next.enrolled_class_room_ids.contains(&e.class_room_id) {
                    next.enrolled_class_room_ids.push(e.class_room_id);
                }
                next
            },
            StudentDroppedFromClassRoom(e) => {
                let mut next = state;
                next.enrolled_class_room_ids.retain(|id| *id != e.class_room_id);
                next
            },
        })
    }
}

#[derive(MultiProjector)]
#[projector(name = "StudentListProjection", version = "1.0.0")]
pub struct StudentListProjector;

impl Projector for StudentListProjector {
    type State = StudentListState;

    fn event_types() -> Vec<&'static str> {
        StudentProjector::event_types()
    }

    fn project(state: Self::State, event: &Event) -> Self::State {
        match_event!(event, state, {
            StudentCreated(e) => {
                let mut next = state;
                next.items.insert(e.student_id, StudentState {
                    student_id: e.student_id,
                    name: e.name.clone(),
                    max_class_count: e.max_class_count,
                    enrolled_class_room_ids: Vec::new(),
                });
                next
            },
            StudentEnrolledInClassRoom(e) => {
                let mut next = state;
                if let Some(student) = next.items.get_mut(&e.student_id) {
                    if !student.enrolled_class_room_ids.contains(&e.class_room_id) {
                        student.enrolled_class_room_ids.push(e.class_room_id);
                    }
                }
                next
            },
            StudentDroppedFromClassRoom(e) => {
                let mut next = state;
                if let Some(student) = next.items.get_mut(&e.student_id) {
                    student.enrolled_class_room_ids.retain(|id| *id != e.class_room_id);
                }
                next
            },
        })
    }
}

impl MultiProjectorQuery for StudentListProjector {
    fn execute_list_query(state: &Self::State, query_type: &str, _params: &str) -> Option<String> {
        if query_type != GetStudentListQuery::QUERY_TYPE {
            return None;
        }
        let mut items: Vec<_> = state.items.values().collect();
        items.sort_by(|left, right| left.name.cmp(&right.name));
        Some(sort_json(&items, "[]"))
    }
}

#[derive(TagProjector)]
#[projector(name = "ClassRoomProjector", version = "1.0.0")]
pub struct ClassRoomProjector;

impl Projector for ClassRoomProjector {
    type State = ClassRoomState;

    fn event_types() -> Vec<&'static str> {
        vec![
            ClassRoomCreated::EVENT_TYPE,
            StudentEnrolledInClassRoom::EVENT_TYPE,
            StudentDroppedFromClassRoom::EVENT_TYPE,
        ]
    }

    fn project(state: Self::State, event: &Event) -> Self::State {
        match_event!(event, state, {
            ClassRoomCreated(e) => ClassRoomState {
                class_room_id: e.class_room_id,
                name: e.name.clone(),
                max_students: e.max_students,
                enrolled_student_ids: Vec::new(),
            },
            StudentEnrolledInClassRoom(e) => {
                let mut next = state;
                if !next.enrolled_student_ids.contains(&e.student_id) {
                    next.enrolled_student_ids.push(e.student_id);
                }
                next
            },
            StudentDroppedFromClassRoom(e) => {
                let mut next = state;
                next.enrolled_student_ids.retain(|id| *id != e.student_id);
                next
            },
        })
    }
}

#[derive(MultiProjector)]
#[projector(name = "ClassRoomListProjection", version = "1.0.0")]
pub struct ClassRoomListProjector;

impl Projector for ClassRoomListProjector {
    type State = ClassRoomListState;

    fn event_types() -> Vec<&'static str> {
        ClassRoomProjector::event_types()
    }

    fn project(state: Self::State, event: &Event) -> Self::State {
        match_event!(event, state, {
            ClassRoomCreated(e) => {
                let mut next = state;
                next.items.insert(e.class_room_id, ClassRoomItem {
                    class_room_id: e.class_room_id,
                    name: e.name.clone(),
                    max_students: e.max_students,
                    enrolled_count: 0,
                    is_full: false,
                    remaining_capacity: e.max_students,
                });
                next
            },
            StudentEnrolledInClassRoom(e) => {
                let mut next = state;
                if let Some(item) = next.items.get_mut(&e.class_room_id) {
                    item.enrolled_count += 1;
                    item.is_full = item.enrolled_count >= item.max_students;
                    item.remaining_capacity = (item.max_students - item.enrolled_count).max(0);
                }
                next
            },
            StudentDroppedFromClassRoom(e) => {
                let mut next = state;
                if let Some(item) = next.items.get_mut(&e.class_room_id) {
                    item.enrolled_count = (item.enrolled_count - 1).max(0);
                    item.is_full = item.enrolled_count >= item.max_students;
                    item.remaining_capacity = (item.max_students - item.enrolled_count).max(0);
                }
                next
            },
        })
    }
}

impl MultiProjectorQuery for ClassRoomListProjector {
    fn execute_list_query(state: &Self::State, query_type: &str, _params: &str) -> Option<String> {
        if query_type != GetClassRoomListQuery::QUERY_TYPE {
            return None;
        }
        let mut items: Vec<_> = state.items.values().collect();
        items.sort_by(|left, right| left.name.cmp(&right.name));
        Some(sort_json(&items, "[]"))
    }
}

#[derive(TagProjector)]
#[projector(name = "UserDirectoryProjector", version = "1.0.0")]
pub struct UserDirectoryProjector;

impl Projector for UserDirectoryProjector {
    type State = UserDirectoryState;

    fn event_types() -> Vec<&'static str> {
        vec![UserRegistered::EVENT_TYPE, UserProfileUpdated::EVENT_TYPE]
    }

    fn project(state: Self::State, event: &Event) -> Self::State {
        match_event!(event, state, {
            UserRegistered(e) => UserDirectoryState {
                user_id: e.user_id,
                display_name: e.display_name.clone(),
                email: e.email.clone(),
                department: e.department.clone(),
                registered_at: e.registered_at.clone(),
                monthly_reservation_limit: e.monthly_reservation_limit,
                external_providers: Vec::new(),
                is_active: true,
            },
            UserProfileUpdated(e) => {
                let mut next = state;
                next.display_name = e.display_name.clone();
                next.email = e.email.clone();
                next.department = e.department.clone();
                next.monthly_reservation_limit = e.monthly_reservation_limit;
                next
            },
        })
    }
}

#[derive(MultiProjector)]
#[projector(name = "UserDirectoryListProjection", version = "1.0.0")]
pub struct UserDirectoryListProjector;

impl Projector for UserDirectoryListProjector {
    type State = UserDirectoryListState;

    fn event_types() -> Vec<&'static str> {
        UserDirectoryProjector::event_types()
    }

    fn project(state: Self::State, event: &Event) -> Self::State {
        match_event!(event, state, {
            UserRegistered(e) => {
                let mut next = state;
                next.items.insert(e.user_id, UserDirectoryListItem {
                    user_id: e.user_id,
                    display_name: e.display_name.clone(),
                    email: e.email.clone(),
                    department: e.department.clone(),
                    is_active: true,
                    registered_at: e.registered_at.clone(),
                    monthly_reservation_limit: e.monthly_reservation_limit,
                    external_providers: Vec::new(),
                    roles: Vec::new(),
                });
                next
            },
            UserProfileUpdated(e) => {
                let mut next = state;
                if let Some(item) = next.items.get_mut(&e.user_id) {
                    item.display_name = e.display_name.clone();
                    item.email = e.email.clone();
                    item.department = e.department.clone();
                    item.monthly_reservation_limit = e.monthly_reservation_limit;
                }
                next
            },
        })
    }
}

impl MultiProjectorQuery for UserDirectoryListProjector {
    fn execute_list_query(state: &Self::State, query_type: &str, params: &str) -> Option<String> {
        if query_type != GetUserDirectoryListQuery::QUERY_TYPE {
            return None;
        }
        let query: GetUserDirectoryListQuery = serde_json::from_str(params).unwrap_or_default();
        let mut items: Vec<_> = state
            .items
            .values()
            .filter(|item| !query.active_only || item.is_active)
            .collect();
        items.sort_by(|left, right| left.display_name.cmp(&right.display_name));
        Some(sort_json(&items, "[]"))
    }
}

#[derive(TagProjector)]
#[projector(name = "UserAccessProjector", version = "1.0.0")]
pub struct UserAccessProjector;

impl Projector for UserAccessProjector {
    type State = UserAccessState;

    fn event_types() -> Vec<&'static str> {
        vec![UserAccessGranted::EVENT_TYPE, UserRoleGranted::EVENT_TYPE]
    }

    fn project(state: Self::State, event: &Event) -> Self::State {
        match_event!(event, state, {
            UserAccessGranted(e) => UserAccessState {
                user_id: e.user_id,
                roles: vec![e.initial_role.clone()],
                granted_at: e.granted_at.clone(),
                is_active: true,
            },
            UserRoleGranted(e) => {
                let mut next = state;
                append_unique(&mut next.roles, &e.role);
                next
            },
        })
    }
}

#[derive(MultiProjector)]
#[projector(name = "UserAccessListProjection", version = "1.0.0")]
pub struct UserAccessListProjector;

impl Projector for UserAccessListProjector {
    type State = UserAccessListState;

    fn event_types() -> Vec<&'static str> {
        UserAccessProjector::event_types()
    }

    fn project(state: Self::State, event: &Event) -> Self::State {
        match_event!(event, state, {
            UserAccessGranted(e) => {
                let mut next = state;
                next.items.insert(e.user_id, UserAccessListItem {
                    user_id: e.user_id,
                    roles: vec![e.initial_role.clone()],
                    is_active: true,
                    granted_at: e.granted_at.clone(),
                });
                next
            },
            UserRoleGranted(e) => {
                let mut next = state;
                if let Some(item) = next.items.get_mut(&e.user_id) {
                    append_unique(&mut item.roles, &e.role);
                }
                next
            },
        })
    }
}

impl MultiProjectorQuery for UserAccessListProjector {
    fn execute_list_query(state: &Self::State, query_type: &str, params: &str) -> Option<String> {
        if query_type != GetUserAccessListQuery::QUERY_TYPE {
            return None;
        }
        let query: GetUserAccessListQuery = serde_json::from_str(params).unwrap_or_default();
        let mut items: Vec<_> = state
            .items
            .values()
            .filter(|item| !query.active_only || item.is_active)
            .filter(|item| {
                query
                    .role_filter
                    .as_ref()
                    .is_none_or(|role| item.roles.iter().any(|item_role| item_role == role))
            })
            .collect();
        items.sort_by(|left, right| right.granted_at.cmp(&left.granted_at));
        Some(sort_json(&items, "[]"))
    }
}

#[derive(TagProjector)]
#[projector(name = "RoomProjector", version = "1.0.0")]
pub struct RoomProjector;

impl Projector for RoomProjector {
    type State = RoomState;

    fn event_types() -> Vec<&'static str> {
        vec![RoomCreated::EVENT_TYPE, RoomUpdated::EVENT_TYPE]
    }

    fn project(state: Self::State, event: &Event) -> Self::State {
        match_event!(event, state, {
            RoomCreated(e) => RoomState {
                room_id: e.room_id,
                name: e.name.clone(),
                capacity: e.capacity,
                location: e.location.clone(),
                equipment: e.equipment.clone(),
                requires_approval: e.requires_approval,
                is_active: true,
            },
            RoomUpdated(e) => {
                let mut next = state;
                next.name = e.name.clone();
                next.capacity = e.capacity;
                next.location = e.location.clone();
                next.equipment = e.equipment.clone();
                next.requires_approval = e.requires_approval;
                next
            },
        })
    }
}

#[derive(MultiProjector)]
#[projector(name = "RoomListProjection", version = "1.0.0")]
pub struct RoomListProjector;

impl Projector for RoomListProjector {
    type State = RoomListState;

    fn event_types() -> Vec<&'static str> {
        RoomProjector::event_types()
    }

    fn project(state: Self::State, event: &Event) -> Self::State {
        match_event!(event, state, {
            RoomCreated(e) => {
                let mut next = state;
                next.items.insert(e.room_id, RoomListItem {
                    room_id: e.room_id,
                    name: e.name.clone(),
                    capacity: e.capacity,
                    location: e.location.clone(),
                    equipment: e.equipment.clone(),
                    requires_approval: e.requires_approval,
                    is_active: true,
                });
                next
            },
            RoomUpdated(e) => {
                let mut next = state;
                if let Some(item) = next.items.get_mut(&e.room_id) {
                    item.name = e.name.clone();
                    item.capacity = e.capacity;
                    item.location = e.location.clone();
                    item.equipment = e.equipment.clone();
                    item.requires_approval = e.requires_approval;
                }
                next
            },
        })
    }
}

impl MultiProjectorQuery for RoomListProjector {
    fn execute_list_query(state: &Self::State, query_type: &str, _params: &str) -> Option<String> {
        if query_type != GetRoomListQuery::QUERY_TYPE {
            return None;
        }
        let mut items: Vec<_> = state.items.values().collect();
        items.sort_by(|left, right| left.name.cmp(&right.name));
        Some(sort_json(&items, "[]"))
    }
}

#[derive(TagProjector)]
#[projector(name = "ReservationProjector", version = "1.0.0")]
pub struct ReservationProjector;

impl Projector for ReservationProjector {
    type State = ReservationState;

    fn event_types() -> Vec<&'static str> {
        vec![
            ReservationDraftCreated::EVENT_TYPE,
            ReservationHoldCommitted::EVENT_TYPE,
            ReservationConfirmed::EVENT_TYPE,
            ReservationCancelled::EVENT_TYPE,
            ReservationRejected::EVENT_TYPE,
        ]
    }

    fn project(state: Self::State, event: &Event) -> Self::State {
        match_event!(event, state, {
            ReservationDraftCreated(e) => ReservationState {
                reservation_id: e.reservation_id,
                room_id: e.room_id,
                organizer_id: e.organizer_id,
                organizer_name: e.organizer_name.clone(),
                start_time: e.start_time.clone(),
                end_time: e.end_time.clone(),
                purpose: e.purpose.clone(),
                selected_equipment: e.selected_equipment.clone(),
                status: "Draft".to_string(),
                requires_approval: false,
                approval_request_id: None,
                approval_request_comment: None,
                approval_decision_comment: None,
                confirmed_at: None,
                reason: None,
            },
            ReservationHoldCommitted(e) => ReservationState {
                reservation_id: e.reservation_id,
                room_id: e.room_id,
                organizer_id: e.organizer_id,
                organizer_name: e.organizer_name.clone(),
                start_time: e.start_time.clone(),
                end_time: e.end_time.clone(),
                purpose: e.purpose.clone(),
                selected_equipment: e.selected_equipment.clone(),
                status: "Held".to_string(),
                requires_approval: e.requires_approval,
                approval_request_id: e.approval_request_id,
                approval_request_comment: e.approval_request_comment.clone(),
                approval_decision_comment: None,
                confirmed_at: None,
                reason: None,
            },
            ReservationConfirmed(e) => ReservationState {
                reservation_id: e.reservation_id,
                room_id: e.room_id,
                organizer_id: e.organizer_id,
                organizer_name: e.organizer_name.clone(),
                start_time: e.start_time.clone(),
                end_time: e.end_time.clone(),
                purpose: e.purpose.clone(),
                selected_equipment: e.selected_equipment.clone(),
                status: "Confirmed".to_string(),
                requires_approval: e.approval_request_id.is_some(),
                approval_request_id: e.approval_request_id,
                approval_request_comment: e.approval_request_comment.clone(),
                approval_decision_comment: e.approval_decision_comment.clone(),
                confirmed_at: Some(e.confirmed_at.clone()),
                reason: None,
            },
            ReservationCancelled(e) => ReservationState {
                reservation_id: e.reservation_id,
                room_id: e.room_id,
                organizer_id: e.organizer_id,
                organizer_name: e.organizer_name.clone(),
                start_time: e.start_time.clone(),
                end_time: e.end_time.clone(),
                purpose: e.purpose.clone(),
                selected_equipment: e.selected_equipment.clone(),
                status: "Cancelled".to_string(),
                requires_approval: false,
                approval_request_id: None,
                approval_request_comment: e.approval_request_comment.clone(),
                approval_decision_comment: Some(e.reason.clone()),
                confirmed_at: None,
                reason: Some(e.reason.clone()),
            },
            ReservationRejected(e) => ReservationState {
                reservation_id: e.reservation_id,
                room_id: e.room_id,
                organizer_id: e.organizer_id,
                organizer_name: e.organizer_name.clone(),
                start_time: e.start_time.clone(),
                end_time: e.end_time.clone(),
                purpose: e.purpose.clone(),
                selected_equipment: e.selected_equipment.clone(),
                status: "Rejected".to_string(),
                requires_approval: true,
                approval_request_id: Some(e.approval_request_id),
                approval_request_comment: e.approval_request_comment.clone(),
                approval_decision_comment: Some(e.reason.clone()),
                confirmed_at: None,
                reason: Some(e.reason.clone()),
            },
        })
    }
}

#[derive(MultiProjector)]
#[projector(name = "ReservationListProjection", version = "1.0.0")]
pub struct ReservationListProjector;

impl Projector for ReservationListProjector {
    type State = ReservationListState;

    fn event_types() -> Vec<&'static str> {
        ReservationProjector::event_types()
    }

    fn project(state: Self::State, event: &Event) -> Self::State {
        match_event!(event, state, {
            ReservationDraftCreated(e) => {
                let mut next = state;
                next.items.insert(e.reservation_id, ReservationListItem {
                    reservation_id: e.reservation_id,
                    room_id: e.room_id,
                    organizer_id: e.organizer_id,
                    organizer_name: e.organizer_name.clone(),
                    start_time: e.start_time.clone(),
                    end_time: e.end_time.clone(),
                    purpose: e.purpose.clone(),
                    selected_equipment: e.selected_equipment.clone(),
                    status: "Draft".to_string(),
                    requires_approval: false,
                    approval_request_id: None,
                    approval_request_comment: None,
                    approval_decision_comment: None,
                });
                next
            },
            ReservationHoldCommitted(e) => {
                let mut next = state;
                if let Some(item) = next.items.get_mut(&e.reservation_id) {
                    item.status = "Held".to_string();
                    item.requires_approval = e.requires_approval;
                    item.approval_request_id = e.approval_request_id;
                    item.approval_request_comment = e.approval_request_comment.clone();
                }
                next
            },
            ReservationConfirmed(e) => {
                let mut next = state;
                if let Some(item) = next.items.get_mut(&e.reservation_id) {
                    item.status = "Confirmed".to_string();
                    item.requires_approval = e.approval_request_id.is_some();
                    item.approval_request_id = e.approval_request_id;
                    item.approval_request_comment = e.approval_request_comment.clone();
                    item.approval_decision_comment = e.approval_decision_comment.clone();
                }
                next
            },
            ReservationCancelled(e) => {
                let mut next = state;
                if let Some(item) = next.items.get_mut(&e.reservation_id) {
                    item.status = "Cancelled".to_string();
                    item.requires_approval = false;
                    item.approval_request_id = None;
                    item.approval_decision_comment = Some(e.reason.clone());
                    item.approval_request_comment = e.approval_request_comment.clone();
                }
                next
            },
            ReservationRejected(e) => {
                let mut next = state;
                if let Some(item) = next.items.get_mut(&e.reservation_id) {
                    item.status = "Rejected".to_string();
                    item.requires_approval = true;
                    item.approval_request_id = Some(e.approval_request_id);
                    item.approval_request_comment = e.approval_request_comment.clone();
                    item.approval_decision_comment = Some(e.reason.clone());
                }
                next
            },
        })
    }
}

impl MultiProjectorQuery for ReservationListProjector {
    fn execute_list_query(state: &Self::State, query_type: &str, params: &str) -> Option<String> {
        if query_type != GetReservationListQuery::QUERY_TYPE {
            return None;
        }
        let query: GetReservationListQuery = serde_json::from_str(params).unwrap_or_default();
        let mut items: Vec<_> = state
            .items
            .values()
            .filter(|item| query.room_id.is_none_or(|room_id| item.room_id == room_id))
            .collect();
        items.sort_by(|left, right| left.start_time.cmp(&right.start_time));
        Some(sort_json(&items, "[]"))
    }
}

#[derive(TagProjector)]
#[projector(name = "ApprovalRequestProjector", version = "1.0.0")]
pub struct ApprovalRequestProjector;

impl Projector for ApprovalRequestProjector {
    type State = ApprovalRequestState;

    fn event_types() -> Vec<&'static str> {
        vec![
            ApprovalFlowStarted::EVENT_TYPE,
            ApprovalDecisionRecorded::EVENT_TYPE,
        ]
    }

    fn project(state: Self::State, event: &Event) -> Self::State {
        match_event!(event, state, {
            ApprovalFlowStarted(e) => ApprovalRequestState {
                approval_request_id: e.approval_request_id,
                reservation_id: e.reservation_id,
                room_id: e.room_id,
                requester_id: e.requester_id,
                approver_ids: e.approver_ids.clone(),
                requested_at: e.requested_at.clone(),
                request_comment: e.request_comment.clone(),
                status: "Pending".to_string(),
            },
            ApprovalDecisionRecorded(e) => {
                let mut next = state;
                next.status = match e.decision {
                    ApprovalDecision::Approved => "Approved",
                    ApprovalDecision::Rejected => "Rejected",
                }
                .to_string();
                next
            },
        })
    }
}

#[derive(MultiProjector)]
#[projector(name = "ApprovalRequestListProjection", version = "1.0.0")]
pub struct ApprovalInboxProjector;

impl Projector for ApprovalInboxProjector {
    type State = ApprovalInboxState;

    fn event_types() -> Vec<&'static str> {
        ApprovalRequestProjector::event_types()
    }

    fn project(state: Self::State, event: &Event) -> Self::State {
        match_event!(event, state, {
            ApprovalFlowStarted(e) => {
                let mut next = state;
                next.items.insert(e.approval_request_id, ApprovalInboxItem {
                    approval_request_id: e.approval_request_id,
                    reservation_id: e.reservation_id,
                    room_id: e.room_id,
                    requester_id: e.requester_id,
                    request_comment: e.request_comment.clone(),
                    approver_ids: e.approver_ids.clone(),
                    requested_at: e.requested_at.clone(),
                    status: "Pending".to_string(),
                });
                next
            },
            ApprovalDecisionRecorded(e) => {
                let mut next = state;
                if let Some(item) = next.items.get_mut(&e.approval_request_id) {
                    item.status = match e.decision {
                        ApprovalDecision::Approved => "Approved",
                        ApprovalDecision::Rejected => "Rejected",
                    }
                    .to_string();
                    item.requested_at = e.decided_at.clone();
                }
                next
            },
        })
    }
}

impl MultiProjectorQuery for ApprovalInboxProjector {
    fn execute_list_query(state: &Self::State, query_type: &str, params: &str) -> Option<String> {
        if query_type != GetApprovalInboxQuery::QUERY_TYPE {
            return None;
        }
        let query: GetApprovalInboxQuery = serde_json::from_str(params).unwrap_or_default();
        let mut items: Vec<_> = state
            .items
            .values()
            .filter(|item| !query.pending_only || item.status == "Pending")
            .collect();
        items.sort_by(|left, right| right.requested_at.cmp(&left.requested_at));
        Some(sort_json(&items, "[]"))
    }
}
