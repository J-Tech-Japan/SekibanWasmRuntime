use chrono::NaiveDate;
use sekiban_derive::Tag;
use serde::{Deserialize, Serialize};
use uuid::Uuid;

#[derive(Debug, Clone, Serialize, Deserialize, Tag)]
#[tag(group = "weather")]
pub struct WeatherForecastTag {
    pub forecast_id: Uuid,
}

impl WeatherForecastTag {
    pub fn new(forecast_id: Uuid) -> Self {
        Self { forecast_id }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, Tag)]
#[tag(group = "Student")]
pub struct StudentTag {
    pub student_id: Uuid,
}

#[derive(Debug, Clone, Serialize, Deserialize, Tag)]
#[tag(group = "ClassRoom")]
pub struct ClassRoomTag {
    pub class_room_id: Uuid,
}

#[derive(Debug, Clone, Serialize, Deserialize, Tag)]
#[tag(group = "User")]
pub struct UserTag {
    pub user_id: Uuid,
}

#[derive(Debug, Clone, Serialize, Deserialize, Tag)]
#[tag(group = "UserAccess")]
pub struct UserAccessTag {
    pub user_id: Uuid,
}

#[derive(Debug, Clone, Serialize, Deserialize, Tag)]
#[tag(group = "Room")]
pub struct RoomTag {
    pub room_id: Uuid,
}

#[derive(Debug, Clone, Serialize, Deserialize, Tag)]
#[tag(group = "Reservation")]
pub struct ReservationTag {
    pub reservation_id: Uuid,
}

#[derive(Debug, Clone, Serialize, Deserialize, Tag)]
#[tag(group = "RoomReservation")]
pub struct RoomReservationTag {
    pub room_id: Uuid,
}

#[derive(Debug, Clone, Serialize, Deserialize, Tag)]
#[tag(group = "ApprovalRequest")]
pub struct ApprovalRequestTag {
    pub approval_request_id: Uuid,
}

#[derive(Debug, Clone, Serialize, Deserialize, Tag)]
#[tag(group = "RoomDailyActivity")]
pub struct RoomDailyActivityTag {
    pub room_id: Uuid,
    pub date: String,
}

impl RoomDailyActivityTag {
    pub fn from_range(room_id: Uuid, start_time: chrono::DateTime<chrono::Utc>, end_time: chrono::DateTime<chrono::Utc>) -> Vec<Self> {
        let mut current = start_time.date_naive();
        let end = end_time.date_naive();
        let mut tags = Vec::new();
        while current <= end {
            tags.push(Self {
                room_id,
                date: current.format("%Y-%m-%d").to_string(),
            });
            current = match current.succ_opt() {
                Some(next) => next,
                None => break,
            };
        }
        if tags.is_empty() {
            tags.push(Self {
                room_id,
                date: start_time.date_naive().format("%Y-%m-%d").to_string(),
            });
        }
        tags
    }

    pub fn parse_date(&self) -> Option<NaiveDate> {
        NaiveDate::parse_from_str(&self.date, "%Y-%m-%d").ok()
    }
}
