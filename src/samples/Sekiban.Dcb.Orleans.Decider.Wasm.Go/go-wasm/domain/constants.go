package domain

const (
	// Weather events
	EventWeatherForecastCreated         = "WeatherForecastCreated"
	EventWeatherForecastLocationUpdated = "WeatherForecastLocationUpdated"
	EventWeatherForecastDeleted         = "WeatherForecastDeleted"

	// Student/ClassRoom events
	EventStudentCreated             = "StudentCreated"
	EventClassRoomCreated           = "ClassRoomCreated"
	EventStudentEnrolledInClassRoom = "StudentEnrolledInClassRoom"
	EventStudentDroppedFromClassRoom = "StudentDroppedFromClassRoom"

	// User events
	EventUserRegistered    = "UserRegistered"
	EventUserProfileUpdated = "UserProfileUpdated"
	EventUserAccessGranted = "UserAccessGranted"
	EventUserRoleGranted   = "UserRoleGranted"

	// Room events
	EventRoomCreated = "RoomCreated"
	EventRoomUpdated = "RoomUpdated"

	// Reservation events
	EventReservationDraftCreated   = "ReservationDraftCreated"
	EventReservationHoldCommitted  = "ReservationHoldCommitted"
	EventReservationConfirmed      = "ReservationConfirmed"
	EventReservationCancelled      = "ReservationCancelled"
	EventReservationRejected       = "ReservationRejected"

	// Approval events
	EventApprovalFlowStarted       = "ApprovalFlowStarted"
	EventApprovalDecisionRecorded  = "ApprovalDecisionRecorded"
)

const (
	TagGroupWeather         = "weather"
	TagGroupStudent         = "Student"
	TagGroupClassRoom       = "ClassRoom"
	TagGroupUser            = "User"
	TagGroupUserAccess      = "UserAccess"
	TagGroupRoom            = "Room"
	TagGroupRoomReservation = "RoomReservation"
	TagGroupReservation     = "Reservation"
	TagGroupApprovalRequest = "ApprovalRequest"
)

const (
	ProjectorWeatherTag         = "WeatherForecastProjector"
	ProjectorStudentTag         = "StudentProjector"
	ProjectorClassRoomTag       = "ClassRoomProjector"
	ProjectorUserDirectoryTag   = "UserDirectoryProjector"
	ProjectorUserAccessTag      = "UserAccessProjector"
	ProjectorRoomTag            = "RoomProjector"
	ProjectorRoomReservationsTag = "RoomReservationsProjector"
	ProjectorReservationTag     = "ReservationProjector"
	ProjectorApprovalRequestTag = "ApprovalRequestProjector"

	ProjectorWeatherList         = "WeatherForecastMultiProjection"
	ProjectorStudentList         = "StudentListProjection"
	ProjectorClassRoomList       = "ClassRoomListProjection"
	ProjectorUserDirectoryList   = "UserDirectoryListProjection"
	ProjectorUserAccessList      = "UserAccessListProjection"
	ProjectorRoomList            = "RoomListProjection"
	ProjectorReservationList     = "ReservationListProjection"
	ProjectorApprovalRequestList = "ApprovalRequestListProjection"
)

// TagProjectorMap maps tag groups to their projector names.
var TagProjectorMap = map[string]string{
	TagGroupWeather:         ProjectorWeatherTag,
	TagGroupStudent:         ProjectorStudentTag,
	TagGroupClassRoom:       ProjectorClassRoomTag,
	TagGroupUser:            ProjectorUserDirectoryTag,
	TagGroupUserAccess:      ProjectorUserAccessTag,
	TagGroupRoom:            ProjectorRoomTag,
	TagGroupRoomReservation: ProjectorRoomReservationsTag,
	TagGroupReservation:     ProjectorReservationTag,
	TagGroupApprovalRequest: ProjectorApprovalRequestTag,
}
