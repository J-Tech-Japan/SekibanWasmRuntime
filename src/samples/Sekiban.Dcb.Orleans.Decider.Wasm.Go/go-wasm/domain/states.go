package domain

import "strings"

// Weather states
type WeatherForecastState struct {
	ForecastId   string  `json:"forecastId"`
	Location     string  `json:"location"`
	Date         string  `json:"date,omitempty"`
	TemperatureC int     `json:"temperatureC"`
	Summary      string  `json:"summary"`
	CreatedAt    string  `json:"createdAt"`
	IsDeleted    bool    `json:"isDeleted"`
	DeletedAt    *string `json:"deletedAt"`
}

func (s WeatherForecastState) IsEmpty() bool { return strings.TrimSpace(s.ForecastId) == "" }

type WeatherForecastItem struct {
	ForecastId   string  `json:"forecastId"`
	Location     string  `json:"location"`
	Date         string  `json:"date,omitempty"`
	TemperatureC int     `json:"temperatureC"`
	Summary      string  `json:"summary"`
	CreatedAt    string  `json:"createdAt"`
	IsDeleted    bool    `json:"isDeleted"`
	DeletedAt    *string `json:"deletedAt"`
}

type WeatherForecastListState struct {
	Items map[string]WeatherForecastItem `json:"items"`
}

// Student states
type StudentState struct {
	StudentId            string   `json:"studentId"`
	Name                 string   `json:"name"`
	MaxClassCount        int      `json:"maxClassCount"`
	EnrolledClassRoomIds []string `json:"enrolledClassRoomIds"`
}

func (s StudentState) IsEmpty() bool { return strings.TrimSpace(s.StudentId) == "" }
func (s StudentState) Remaining() int { return s.MaxClassCount - len(s.EnrolledClassRoomIds) }

type StudentListState struct {
	Items map[string]StudentState `json:"items"`
}

// ClassRoom states
type ClassRoomState struct {
	ClassRoomId        string   `json:"classRoomId"`
	Name               string   `json:"name"`
	MaxStudents        int      `json:"maxStudents"`
	EnrolledStudentIds []string `json:"enrolledStudentIds"`
	IsFull             bool     `json:"isFull"`
}

func (s ClassRoomState) IsEmpty() bool { return strings.TrimSpace(s.ClassRoomId) == "" }
func (s ClassRoomState) Remaining() int {
	if s.MaxStudents <= 0 {
		return 0
	}
	return s.MaxStudents - len(s.EnrolledStudentIds)
}

type ClassRoomItem struct {
	ClassRoomId       string `json:"classRoomId"`
	Name              string `json:"name"`
	MaxStudents       int    `json:"maxStudents"`
	EnrolledCount     int    `json:"enrolledCount"`
	IsFull            bool   `json:"isFull"`
	RemainingCapacity int    `json:"remainingCapacity"`
}

type ClassRoomListState struct {
	Items map[string]ClassRoomItem `json:"items"`
}

// User states
type UserDirectoryState struct {
	UserId                  string   `json:"userId"`
	DisplayName             string   `json:"displayName"`
	Email                   string   `json:"email"`
	Department              *string  `json:"department"`
	RegisteredAt            string   `json:"registeredAt"`
	MonthlyReservationLimit int      `json:"monthlyReservationLimit"`
	ExternalProviders       []string `json:"externalProviders"`
	IsActive                bool     `json:"isActive"`
}

func (s UserDirectoryState) IsEmpty() bool { return strings.TrimSpace(s.UserId) == "" }

type UserDirectoryListItem struct {
	UserId                  string   `json:"userId"`
	DisplayName             string   `json:"displayName"`
	Email                   string   `json:"email"`
	Department              *string  `json:"department"`
	RegisteredAt            string   `json:"registeredAt"`
	MonthlyReservationLimit int      `json:"monthlyReservationLimit"`
	Roles                   []string `json:"roles"`
	IsActive                bool     `json:"isActive"`
}

type UserDirectoryListState struct {
	Items map[string]UserDirectoryListItem `json:"items"`
}

type UserAccessState struct {
	UserId    string   `json:"userId"`
	Roles     []string `json:"roles"`
	GrantedAt string   `json:"grantedAt"`
	IsActive  bool     `json:"isActive"`
}

func (s UserAccessState) IsEmpty() bool { return strings.TrimSpace(s.UserId) == "" }

type UserAccessListItem struct {
	UserId    string   `json:"userId"`
	Roles     []string `json:"roles"`
	GrantedAt string   `json:"grantedAt"`
	IsActive  bool     `json:"isActive"`
}

