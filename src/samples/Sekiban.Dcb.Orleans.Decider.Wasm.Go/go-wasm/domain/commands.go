package domain

import (
	"encoding/json"
	"fmt"
	"time"

	sekiban "github.com/J-Tech-Japan/sekiban-go/domain"
	"github.com/google/uuid"
)

// ── helpers ──────────────────────────────────────────────────────────────────

func nowIso() string { return time.Now().UTC().Format(time.RFC3339Nano) }
func newID() string  { return uuid.New().String() }

func parseWeatherState(raw string) WeatherForecastState {
	var s WeatherForecastState
	if sekiban.IsEmptyJSON(raw) {
		return s
	}
	_ = json.Unmarshal([]byte(raw), &s)
	return s
}

func parseStudentState(raw string) StudentState {
	var s StudentState
	if sekiban.IsEmptyJSON(raw) {
		return s
	}
	_ = json.Unmarshal([]byte(raw), &s)
	return s
}

func parseClassRoomState(raw string) ClassRoomState {
	var s ClassRoomState
	if sekiban.IsEmptyJSON(raw) {
		return s
	}
	_ = json.Unmarshal([]byte(raw), &s)
	return s
}

func parseUserDirectoryState(raw string) UserDirectoryState {
	var s UserDirectoryState
	if sekiban.IsEmptyJSON(raw) {
		return s
	}
	_ = json.Unmarshal([]byte(raw), &s)
	return s
}

func parseUserAccessState(raw string) UserAccessState {
	var s UserAccessState
	if sekiban.IsEmptyJSON(raw) {
		return s
	}
	_ = json.Unmarshal([]byte(raw), &s)
	return s
}

func parseRoomState(raw string) RoomState {
	var s RoomState
	if sekiban.IsEmptyJSON(raw) {
		return s
	}
	_ = json.Unmarshal([]byte(raw), &s)
	return s
}

func parseReservationState(raw string) ReservationState {
	var s ReservationState
	if sekiban.IsEmptyJSON(raw) {
		return s
	}
	_ = json.Unmarshal([]byte(raw), &s)
	return s
}

func parseApprovalRequestState(raw string) ApprovalRequestState {
	var s ApprovalRequestState
	if sekiban.IsEmptyJSON(raw) {
		return s
	}
	_ = json.Unmarshal([]byte(raw), &s)
	return s
}

// ── 1. CreateWeatherForecast ─────────────────────────────────────────────────

type CreateWeatherForecast struct {
	ForecastId   *string `json:"forecastId"`
	Location     string  `json:"location"`
	Date         string  `json:"date"`
	TemperatureC int     `json:"temperatureC"`
	Summary      *string `json:"summary"`
}

func (c CreateWeatherForecast) CommandType() string { return "CreateWeatherForecast" }

func (c CreateWeatherForecast) Handle(ctx sekiban.CommandContext) (*sekiban.CommandOutput, error) {
	id := newID()
	if c.ForecastId != nil && *c.ForecastId != "" {
		id = *c.ForecastId
	}
	tag := sekiban.TagString(TagGroupWeather, id)

	resp, err := ctx.GetTagState(TagGroupWeather, id)
	if err != nil {
		return nil, err
	}
	state := parseWeatherState(resp.StateJson)
	if !state.IsEmpty() {
		return nil, fmt.Errorf("%w: weather forecast %s", sekiban.ErrAlreadyExists, id)
	}

	summary := ""
	if c.Summary != nil {
		summary = *c.Summary
	}

	out, err := sekiban.NewCommandOutput(
		EventWeatherForecastCreated,
		WeatherForecastCreated{
			ForecastId:   id,
			Location:     c.Location,
			Date:         c.Date,
			TemperatureC: c.TemperatureC,
			Summary:      summary,
			CreatedAt:    nowIso(),
		},
		[]string{tag},
		[]string{tag},
		map[string]int{tag: resp.Version},
	)
	if err != nil {
		return nil, err
	}
	return &out, nil
}

// ── 2. UpdateWeatherForecastLocation ─────────────────────────────────────────

type UpdateWeatherForecastLocation struct {
	ForecastId  string `json:"forecastId"`
	NewLocation string `json:"newLocation"`
}

func (c UpdateWeatherForecastLocation) CommandType() string {
	return "UpdateWeatherForecastLocation"
}

