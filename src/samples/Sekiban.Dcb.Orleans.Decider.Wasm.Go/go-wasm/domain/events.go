package domain

// Weather events
type WeatherForecastCreated struct {
	ForecastId   string `json:"forecastId"`
	Location     string `json:"location"`
	Date         string `json:"date,omitempty"`
	TemperatureC int    `json:"temperatureC"`
	Summary      string `json:"summary"`
	CreatedAt    string `json:"createdAt"`
}

type WeatherForecastLocationUpdated struct {
	ForecastId  string `json:"forecastId"`
	NewLocation string `json:"newLocation"`
	UpdatedAt   string `json:"updatedAt"`
}

type WeatherForecastDeleted struct {
	ForecastId string `json:"forecastId"`
	DeletedAt  string `json:"deletedAt"`
}

// Student/ClassRoom events
type StudentCreated struct {
	StudentId     string `json:"studentId"`
	Name          string `json:"name"`
	MaxClassCount int    `json:"maxClassCount"`
}

type ClassRoomCreated struct {
	ClassRoomId string `json:"classRoomId"`
	Name        string `json:"name"`
	MaxStudents int    `json:"maxStudents"`
}

type StudentEnrolledInClassRoom struct {
	StudentId   string `json:"studentId"`
	ClassRoomId string `json:"classRoomId"`
}

type StudentDroppedFromClassRoom struct {
	StudentId   string `json:"studentId"`
	ClassRoomId string `json:"classRoomId"`
}

// User events
type UserRegistered struct {
	UserId                   string  `json:"userId"`
	DisplayName              string  `json:"displayName"`
	Email                    string  `json:"email"`
	Department               *string `json:"department"`
	RegisteredAt             string  `json:"registeredAt"`
	MonthlyReservationLimit  int     `json:"monthlyReservationLimit"`
}

type UserProfileUpdated struct {
	UserId                   string  `json:"userId"`
	DisplayName              string  `json:"displayName"`
	Email                    string  `json:"email"`
	Department               *string `json:"department"`
	MonthlyReservationLimit  int     `json:"monthlyReservationLimit"`
}

type UserAccessGranted struct {
	UserId      string `json:"userId"`
	InitialRole string `json:"initialRole"`
	GrantedAt   string `json:"grantedAt"`
}

type UserRoleGranted struct {
	UserId    string `json:"userId"`
	Role      string `json:"role"`
	GrantedAt string `json:"grantedAt"`
}

// Room events
type RoomCreated struct {
	RoomId           string   `json:"roomId"`
	Name             string   `json:"name"`
	Capacity         int      `json:"capacity"`
	Location         string   `json:"location"`
	Equipment        []string `json:"equipment"`
	RequiresApproval bool     `json:"requiresApproval"`
}

type RoomUpdated struct {
	RoomId           string   `json:"roomId"`
	Name             string   `json:"name"`
	Capacity         int      `json:"capacity"`
	Location         string   `json:"location"`
	Equipment        []string `json:"equipment"`
	RequiresApproval bool     `json:"requiresApproval"`
}

// Reservation events
type ReservationDraftCreated struct {
	ReservationId     string   `json:"reservationId"`
	RoomId            string   `json:"roomId"`
	OrganizerId       string   `json:"organizerId"`
	OrganizerName     string   `json:"organizerName"`
	StartTime         string   `json:"startTime"`
	EndTime           string   `json:"endTime"`
	Purpose           string   `json:"purpose"`
	SelectedEquipment []string `json:"selectedEquipment"`
}

type ReservationHoldCommitted struct {
	ReservationId          string   `json:"reservationId"`
	RoomId                 string   `json:"roomId"`
	OrganizerId            string   `json:"organizerId"`
	OrganizerName          string   `json:"organizerName"`
	StartTime              string   `json:"startTime"`
	EndTime                string   `json:"endTime"`
	Purpose                string   `json:"purpose"`
	SelectedEquipment      []string `json:"selectedEquipment"`
	RequiresApproval       bool     `json:"requiresApproval"`
	ApprovalRequestId      *string  `json:"approvalRequestId"`
	ApprovalRequestComment *string  `json:"approvalRequestComment"`
}

type ReservationConfirmed struct {
	ReservationId          string   `json:"reservationId"`
	RoomId                 string   `json:"roomId"`
	OrganizerId            string   `json:"organizerId"`
	OrganizerName          string   `json:"organizerName"`
	StartTime              string   `json:"startTime"`
	EndTime                string   `json:"endTime"`
	Purpose                string   `json:"purpose"`
	SelectedEquipment      []string `json:"selectedEquipment"`
	ConfirmedAt            string   `json:"confirmedAt"`
	ApprovalRequestId      *string  `json:"approvalRequestId"`
	ApprovalRequestComment *string  `json:"approvalRequestComment"`
	ApprovalDecisionComment *string `json:"approvalDecisionComment"`
}

type ReservationCancelled struct {
	ReservationId          string   `json:"reservationId"`
	RoomId                 string   `json:"roomId"`
	OrganizerId            string   `json:"organizerId"`
	OrganizerName          string   `json:"organizerName"`
	StartTime              string   `json:"startTime"`
	EndTime                string   `json:"endTime"`
	Purpose                string   `json:"purpose"`
	SelectedEquipment      []string `json:"selectedEquipment"`
	ApprovalRequestComment *string  `json:"approvalRequestComment"`
	Reason                 string   `json:"reason"`
	CancelledAt            string   `json:"cancelledAt"`
}

type ReservationRejected struct {
	ReservationId          string   `json:"reservationId"`
	RoomId                 string   `json:"roomId"`
	OrganizerId            string   `json:"organizerId"`
	OrganizerName          string   `json:"organizerName"`
	StartTime              string   `json:"startTime"`
	EndTime                string   `json:"endTime"`
	Purpose                string   `json:"purpose"`
	SelectedEquipment      []string `json:"selectedEquipment"`
	ApprovalRequestId      string   `json:"approvalRequestId"`
	ApprovalRequestComment *string  `json:"approvalRequestComment"`
	Reason                 string   `json:"reason"`
	RejectedAt             string   `json:"rejectedAt"`
}

// Approval events
type ApprovalFlowStarted struct {
	ApprovalRequestId string   `json:"approvalRequestId"`
	ReservationId     string   `json:"reservationId"`
	RoomId            string   `json:"roomId"`
	RequesterId       string   `json:"requesterId"`
	ApproverIds       []string `json:"approverIds"`
	RequestedAt       string   `json:"requestedAt"`
	RequestComment    *string  `json:"requestComment"`
}

type ApprovalDecisionRecorded struct {
	ApprovalRequestId string  `json:"approvalRequestId"`
	ReservationId     string  `json:"reservationId"`
	ApproverId        string  `json:"approverId"`
	Decision          string  `json:"decision"`
	Comment           *string `json:"comment"`
	DecidedAt         string  `json:"decidedAt"`
}
