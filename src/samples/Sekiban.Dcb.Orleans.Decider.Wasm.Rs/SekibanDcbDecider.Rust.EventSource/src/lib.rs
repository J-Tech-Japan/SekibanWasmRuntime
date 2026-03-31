pub mod commands;
pub mod events;
pub mod projectors;
pub mod queries;
pub mod states;
pub mod tags;

pub use commands::*;
pub use events::*;
pub use projectors::*;
pub use queries::*;
pub use states::*;
pub use tags::*;

use sekiban_core::prelude::*;

domain_types!(DeciderDomain {
    events: [
        WeatherForecastCreated,
        WeatherForecastLocationUpdated,
        WeatherForecastDeleted,
        StudentCreated,
        ClassRoomCreated,
        StudentEnrolledInClassRoom,
        StudentDroppedFromClassRoom,
        UserRegistered,
        UserProfileUpdated,
        UserAccessGranted,
        UserRoleGranted,
        RoomCreated,
        RoomUpdated,
        ReservationDraftCreated,
        ReservationHoldCommitted,
        ReservationConfirmed,
        ReservationCancelled,
        ReservationRejected,
        ApprovalFlowStarted,
        ApprovalDecisionRecorded,
    ],
    tags: [
        WeatherForecastTag,
        StudentTag,
        ClassRoomTag,
        UserTag,
        UserAccessTag,
        RoomTag,
        ReservationTag,
        ApprovalRequestTag,
    ],
    tag_projectors: [
        WeatherForecastProjector,
        StudentProjector,
        ClassRoomProjector,
        UserDirectoryProjector,
        UserAccessProjector,
        RoomProjector,
        ReservationProjector,
        ApprovalRequestProjector,
    ],
    multi_projectors: [
        WeatherForecastListProjector,
        StudentListProjector,
        ClassRoomListProjector,
        UserDirectoryListProjector,
        UserAccessListProjector,
        RoomListProjector,
        ReservationListProjector,
        ApprovalInboxProjector,
    ],
    commands: [
        CreateWeatherForecast,
        UpdateWeatherForecastLocation,
        DeleteWeatherForecast,
        CreateStudent,
        CreateClassRoom,
        EnrollStudentInClassRoom,
        DropStudentFromClassRoom,
        RegisterUser,
        UpdateUserMonthlyReservationLimit,
        GrantUserAccess,
        GrantUserRole,
        CreateRoom,
        UpdateRoom,
        CreateReservationDraft,
        CommitReservationHold,
        ConfirmReservation,
        CancelReservation,
        RejectReservation,
        StartApprovalFlow,
        RecordApprovalDecision,
    ],
    queries: [GetWeatherForecastCountQuery,],
    list_queries: [
        GetWeatherForecastListQuery,
        GetStudentListQuery,
        GetClassRoomListQuery,
        GetRoomListQuery,
        GetReservationListQuery,
        GetApprovalInboxQuery,
        GetUserDirectoryListQuery,
        GetUserAccessListQuery,
    ],
});