func (c UpdateWeatherForecastLocation) Handle(ctx sekiban.CommandContext) (*sekiban.CommandOutput, error) {
	tag := sekiban.TagString(TagGroupWeather, c.ForecastId)

	resp, err := ctx.GetTagState(TagGroupWeather, c.ForecastId)
	if err != nil {
		return nil, err
	}
	state := parseWeatherState(resp.StateJson)
	if state.IsEmpty() {
		return nil, fmt.Errorf("%w: weather forecast %s", sekiban.ErrNotFound, c.ForecastId)
	}

	out, err := sekiban.NewCommandOutput(
		EventWeatherForecastLocationUpdated,
		WeatherForecastLocationUpdated{
			ForecastId:  c.ForecastId,
			NewLocation: c.NewLocation,
			UpdatedAt:   nowIso(),
		},
		[]string{tag},
		[]string{tag},
		map[string]int{tag: resp.Version},
	)
	if err != nil {
		return nil, err
	}
	return &out, nil
}

// ── 3. DeleteWeatherForecast ─────────────────────────────────────────────────

type DeleteWeatherForecast struct {
	ForecastId string `json:"forecastId"`
}

func (c DeleteWeatherForecast) CommandType() string { return "DeleteWeatherForecast" }

func (c DeleteWeatherForecast) Handle(ctx sekiban.CommandContext) (*sekiban.CommandOutput, error) {
	tag := sekiban.TagString(TagGroupWeather, c.ForecastId)

	resp, err := ctx.GetTagState(TagGroupWeather, c.ForecastId)
	if err != nil {
		return nil, err
	}
	state := parseWeatherState(resp.StateJson)
	if state.IsEmpty() {
		return nil, fmt.Errorf("%w: weather forecast %s", sekiban.ErrNotFound, c.ForecastId)
	}

	out, err := sekiban.NewCommandOutput(
		EventWeatherForecastDeleted,
		WeatherForecastDeleted{
			ForecastId: c.ForecastId,
			DeletedAt:  nowIso(),
		},
		[]string{tag},
		[]string{tag},
		map[string]int{tag: resp.Version},
	)
	if err != nil {
		return nil, err
	}
	return &out, nil
}

// ── 4. CreateStudent ─────────────────────────────────────────────────────────

type CreateStudent struct {
	StudentId     *string `json:"studentId"`
	Name          string  `json:"name"`
	MaxClassCount int     `json:"maxClassCount"`
}

func (c CreateStudent) CommandType() string { return "CreateStudent" }

func (c CreateStudent) Handle(ctx sekiban.CommandContext) (*sekiban.CommandOutput, error) {
	if c.MaxClassCount < 1 {
		return nil, fmt.Errorf("%w: maxClassCount must be at least 1", sekiban.ErrValidation)
	}

	id := newID()
	if c.StudentId != nil && *c.StudentId != "" {
		id = *c.StudentId
	}
	tag := sekiban.TagString(TagGroupStudent, id)

	resp, err := ctx.GetTagState(TagGroupStudent, id)
	if err != nil {
		return nil, err
	}
	state := parseStudentState(resp.StateJson)
	if !state.IsEmpty() {
		return nil, fmt.Errorf("%w: student %s", sekiban.ErrAlreadyExists, id)
	}

	out, err := sekiban.NewCommandOutput(
		EventStudentCreated,
		StudentCreated{
			StudentId:     id,
			Name:          c.Name,
			MaxClassCount: c.MaxClassCount,
		},
		[]string{tag},
		[]string{tag},
		map[string]int{tag: resp.Version},
	)
	if err != nil {
		return nil, err
	}
	return &out, nil
}

// ── 5. CreateClassRoom ───────────────────────────────────────────────────────

type CreateClassRoom struct {
	ClassRoomId *string `json:"classRoomId"`
	Name        string  `json:"name"`
	MaxStudents int     `json:"maxStudents"`
}

func (c CreateClassRoom) CommandType() string { return "CreateClassRoom" }

func (c CreateClassRoom) Handle(ctx sekiban.CommandContext) (*sekiban.CommandOutput, error) {
	if c.MaxStudents < 1 {
		return nil, fmt.Errorf("%w: maxStudents must be at least 1", sekiban.ErrValidation)
	}

	id := newID()
	if c.ClassRoomId != nil && *c.ClassRoomId != "" {
		id = *c.ClassRoomId
	}
	tag := sekiban.TagString(TagGroupClassRoom, id)

	resp, err := ctx.GetTagState(TagGroupClassRoom, id)
	if err != nil {
		return nil, err
	}
	state := parseClassRoomState(resp.StateJson)
	if !state.IsEmpty() {
		return nil, fmt.Errorf("%w: classroom %s", sekiban.ErrAlreadyExists, id)
	}

	out, err := sekiban.NewCommandOutput(
		EventClassRoomCreated,
		ClassRoomCreated{
			ClassRoomId: id,
			Name:        c.Name,
			MaxStudents: c.MaxStudents,
		},
		[]string{tag},
		[]string{tag},
		map[string]int{tag: resp.Version},
	)
	if err != nil {
		return nil, err
	}
	return &out, nil
}