type UserAccessListState struct {
	Items map[string]UserAccessListItem `json:"items"`
}

// Room states
type RoomState struct {
	RoomId           string   `json:"roomId"`
	Name             string   `json:"name"`
	Capacity         int      `json:"capacity"`
	Location         string   `json:"location"`
	Equipment        []string `json:"equipment"`
	RequiresApproval bool     `json:"requiresApproval"`
	IsActive         bool     `json:"isActive"`
}

func (s RoomState) IsEmpty() bool { return strings.TrimSpace(s.RoomId) == "" }

type RoomListItem struct {
	RoomId           string   `json:"roomId"`
	Name             string   `json:"name"`
	Capacity         int      `json:"capacity"`
	Location         string   `json:"location"`
	Equipment        []string `json:"equipment"`
	RequiresApproval bool     `json:"requiresApproval"`
	IsActive         bool     `json:"isActive"`
}

type RoomListState struct {
	Items map[string]RoomListItem `json:"items"`
}

// Reservation states
type ReservationState struct {
	ReservationId          string   `json:"reservationId"`
	RoomId                 string   `json:"roomId"`
	OrganizerId            string   `json:"organizerId"`
	OrganizerName          string   `json:"organizerName"`
	StartTime              string   `json:"startTime"`
	EndTime                string   `json:"endTime"`
	Purpose                string   `json:"purpose"`
	SelectedEquipment      []string `json:"selectedEquipment"`
	Status                 string   `json:"status"`
	RequiresApproval       bool     `json:"requiresApproval"`
	ApprovalRequestId      *string  `json:"approvalRequestId"`
	ApprovalRequestComment *string  `json:"approvalRequestComment"`
	ApprovalDecisionComment *string `json:"approvalDecisionComment"`
	ConfirmedAt            *string  `json:"confirmedAt"`
	Reason                 *string  `json:"reason"`
	CancelReason           *string  `json:"cancelReason"`
	CancelledAt            *string  `json:"cancelledAt"`
	RejectReason           *string  `json:"rejectReason"`
	RejectedAt             *string  `json:"rejectedAt"`
}

func (s ReservationState) IsEmpty() bool { return strings.TrimSpace(s.ReservationId) == "" }

type ReservationListItem struct {
	ReservationId     string   `json:"reservationId"`
	RoomId            string   `json:"roomId"`
	OrganizerId       string   `json:"organizerId"`
	OrganizerName     string   `json:"organizerName"`
	StartTime         string   `json:"startTime"`
	EndTime           string   `json:"endTime"`
	Purpose           string   `json:"purpose"`
	SelectedEquipment  []string `json:"selectedEquipment"`
	Status             string   `json:"status"`
	RequiresApproval   bool     `json:"requiresApproval"`
	ApprovalRequestId  *string  `json:"approvalRequestId"`
	ConfirmedAt        *string  `json:"confirmedAt"`
	CancelledAt        *string  `json:"cancelledAt"`
	RejectedAt         *string  `json:"rejectedAt"`
}

type ReservationListState struct {
	Items map[string]ReservationListItem `json:"items"`
}

// Approval states
type ApprovalRequestState struct {
	ApprovalRequestId string   `json:"approvalRequestId"`
	ReservationId     string   `json:"reservationId"`
	RoomId            string   `json:"roomId"`
	RequesterId       string   `json:"requesterId"`
	ApproverIds       []string `json:"approverIds"`
	RequestedAt       string   `json:"requestedAt"`
	RequestComment    *string  `json:"requestComment"`
	Status            string   `json:"status"`
	ApproverId        *string  `json:"approverId"`
	DecisionComment   *string  `json:"decisionComment"`
	DecidedAt         string   `json:"decidedAt"`
}

func (s ApprovalRequestState) IsEmpty() bool { return strings.TrimSpace(s.ApprovalRequestId) == "" }

type ApprovalInboxItem struct {
	ApprovalRequestId string   `json:"approvalRequestId"`
	ReservationId     string   `json:"reservationId"`
	RoomId            string   `json:"roomId"`
	RequesterId       string   `json:"requesterId"`
	ApproverIds       []string `json:"approverIds"`
	RequestedAt       string   `json:"requestedAt"`
	RequestComment    *string  `json:"requestComment"`
	Status            string   `json:"status"`
	ApproverId        *string  `json:"approverId"`
	DecisionComment   *string  `json:"decisionComment"`
	DecidedAt         string   `json:"decidedAt"`
}

type ApprovalRequestListState struct {
	Items map[string]ApprovalInboxItem `json:"items"`
}
