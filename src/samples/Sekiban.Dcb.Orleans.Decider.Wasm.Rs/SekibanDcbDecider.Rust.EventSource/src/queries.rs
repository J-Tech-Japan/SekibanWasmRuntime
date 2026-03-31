use sekiban_core::prelude::*;
use serde::{Deserialize, Serialize};
use uuid::Uuid;

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct LocationQuery {
    pub location_filter: Option<String>,
    pub forecast_id: Option<Uuid>,
}

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct GetWeatherForecastListQuery {
    pub location_filter: Option<String>,
    pub forecast_id: Option<Uuid>,
    pub wait_for_sortable_unique_id: Option<String>,
    pub page_number: Option<i32>,
    pub page_size: Option<i32>,
}

impl ListQuery for GetWeatherForecastListQuery {
    const QUERY_TYPE: &'static str = "GetWeatherForecastListQuery";

    fn wait_for_sortable_id(&self) -> Option<&str> {
        self.wait_for_sortable_unique_id.as_deref()
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct GetWeatherForecastCountQuery {
    pub location_filter: Option<String>,
    pub forecast_id: Option<Uuid>,
    pub wait_for_sortable_unique_id: Option<String>,
}

impl Query for GetWeatherForecastCountQuery {
    const QUERY_TYPE: &'static str = "GetWeatherForecastCountQuery";

    fn wait_for_sortable_id(&self) -> Option<&str> {
        self.wait_for_sortable_unique_id.as_deref()
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct GetStudentListQuery {
    pub page_number: Option<i32>,
    pub page_size: Option<i32>,
    pub wait_for_sortable_unique_id: Option<String>,
}

impl ListQuery for GetStudentListQuery {
    const QUERY_TYPE: &'static str = "GetStudentListQuery";

    fn wait_for_sortable_id(&self) -> Option<&str> {
        self.wait_for_sortable_unique_id.as_deref()
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct GetClassRoomListQuery {
    pub page_number: Option<i32>,
    pub page_size: Option<i32>,
    pub wait_for_sortable_unique_id: Option<String>,
}

impl ListQuery for GetClassRoomListQuery {
    const QUERY_TYPE: &'static str = "GetClassRoomListQuery";

    fn wait_for_sortable_id(&self) -> Option<&str> {
        self.wait_for_sortable_unique_id.as_deref()
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct GetRoomListQuery {
    pub page_number: Option<i32>,
    pub page_size: Option<i32>,
    pub wait_for_sortable_unique_id: Option<String>,
}

impl ListQuery for GetRoomListQuery {
    const QUERY_TYPE: &'static str = "GetRoomListQuery";

    fn wait_for_sortable_id(&self) -> Option<&str> {
        self.wait_for_sortable_unique_id.as_deref()
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct GetReservationListQuery {
    pub page_number: Option<i32>,
    pub page_size: Option<i32>,
    pub wait_for_sortable_unique_id: Option<String>,
    pub room_id: Option<Uuid>,
}

impl ListQuery for GetReservationListQuery {
    const QUERY_TYPE: &'static str = "GetReservationListQuery";

    fn wait_for_sortable_id(&self) -> Option<&str> {
        self.wait_for_sortable_unique_id.as_deref()
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct GetApprovalInboxQuery {
    pub page_number: Option<i32>,
    pub page_size: Option<i32>,
    pub wait_for_sortable_unique_id: Option<String>,
    pub pending_only: bool,
}

impl Default for GetApprovalInboxQuery {
    fn default() -> Self {
        Self {
            page_number: None,
            page_size: None,
            wait_for_sortable_unique_id: None,
            pending_only: true,
        }
    }
}

impl ListQuery for GetApprovalInboxQuery {
    const QUERY_TYPE: &'static str = "GetApprovalInboxQuery";

    fn wait_for_sortable_id(&self) -> Option<&str> {
        self.wait_for_sortable_unique_id.as_deref()
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct GetUserDirectoryListQuery {
    pub page_number: Option<i32>,
    pub page_size: Option<i32>,
    pub wait_for_sortable_unique_id: Option<String>,
    pub active_only: bool,
}

impl Default for GetUserDirectoryListQuery {
    fn default() -> Self {
        Self {
            page_number: None,
            page_size: None,
            wait_for_sortable_unique_id: None,
            active_only: false,
        }
    }
}

impl ListQuery for GetUserDirectoryListQuery {
    const QUERY_TYPE: &'static str = "GetUserDirectoryListQuery";

    fn wait_for_sortable_id(&self) -> Option<&str> {
        self.wait_for_sortable_unique_id.as_deref()
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct GetUserAccessListQuery {
    pub page_number: Option<i32>,
    pub page_size: Option<i32>,
    pub wait_for_sortable_unique_id: Option<String>,
    pub active_only: bool,
    pub role_filter: Option<String>,
}

impl Default for GetUserAccessListQuery {
    fn default() -> Self {
        Self {
            page_number: None,
            page_size: None,
            wait_for_sortable_unique_id: None,
            active_only: false,
            role_filter: None,
        }
    }
}

impl ListQuery for GetUserAccessListQuery {
    const QUERY_TYPE: &'static str = "GetUserAccessListQuery";

    fn wait_for_sortable_id(&self) -> Option<&str> {
        self.wait_for_sortable_unique_id.as_deref()
    }
}