// ── 6. EnrollStudentInClassRoom ──────────────────────────────────────────────

type EnrollStudentInClassRoom struct {
	StudentId   string `json:"studentId"`
	ClassRoomId string `json:"classRoomId"`
}

func (c EnrollStudentInClassRoom) CommandType() string { return "EnrollStudentInClassRoom" }

func (c EnrollStudentInClassRoom) Handle(ctx sekiban.CommandContext) (*sekiban.CommandOutput, error) {
	studentTag := sekiban.TagString(TagGroupStudent, c.StudentId)
	classTag := sekiban.TagString(TagGroupClassRoom, c.ClassRoomId)

	studentResp, err := ctx.GetTagState(TagGroupStudent, c.StudentId)
	if err != nil {
		return nil, err
	}
	student := parseStudentState(studentResp.StateJson)
	if student.IsEmpty() {
		return nil, fmt.Errorf("%w: student %s", sekiban.ErrNotFound, c.StudentId)
	}

	classResp, err := ctx.GetTagState(TagGroupClassRoom, c.ClassRoomId)
	if err != nil {
		return nil, err
	}
	class := parseClassRoomState(classResp.StateJson)
	if class.IsEmpty() {
		return nil, fmt.Errorf("%w: classroom %s", sekiban.ErrNotFound, c.ClassRoomId)
	}

	if class.Remaining() <= 0 {
		return nil, fmt.Errorf("%w: classroom %s is full", sekiban.ErrValidation, c.ClassRoomId)
	}
	if student.Remaining() <= 0 {
		return nil, fmt.Errorf("%w: student %s has reached max class count", sekiban.ErrValidation, c.StudentId)
	}
	if sekiban.ContainsString(student.EnrolledClassRoomIds, c.ClassRoomId) {
		return nil, fmt.Errorf("%w: student %s already enrolled in classroom %s", sekiban.ErrValidation, c.StudentId, c.ClassRoomId)
	}

	out, err := sekiban.NewCommandOutput(
		EventStudentEnrolledInClassRoom,
		StudentEnrolledInClassRoom{
			StudentId:   c.StudentId,
			ClassRoomId: c.ClassRoomId,
		},
		[]string{studentTag, classTag},
		[]string{studentTag, classTag},
		map[string]int{studentTag: studentResp.Version, classTag: classResp.Version},
	)
	if err != nil {
		return nil, err
	}
	return &out, nil
}

// ── 7. DropStudentFromClassRoom ──────────────────────────────────────────────

type DropStudentFromClassRoom struct {
	StudentId   string `json:"studentId"`
	ClassRoomId string `json:"classRoomId"`
}

func (c DropStudentFromClassRoom) CommandType() string { return "DropStudentFromClassRoom" }

func (c DropStudentFromClassRoom) Handle(ctx sekiban.CommandContext) (*sekiban.CommandOutput, error) {
	studentTag := sekiban.TagString(TagGroupStudent, c.StudentId)
	classTag := sekiban.TagString(TagGroupClassRoom, c.ClassRoomId)

	studentResp, err := ctx.GetTagState(TagGroupStudent, c.StudentId)
	if err != nil {
		return nil, err
	}
	student := parseStudentState(studentResp.StateJson)
	if student.IsEmpty() {
		return nil, fmt.Errorf("%w: student %s", sekiban.ErrNotFound, c.StudentId)
	}

	classResp, err := ctx.GetTagState(TagGroupClassRoom, c.ClassRoomId)
	if err != nil {
		return nil, err
	}
	class := parseClassRoomState(classResp.StateJson)
	if class.IsEmpty() {
		return nil, fmt.Errorf("%w: classroom %s", sekiban.ErrNotFound, c.ClassRoomId)
	}

	if !sekiban.ContainsString(student.EnrolledClassRoomIds, c.ClassRoomId) {
		return nil, fmt.Errorf("%w: student %s not enrolled in classroom %s", sekiban.ErrValidation, c.StudentId, c.ClassRoomId)
	}

	out, err := sekiban.NewCommandOutput(
		EventStudentDroppedFromClassRoom,
		StudentDroppedFromClassRoom{
			StudentId:   c.StudentId,
			ClassRoomId: c.ClassRoomId,
		},
		[]string{studentTag, classTag},
		[]string{studentTag, classTag},
		map[string]int{studentTag: studentResp.Version, classTag: classResp.Version},
	)
	if err != nil {
		return nil, err
	}
	return &out, nil
}

// ── 8. RegisterUser ──────────────────────────────────────────────────────────

type RegisterUser struct {
	UserId                  *string `json:"userId"`
	DisplayName             string  `json:"displayName"`
	Email                   string  `json:"email"`
	Department              *string `json:"department"`
	MonthlyReservationLimit int     `json:"monthlyReservationLimit"`
}

func (c RegisterUser) CommandType() string { return "RegisterUser" }

func (c RegisterUser) Handle(ctx sekiban.CommandContext) (*sekiban.CommandOutput, error) {
	id := newID()
	if c.UserId != nil && *c.UserId != "" {
		id = *c.UserId
	}
	tag := sekiban.TagString(TagGroupUser, id)

	resp, err := ctx.GetTagState(TagGroupUser, id)
	if err != nil {
		return nil, err
	}
	state := parseUserDirectoryState(resp.StateJson)
	if !state.IsEmpty() {
		return nil, fmt.Errorf("%w: user %s", sekiban.ErrAlreadyExists, id)
	}

	out, err := sekiban.NewCommandOutput(
		EventUserRegistered,
		UserRegistered{
			UserId:                  id,
			DisplayName:             c.DisplayName,
			Email:                   c.Email,
			Department:              c.Department,
			RegisteredAt:            nowIso(),
			MonthlyReservationLimit: c.MonthlyReservationLimit,
		},
		[]string{tag},
		[]string{tag},
		map[string]int{tag: resp.Version},
	)
	if err != nil {
		return nil, err
	}
	return &out, nil
}

// ── 9. UpdateUserMonthlyReservationLimit ─────────────────────────────────────

type UpdateUserMonthlyReservationLimit struct {
	UserId                  string `json:"userId"`
	MonthlyReservationLimit int    `json:"monthlyReservationLimit"`
}

func (c UpdateUserMonthlyReservationLimit) CommandType() string {
	return "UpdateUserMonthlyReservationLimit"
}

func (c UpdateUserMonthlyReservationLimit) Handle(ctx sekiban.CommandContext) (*sekiban.CommandOutput, error) {
	tag := sekiban.TagString(TagGroupUser, c.UserId)

	resp, err := ctx.GetTagState(TagGroupUser, c.UserId)
	if err != nil {
		return nil, err
	}
	state := parseUserDirectoryState(resp.StateJson)
	if state.IsEmpty() {
		return nil, fmt.Errorf("%w: user %s", sekiban.ErrNotFound, c.UserId)
	}

	out, err := sekiban.NewCommandOutput(
		EventUserProfileUpdated,
		UserProfileUpdated{
			UserId:                  c.UserId,
			DisplayName:             state.DisplayName,
			Email:                   state.Email,
			Department:              state.Department,
			MonthlyReservationLimit: c.MonthlyReservationLimit,
		},
		[]string{tag},
		[]string{tag},
		map[string]int{tag: resp.Version},
	)
	if err != nil {
		return nil, err
	}
	return &out, nil
}

// ── 10. GrantUserAccess ──────────────────────────────────────────────────────

type GrantUserAccess struct {
	UserId      string `json:"userId"`
	InitialRole string `json:"initialRole"`
}

func (c GrantUserAccess) CommandType() string { return "GrantUserAccess" }

func (c GrantUserAccess) Handle(ctx sekiban.CommandContext) (*sekiban.CommandOutput, error) {
	userResp, err := ctx.GetTagState(TagGroupUser, c.UserId)
	if err != nil {
		return nil, err
	}
	user := parseUserDirectoryState(userResp.StateJson)
	if user.IsEmpty() {
		return nil, fmt.Errorf("%w: user %s", sekiban.ErrNotFound, c.UserId)
	}

	accessTag := sekiban.TagString(TagGroupUserAccess, c.UserId)

	out, err := sekiban.NewCommandOutput(
		EventUserAccessGranted,
		UserAccessGranted{
			UserId:      c.UserId,
			InitialRole: c.InitialRole,
			GrantedAt:   nowIso(),
		},
		[]string{accessTag},
		[]string{accessTag},
		map[string]int{},
	)
	if err != nil {
		return nil, err
	}
	return &out, nil
}

// ── 11. GrantUserRole ────────────────────────────────────────────────────────

type GrantUserRole struct {
	UserId string `json:"userId"`
	Role   string `json:"role"`
}

func (c GrantUserRole) CommandType() string { return "GrantUserRole" }

func (c GrantUserRole) Handle(ctx sekiban.CommandContext) (*sekiban.CommandOutput, error) {
	accessTag := sekiban.TagString(TagGroupUserAccess, c.UserId)

	resp, err := ctx.GetTagState(TagGroupUserAccess, c.UserId)
	if err != nil {
		return nil, err
	}
	state := parseUserAccessState(resp.StateJson)
	if state.IsEmpty() {
		return nil, fmt.Errorf("%w: user access for %s", sekiban.ErrNotFound, c.UserId)
	}

	out, err := sekiban.NewCommandOutput(
		EventUserRoleGranted,
		UserRoleGranted{
			UserId:    c.UserId,
			Role:      c.Role,
			GrantedAt: nowIso(),
		},
		[]string{accessTag},
		[]string{accessTag},
		map[string]int{accessTag: resp.Version},
	)
	if err != nil {
		return nil, err
	}
	return &out, nil
}

// ── 12. CreateRoom ───────────────────────────────────────────────────────────

type CreateRoom struct {
	RoomId           *string  `json:"roomId"`
	Name             string   `json:"name"`
	Capacity         int      `json:"capacity"`
	Location         string   `json:"location"`
	Equipment        []string `json:"equipment"`
	RequiresApproval bool     `json:"requiresApproval"`
}

func (c CreateRoom) CommandType() string { return "CreateRoom" }

func (c CreateRoom) Handle(ctx sekiban.CommandContext) (*sekiban.CommandOutput, error) {
	id := newID()
	if c.RoomId != nil && *c.RoomId != "" {
		id = *c.RoomId
	}
	tag := sekiban.TagString(TagGroupRoom, id)

	resp, err := ctx.GetTagState(TagGroupRoom, id)
	if err != nil {
		return nil, err
	}
	state := parseRoomState(resp.StateJson)
	if !state.IsEmpty() {
		return nil, fmt.Errorf("%w: room %s", sekiban.ErrAlreadyExists, id)
	}

	equipment := c.Equipment
	if equipment == nil {
		equipment = []string{}
	}

	out, err := sekiban.NewCommandOutput(
		EventRoomCreated,
		RoomCreated{
			RoomId:           id,
			Name:             c.Name,
			Capacity:         c.Capacity,
			Location:         c.Location,
			Equipment:        equipment,
			RequiresApproval: c.RequiresApproval,
		},
		[]string{tag},
		[]string{tag},
		map[string]int{tag: resp.Version},
	)
	if err != nil {
		return nil, err
	}
	return &out, nil
}

// ── 13. UpdateRoom ───────────────────────────────────────────────────────────

type UpdateRoom struct {
	RoomId           string   `json:"roomId"`
	Name             string   `json:"name"`
	Capacity         int      `json:"capacity"`
	Location         string   `json:"location"`
	Equipment        []string `json:"equipment"`
	RequiresApproval bool     `json:"requiresApproval"`
}

func (c UpdateRoom) CommandType() string { return "UpdateRoom" }

func (c UpdateRoom) Handle(ctx sekiban.CommandContext) (*sekiban.CommandOutput, error) {
	tag := sekiban.TagString(TagGroupRoom, c.RoomId)

	resp, err := ctx.GetTagState(TagGroupRoom, c.RoomId)
	if err != nil {
		return nil, err
	}
	state := parseRoomState(resp.StateJson)
	if state.IsEmpty() {
		return nil, fmt.Errorf("%w: room %s", sekiban.ErrNotFound, c.RoomId)
	}

	equipment := c.Equipment
	if equipment == nil {
		equipment = []string{}
	}

	out, err := sekiban.NewCommandOutput(
		EventRoomUpdated,
		RoomUpdated{
			RoomId:           c.RoomId,
			Name:             c.Name,
			Capacity:         c.Capacity,
			Location:         c.Location,
			Equipment:        equipment,
			RequiresApproval: c.RequiresApproval,
		},
		[]string{tag},
		[]string{tag},
		map[string]int{tag: resp.Version},
	)
	if err != nil {
		return nil, err
	}
	return &out, nil
}

// ── 14. CreateReservationDraft ───────────────────────────────────────────────

type CreateReservationDraft struct {
	ReservationId     *string  `json:"reservationId"`
	RoomId            string   `json:"roomId"`
	OrganizerId       string   `json:"organizerId"`
	OrganizerName     string   `json:"organizerName"`
	StartTime         string   `json:"startTime"`
	EndTime           string   `json:"endTime"`
	Purpose           string   `json:"purpose"`
	SelectedEquipment []string `json:"selectedEquipment"`
}

func (c CreateReservationDraft) CommandType() string { return "CreateReservationDraft" }

func (c CreateReservationDraft) Handle(ctx sekiban.CommandContext) (*sekiban.CommandOutput, error) {
	// Check room exists
	roomResp, err := ctx.GetTagState(TagGroupRoom, c.RoomId)
	if err != nil {
		return nil, err
	}
	room := parseRoomState(roomResp.StateJson)
	if room.IsEmpty() {
		return nil, fmt.Errorf("%w: room %s", sekiban.ErrNotFound, c.RoomId)
	}

	id := newID()
	if c.ReservationId != nil && *c.ReservationId != "" {
		id = *c.ReservationId
	}

	reservationTag := sekiban.TagString(TagGroupReservation, id)
	roomTag := sekiban.TagString(TagGroupRoom, c.RoomId)

	selectedEquipment := c.SelectedEquipment
	if selectedEquipment == nil {
		selectedEquipment = []string{}
	}

	out, err := sekiban.NewCommandOutput(
		EventReservationDraftCreated,
		ReservationDraftCreated{
			ReservationId:     id,
			RoomId:            c.RoomId,
			OrganizerId:       c.OrganizerId,
			OrganizerName:     c.OrganizerName,
			StartTime:         c.StartTime,
			EndTime:           c.EndTime,
			Purpose:           c.Purpose,
			SelectedEquipment: selectedEquipment,
		},
		[]string{reservationTag, roomTag},
		[]string{reservationTag},
		map[string]int{},
	)
	if err != nil {
		return nil, err
	}
	return &out, nil
}

// ── 15. CommitReservationHold ────────────────────────────────────────────────

type CommitReservationHold struct {
	ReservationId          string  `json:"reservationId"`
	RoomId                 string  `json:"roomId"`
	RequiresApproval       bool    `json:"requiresApproval"`
	ApprovalRequestId      *string `json:"approvalRequestId"`
	ApprovalRequestComment *string `json:"approvalRequestComment"`
}

func (c CommitReservationHold) CommandType() string { return "CommitReservationHold" }

func (c CommitReservationHold) Handle(ctx sekiban.CommandContext) (*sekiban.CommandOutput, error) {
	reservationTag := sekiban.TagString(TagGroupReservation, c.ReservationId)

	resp, err := ctx.GetTagState(TagGroupReservation, c.ReservationId)
	if err != nil {
		return nil, err
	}
	state := parseReservationState(resp.StateJson)
	if state.IsEmpty() {
		return nil, fmt.Errorf("%w: reservation %s", sekiban.ErrNotFound, c.ReservationId)
	}
	if state.Status != "Draft" {
		return nil, fmt.Errorf("%w: reservation %s is not in Draft status", sekiban.ErrValidation, c.ReservationId)
	}

	roomTag := sekiban.TagString(TagGroupRoom, c.RoomId)

	out, err := sekiban.NewCommandOutput(
		EventReservationHoldCommitted,
		ReservationHoldCommitted{
			ReservationId:          c.ReservationId,
			RoomId:                 c.RoomId,
			OrganizerId:            state.OrganizerId,
			OrganizerName:          state.OrganizerName,
			StartTime:              state.StartTime,
			EndTime:                state.EndTime,
			Purpose:                state.Purpose,
			SelectedEquipment:      state.SelectedEquipment,
			RequiresApproval:       c.RequiresApproval,
			ApprovalRequestId:      c.ApprovalRequestId,
			ApprovalRequestComment: c.ApprovalRequestComment,
		},
		[]string{reservationTag, roomTag},
		[]string{reservationTag},
		map[string]int{reservationTag: resp.Version},
	)
	if err != nil {
		return nil, err
	}
	return &out, nil
}

// ── 16. ConfirmReservation ───────────────────────────────────────────────────

type ConfirmReservation struct {
	ReservationId          string  `json:"reservationId"`
	RoomId                 string  `json:"roomId"`
	ApprovalRequestId      *string `json:"approvalRequestId"`
	ApprovalDecisionComment *string `json:"approvalDecisionComment"`
}

func (c ConfirmReservation) CommandType() string { return "ConfirmReservation" }

func (c ConfirmReservation) Handle(ctx sekiban.CommandContext) (*sekiban.CommandOutput, error) {
	reservationTag := sekiban.TagString(TagGroupReservation, c.ReservationId)

	resp, err := ctx.GetTagState(TagGroupReservation, c.ReservationId)
	if err != nil {
		return nil, err
	}
	state := parseReservationState(resp.StateJson)
	if state.IsEmpty() {
		return nil, fmt.Errorf("%w: reservation %s", sekiban.ErrNotFound, c.ReservationId)
	}

	roomTag := sekiban.TagString(TagGroupRoom, c.RoomId)

	out, err := sekiban.NewCommandOutput(
		EventReservationConfirmed,
		ReservationConfirmed{
			ReservationId:           c.ReservationId,
			RoomId:                  c.RoomId,
			OrganizerId:             state.OrganizerId,
			OrganizerName:           state.OrganizerName,
			StartTime:               state.StartTime,
			EndTime:                 state.EndTime,
			Purpose:                 state.Purpose,
			SelectedEquipment:       state.SelectedEquipment,
			ConfirmedAt:             nowIso(),
			ApprovalRequestId:       c.ApprovalRequestId,
			ApprovalRequestComment:  state.ApprovalRequestComment,
			ApprovalDecisionComment: c.ApprovalDecisionComment,
		},
		[]string{reservationTag, roomTag},
		[]string{reservationTag},
		map[string]int{reservationTag: resp.Version},
	)
	if err != nil {
		return nil, err
	}
	return &out, nil
}

// ── 17. CancelReservation ────────────────────────────────────────────────────

type CancelReservation struct {
	ReservationId string `json:"reservationId"`
	RoomId        string `json:"roomId"`
	Reason        string `json:"reason"`
}

func (c CancelReservation) CommandType() string { return "CancelReservation" }

func (c CancelReservation) Handle(ctx sekiban.CommandContext) (*sekiban.CommandOutput, error) {
	reservationTag := sekiban.TagString(TagGroupReservation, c.ReservationId)

	resp, err := ctx.GetTagState(TagGroupReservation, c.ReservationId)
	if err != nil {
		return nil, err
	}
	state := parseReservationState(resp.StateJson)
	if state.IsEmpty() {
		return nil, fmt.Errorf("%w: reservation %s", sekiban.ErrNotFound, c.ReservationId)
	}

	roomTag := sekiban.TagString(TagGroupRoom, c.RoomId)

	out, err := sekiban.NewCommandOutput(
		EventReservationCancelled,
		ReservationCancelled{
			ReservationId:          c.ReservationId,
			RoomId:                 c.RoomId,
			OrganizerId:            state.OrganizerId,
			OrganizerName:          state.OrganizerName,
			StartTime:              state.StartTime,
			EndTime:                state.EndTime,
			Purpose:                state.Purpose,
			SelectedEquipment:      state.SelectedEquipment,
			ApprovalRequestComment: state.ApprovalRequestComment,
			Reason:                 c.Reason,
			CancelledAt:            nowIso(),
		},
		[]string{reservationTag, roomTag},
		[]string{reservationTag},
		map[string]int{reservationTag: resp.Version},
	)
	if err != nil {
		return nil, err
	}
	return &out, nil
}

// ── 18. RejectReservation ────────────────────────────────────────────────────

type RejectReservation struct {
	ReservationId     string `json:"reservationId"`
	RoomId            string `json:"roomId"`
	ApprovalRequestId string `json:"approvalRequestId"`
	Reason            string `json:"reason"`
}

func (c RejectReservation) CommandType() string { return "RejectReservation" }

func (c RejectReservation) Handle(ctx sekiban.CommandContext) (*sekiban.CommandOutput, error) {
	reservationTag := sekiban.TagString(TagGroupReservation, c.ReservationId)

	resp, err := ctx.GetTagState(TagGroupReservation, c.ReservationId)
	if err != nil {
		return nil, err
	}
	state := parseReservationState(resp.StateJson)
	if state.IsEmpty() {
		return nil, fmt.Errorf("%w: reservation %s", sekiban.ErrNotFound, c.ReservationId)
	}

	roomTag := sekiban.TagString(TagGroupRoom, c.RoomId)

	out, err := sekiban.NewCommandOutput(
		EventReservationRejected,
		ReservationRejected{
			ReservationId:          c.ReservationId,
			RoomId:                 c.RoomId,
			OrganizerId:            state.OrganizerId,
			OrganizerName:          state.OrganizerName,
			StartTime:              state.StartTime,
			EndTime:                state.EndTime,
			Purpose:                state.Purpose,
			SelectedEquipment:      state.SelectedEquipment,
			ApprovalRequestId:      c.ApprovalRequestId,
			ApprovalRequestComment: state.ApprovalRequestComment,
			Reason:                 c.Reason,
			RejectedAt:             nowIso(),
		},
		[]string{reservationTag, roomTag},
		[]string{reservationTag},
		map[string]int{reservationTag: resp.Version},
	)
	if err != nil {
		return nil, err
	}
	return &out, nil
}

// ── 19. StartApprovalFlow ────────────────────────────────────────────────────

type StartApprovalFlow struct {
	ApprovalRequestId *string  `json:"approvalRequestId"`
	ReservationId     string   `json:"reservationId"`
	ApproverIds       []string `json:"approverIds"`
	RequestComment    *string  `json:"requestComment"`
}

func (c StartApprovalFlow) CommandType() string { return "StartApprovalFlow" }

func (c StartApprovalFlow) Handle(ctx sekiban.CommandContext) (*sekiban.CommandOutput, error) {
	// Load reservation to get roomId and requesterId
	reservationResp, err := ctx.GetTagState(TagGroupReservation, c.ReservationId)
	if err != nil {
		return nil, err
	}
	reservation := parseReservationState(reservationResp.StateJson)
	if reservation.IsEmpty() {
		return nil, fmt.Errorf("%w: reservation %s", sekiban.ErrNotFound, c.ReservationId)
	}

	id := newID()
	if c.ApprovalRequestId != nil && *c.ApprovalRequestId != "" {
		id = *c.ApprovalRequestId
	}

	approvalTag := sekiban.TagString(TagGroupApprovalRequest, id)

	approverIds := c.ApproverIds
	if approverIds == nil {
		approverIds = []string{}
	}

	out, err := sekiban.NewCommandOutput(
		EventApprovalFlowStarted,
		ApprovalFlowStarted{
			ApprovalRequestId: id,
			ReservationId:     c.ReservationId,
			RoomId:            reservation.RoomId,
			RequesterId:       reservation.OrganizerId,
			ApproverIds:       approverIds,
			RequestedAt:       nowIso(),
			RequestComment:    c.RequestComment,
		},
		[]string{approvalTag},
		[]string{approvalTag},
		map[string]int{},
	)
	if err != nil {
		return nil, err
	}
	return &out, nil
}

// ── 20. RecordApprovalDecision ───────────────────────────────────────────────

type RecordApprovalDecision struct {
	ApprovalRequestId string  `json:"approvalRequestId"`
	ApproverId        string  `json:"approverId"`
	Decision          string  `json:"decision"`
	Comment           *string `json:"comment"`
}

func (c RecordApprovalDecision) CommandType() string { return "RecordApprovalDecision" }

func (c RecordApprovalDecision) Handle(ctx sekiban.CommandContext) (*sekiban.CommandOutput, error) {
	approvalTag := sekiban.TagString(TagGroupApprovalRequest, c.ApprovalRequestId)

	resp, err := ctx.GetTagState(TagGroupApprovalRequest, c.ApprovalRequestId)
	if err != nil {
		return nil, err
	}
	state := parseApprovalRequestState(resp.StateJson)
	if state.IsEmpty() {
		return nil, fmt.Errorf("%w: approval request %s", sekiban.ErrNotFound, c.ApprovalRequestId)
	}

	out, err := sekiban.NewCommandOutput(
		EventApprovalDecisionRecorded,
		ApprovalDecisionRecorded{
			ApprovalRequestId: c.ApprovalRequestId,
			ReservationId:     state.ReservationId,
			ApproverId:        c.ApproverId,
			Decision:          c.Decision,
			Comment:           c.Comment,
			DecidedAt:         nowIso(),
		},
		[]string{approvalTag},
		[]string{approvalTag},
		map[string]int{approvalTag: resp.Version},
	)
	if err != nil {
		return nil, err
	}
	return &out, nil
}
