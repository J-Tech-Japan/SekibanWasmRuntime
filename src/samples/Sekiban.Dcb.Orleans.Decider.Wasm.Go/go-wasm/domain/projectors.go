package domain

import (
	"encoding/json"
	"sort"
	"strings"

	sekiban "github.com/J-Tech-Japan/SekibanWasmRuntime/src/lib/sekiban-go/domain"
)

// ---------------------------------------------------------------------------
// Helper delegates to shared library
// ---------------------------------------------------------------------------

func containsString(list []string, value string) bool {
	return sekiban.ContainsString(list, value)
}

func removeString(list []string, value string) []string {
	return sekiban.RemoveString(list, value)
}

// ---------------------------------------------------------------------------
// Query param types
// ---------------------------------------------------------------------------

// WeatherListQuery filters for the weather forecast list query.
type WeatherListQuery struct {
	LocationFilter          *string `json:"locationFilter"`
	ForecastId              *string `json:"forecastId"`
	WaitForSortableUniqueId *string `json:"waitForSortableUniqueId"`
	sekiban.PagingQuery
}

// ReservationListQuery filters for the reservation list query.
type ReservationListQuery struct {
	RoomId *string `json:"roomId"`
	sekiban.PagingQuery
}

// ApprovalListQuery filters for the approval inbox query.
type ApprovalListQuery struct {
	PendingOnly *bool `json:"pendingOnly"`
	sekiban.PagingQuery
}

// UserDirectoryListQuery filters for the user directory list query.
type UserDirectoryListQuery struct {
	ActiveOnly *bool `json:"activeOnly"`
	sekiban.PagingQuery
}

// UserAccessListQuery filters for the user access list query.
type UserAccessListQuery struct {
	ActiveOnly *bool   `json:"activeOnly"`
	RoleFilter *string `json:"roleFilter"`
	sekiban.PagingQuery
}

// ---------------------------------------------------------------------------
// 1. Tag projector: WeatherForecast
// ---------------------------------------------------------------------------

func ApplyWeatherForecastEvent(state WeatherForecastState, eventType, payload string) WeatherForecastState {
	switch eventType {
	case EventWeatherForecastCreated:
		var ev WeatherForecastCreated
		_ = json.Unmarshal([]byte(payload), &ev)
		return WeatherForecastState{
			ForecastId:   ev.ForecastId,
			Location:     ev.Location,
			Date:         ev.Date,
			TemperatureC: ev.TemperatureC,
			Summary:      ev.Summary,
			IsDeleted:    false,
		}
	case EventWeatherForecastLocationUpdated:
		var ev WeatherForecastLocationUpdated
		_ = json.Unmarshal([]byte(payload), &ev)
		state.Location = ev.NewLocation
		return state
	case EventWeatherForecastDeleted:
		state.IsDeleted = true
		return state
	}
	return state
}

// ---------------------------------------------------------------------------
// 2. Tag projector: Student
// ---------------------------------------------------------------------------

func ApplyStudentEvent(state StudentState, eventType, payload string) StudentState {
	switch eventType {
	case EventStudentCreated:
		var ev StudentCreated
		_ = json.Unmarshal([]byte(payload), &ev)
		return StudentState{
			StudentId:          ev.StudentId,
			Name:               ev.Name,
			MaxClassCount:      ev.MaxClassCount,
			EnrolledClassRoomIds: []string{},
		}
	case EventStudentEnrolledInClassRoom:
		var ev StudentEnrolledInClassRoom
		_ = json.Unmarshal([]byte(payload), &ev)
		if !containsString(state.EnrolledClassRoomIds, ev.ClassRoomId) {
			state.EnrolledClassRoomIds = append(state.EnrolledClassRoomIds, ev.ClassRoomId)
		}
		return state
	case EventStudentDroppedFromClassRoom:
		var ev StudentDroppedFromClassRoom
		_ = json.Unmarshal([]byte(payload), &ev)
		state.EnrolledClassRoomIds = removeString(state.EnrolledClassRoomIds, ev.ClassRoomId)
		return state
	}
	return state
}

// ---------------------------------------------------------------------------
// 3. Tag projector: ClassRoom
// ---------------------------------------------------------------------------

func ApplyClassRoomEvent(state ClassRoomState, eventType, payload string) ClassRoomState {
	switch eventType {
	case EventClassRoomCreated:
		var ev ClassRoomCreated
		_ = json.Unmarshal([]byte(payload), &ev)
		return ClassRoomState{
			ClassRoomId:        ev.ClassRoomId,
			Name:               ev.Name,
			MaxStudents:        ev.MaxStudents,
			EnrolledStudentIds: []string{},
			IsFull:             false,
		}
	case EventStudentEnrolledInClassRoom:
		var ev StudentEnrolledInClassRoom
		_ = json.Unmarshal([]byte(payload), &ev)
		if !containsString(state.EnrolledStudentIds, ev.StudentId) {
			state.EnrolledStudentIds = append(state.EnrolledStudentIds, ev.StudentId)
		}
		state.IsFull = len(state.EnrolledStudentIds) >= state.MaxStudents
		return state
	case EventStudentDroppedFromClassRoom:
		var ev StudentDroppedFromClassRoom
		_ = json.Unmarshal([]byte(payload), &ev)
		state.EnrolledStudentIds = removeString(state.EnrolledStudentIds, ev.StudentId)
		state.IsFull = len(state.EnrolledStudentIds) >= state.MaxStudents
		return state
	}
	return state
}

// ---------------------------------------------------------------------------
// 4. Tag projector: UserDirectory
// ---------------------------------------------------------------------------

func ApplyUserDirectoryEvent(state UserDirectoryState, eventType, payload string) UserDirectoryState {
	switch eventType {
	case EventUserRegistered:
		var ev UserRegistered
		_ = json.Unmarshal([]byte(payload), &ev)
		return UserDirectoryState{
			UserId:                  ev.UserId,
			DisplayName:             ev.DisplayName,
			Email:                   ev.Email,
			Department:              ev.Department,
			RegisteredAt:            ev.RegisteredAt,
			MonthlyReservationLimit: ev.MonthlyReservationLimit,
			IsActive:                true,
		}
	case EventUserProfileUpdated:
		var ev UserProfileUpdated
		_ = json.Unmarshal([]byte(payload), &ev)
		state.DisplayName = ev.DisplayName
		state.Email = ev.Email
		state.Department = ev.Department
		state.MonthlyReservationLimit = ev.MonthlyReservationLimit
		return state
	}
	return state
}

// ---------------------------------------------------------------------------
// 5. Tag projector: UserAccess
// ---------------------------------------------------------------------------

func ApplyUserAccessEvent(state UserAccessState, eventType, payload string) UserAccessState {
	switch eventType {
	case EventUserAccessGranted:
		var ev UserAccessGranted
		_ = json.Unmarshal([]byte(payload), &ev)
		return UserAccessState{
			UserId:    ev.UserId,
			Roles:     []string{ev.InitialRole},
			GrantedAt: ev.GrantedAt,
			IsActive:  true,
		}
	case EventUserRoleGranted:
		var ev UserRoleGranted
		_ = json.Unmarshal([]byte(payload), &ev)
		if !containsString(state.Roles, ev.Role) {
			state.Roles = append(state.Roles, ev.Role)
		}
		return state
	}
	return state
}

// ---------------------------------------------------------------------------
// 6. Tag projector: Room
// ---------------------------------------------------------------------------

func ApplyRoomEvent(state RoomState, eventType, payload string) RoomState {
	switch eventType {
	case EventRoomCreated:
		var ev RoomCreated
		_ = json.Unmarshal([]byte(payload), &ev)
		return RoomState{
			RoomId:           ev.RoomId,
			Name:             ev.Name,
			Capacity:         ev.Capacity,
			Location:         ev.Location,
			Equipment:        ev.Equipment,
			RequiresApproval: ev.RequiresApproval,
			IsActive:         true,
		}
	case EventRoomUpdated:
		var ev RoomUpdated
		_ = json.Unmarshal([]byte(payload), &ev)
		state.Name = ev.Name
		state.Capacity = ev.Capacity
		state.Location = ev.Location
		state.Equipment = ev.Equipment
		state.RequiresApproval = ev.RequiresApproval
		return state
	}
	return state
}

// ---------------------------------------------------------------------------
// 7. Tag projector: Reservation
// ---------------------------------------------------------------------------

func ApplyReservationEvent(state ReservationState, eventType, payload string) ReservationState {
	switch eventType {
	case EventReservationDraftCreated:
		var ev ReservationDraftCreated
		_ = json.Unmarshal([]byte(payload), &ev)
		return ReservationState{
			ReservationId:          ev.ReservationId,
			RoomId:                 ev.RoomId,
			OrganizerId:            ev.OrganizerId,
			OrganizerName:          ev.OrganizerName,
			StartTime:              ev.StartTime,
			EndTime:                ev.EndTime,
			Purpose:                ev.Purpose,
			SelectedEquipment:      ev.SelectedEquipment,
			Status:                 "Draft",
			RequiresApproval:       false,
			ApprovalRequestId:      nil,
			ApprovalRequestComment: nil,
		}
	case EventReservationHoldCommitted:
		var ev ReservationHoldCommitted
		_ = json.Unmarshal([]byte(payload), &ev)
		state.Status = "Held"
		state.RequiresApproval = ev.RequiresApproval
		state.ApprovalRequestId = ev.ApprovalRequestId
		state.ApprovalRequestComment = ev.ApprovalRequestComment
		if ev.SelectedEquipment != nil {
			state.SelectedEquipment = ev.SelectedEquipment
		}
		return state
	case EventReservationConfirmed:
		var ev ReservationConfirmed
		_ = json.Unmarshal([]byte(payload), &ev)
		state.Status = "Confirmed"
		state.ConfirmedAt = &ev.ConfirmedAt
		state.ApprovalDecisionComment = ev.ApprovalDecisionComment
		return state
	case EventReservationCancelled:
		var ev ReservationCancelled
		_ = json.Unmarshal([]byte(payload), &ev)
		state.Status = "Cancelled"
		state.CancelReason = &ev.Reason
		cancelledAt := ev.CancelledAt
		state.CancelledAt = &cancelledAt
		return state
	case EventReservationRejected:
		var ev ReservationRejected
		_ = json.Unmarshal([]byte(payload), &ev)
		state.Status = "Rejected"
		state.RejectReason = &ev.Reason
		rejectedAt := ev.RejectedAt
		state.RejectedAt = &rejectedAt
		return state
	}
	return state
}

// ---------------------------------------------------------------------------
// 8. Tag projector: ApprovalRequest
// ---------------------------------------------------------------------------

func ApplyApprovalRequestEvent(state ApprovalRequestState, eventType, payload string) ApprovalRequestState {
	switch eventType {
	case EventApprovalFlowStarted:
		var ev ApprovalFlowStarted
		_ = json.Unmarshal([]byte(payload), &ev)
		return ApprovalRequestState{
			ApprovalRequestId: ev.ApprovalRequestId,
			ReservationId:     ev.ReservationId,
			RoomId:            ev.RoomId,
			RequesterId:       ev.RequesterId,
			ApproverIds:       ev.ApproverIds,
			RequestedAt:       ev.RequestedAt,
			RequestComment:    ev.RequestComment,
			Status:            "Pending",
		}
	case EventApprovalDecisionRecorded:
		var ev ApprovalDecisionRecorded
		_ = json.Unmarshal([]byte(payload), &ev)
		state.ApproverId = &ev.ApproverId
		state.DecisionComment = ev.Comment
		state.DecidedAt = ev.DecidedAt
		if ev.Decision == "Approved" {
			state.Status = "Approved"
		} else {
			state.Status = "Rejected"
		}
		return state
	}
	return state
}

// ===========================================================================
// Multi/List projectors
// ===========================================================================

// ---------------------------------------------------------------------------
// 1. Multi projector: WeatherForecastList
// ---------------------------------------------------------------------------

func ApplyWeatherListEvent(state WeatherForecastListState, eventType, payload string) WeatherForecastListState {
	if state.Items == nil {
		state.Items = make(map[string]WeatherForecastItem)
	}
	switch eventType {
	case EventWeatherForecastCreated:
		var ev WeatherForecastCreated
		_ = json.Unmarshal([]byte(payload), &ev)
		state.Items[ev.ForecastId] = WeatherForecastItem{
			ForecastId:   ev.ForecastId,
			Location:     ev.Location,
			Date:         ev.Date,
			TemperatureC: ev.TemperatureC,
			Summary:      ev.Summary,
			IsDeleted:    false,
		}
	case EventWeatherForecastLocationUpdated:
		var ev WeatherForecastLocationUpdated
		_ = json.Unmarshal([]byte(payload), &ev)
		if item, ok := state.Items[ev.ForecastId]; ok {
			item.Location = ev.NewLocation
			state.Items[ev.ForecastId] = item
		}
	case EventWeatherForecastDeleted:
		var ev WeatherForecastDeleted
		_ = json.Unmarshal([]byte(payload), &ev)
		if item, ok := state.Items[ev.ForecastId]; ok {
			item.IsDeleted = true
			state.Items[ev.ForecastId] = item
		}
	}
	return state
}

// ---------------------------------------------------------------------------
// 2. Multi projector: StudentList
// ---------------------------------------------------------------------------

func ApplyStudentListEvent(state StudentListState, eventType, payload string) StudentListState {
	if state.Items == nil {
		state.Items = make(map[string]StudentState)
	}
	switch eventType {
	case EventStudentCreated:
		var ev StudentCreated
		_ = json.Unmarshal([]byte(payload), &ev)
		state.Items[ev.StudentId] = StudentState{
			StudentId:           ev.StudentId,
			Name:                ev.Name,
			MaxClassCount:       ev.MaxClassCount,
			EnrolledClassRoomIds: []string{},
		}
	case EventStudentEnrolledInClassRoom:
		var ev StudentEnrolledInClassRoom
		_ = json.Unmarshal([]byte(payload), &ev)
		if s, ok := state.Items[ev.StudentId]; ok {
			if !containsString(s.EnrolledClassRoomIds, ev.ClassRoomId) {
				s.EnrolledClassRoomIds = append(s.EnrolledClassRoomIds, ev.ClassRoomId)
			}
			state.Items[ev.StudentId] = s
		}
	case EventStudentDroppedFromClassRoom:
		var ev StudentDroppedFromClassRoom
		_ = json.Unmarshal([]byte(payload), &ev)
		if s, ok := state.Items[ev.StudentId]; ok {
			s.EnrolledClassRoomIds = removeString(s.EnrolledClassRoomIds, ev.ClassRoomId)
			state.Items[ev.StudentId] = s
		}
	}
	return state
}

// ---------------------------------------------------------------------------
// 3. Multi projector: ClassRoomList
// ---------------------------------------------------------------------------

func ApplyClassRoomListEvent(state ClassRoomListState, eventType, payload string) ClassRoomListState {
	if state.Items == nil {
		state.Items = make(map[string]ClassRoomItem)
	}
	switch eventType {
	case EventClassRoomCreated:
		var ev ClassRoomCreated
		_ = json.Unmarshal([]byte(payload), &ev)
		state.Items[ev.ClassRoomId] = ClassRoomItem{
			ClassRoomId:       ev.ClassRoomId,
			Name:              ev.Name,
			MaxStudents:       ev.MaxStudents,
			EnrolledCount:     0,
			IsFull:            false,
			RemainingCapacity: ev.MaxStudents,
		}
	case EventStudentEnrolledInClassRoom:
		var ev StudentEnrolledInClassRoom
		_ = json.Unmarshal([]byte(payload), &ev)
		if item, ok := state.Items[ev.ClassRoomId]; ok {
			item.EnrolledCount++
			item.RemainingCapacity = item.MaxStudents - item.EnrolledCount
			item.IsFull = item.EnrolledCount >= item.MaxStudents
			state.Items[ev.ClassRoomId] = item
		}
	case EventStudentDroppedFromClassRoom:
		var ev StudentDroppedFromClassRoom
		_ = json.Unmarshal([]byte(payload), &ev)
		if item, ok := state.Items[ev.ClassRoomId]; ok {
			if item.EnrolledCount > 0 {
				item.EnrolledCount--
			}
			item.RemainingCapacity = item.MaxStudents - item.EnrolledCount
			item.IsFull = item.EnrolledCount >= item.MaxStudents
			state.Items[ev.ClassRoomId] = item
		}
	}
	return state
}

// ---------------------------------------------------------------------------
// 4. Multi projector: UserDirectoryList
// ---------------------------------------------------------------------------

func ApplyUserDirectoryListEvent(state UserDirectoryListState, eventType, payload string) UserDirectoryListState {
	if state.Items == nil {
		state.Items = make(map[string]UserDirectoryListItem)
	}
	switch eventType {
	case EventUserRegistered:
		var ev UserRegistered
		_ = json.Unmarshal([]byte(payload), &ev)
		state.Items[ev.UserId] = UserDirectoryListItem{
			UserId:                  ev.UserId,
			DisplayName:             ev.DisplayName,
			Email:                   ev.Email,
			Department:              ev.Department,
			RegisteredAt:            ev.RegisteredAt,
			MonthlyReservationLimit: ev.MonthlyReservationLimit,
			IsActive:                true,
		}
	case EventUserProfileUpdated:
		var ev UserProfileUpdated
		_ = json.Unmarshal([]byte(payload), &ev)
		if item, ok := state.Items[ev.UserId]; ok {
			item.DisplayName = ev.DisplayName
			item.Email = ev.Email
			item.Department = ev.Department
			item.MonthlyReservationLimit = ev.MonthlyReservationLimit
			state.Items[ev.UserId] = item
		}
	}
	return state
}

// ---------------------------------------------------------------------------
// 5. Multi projector: UserAccessList
// ---------------------------------------------------------------------------

func ApplyUserAccessListEvent(state UserAccessListState, eventType, payload string) UserAccessListState {
	if state.Items == nil {
		state.Items = make(map[string]UserAccessListItem)
	}
	switch eventType {
	case EventUserAccessGranted:
		var ev UserAccessGranted
		_ = json.Unmarshal([]byte(payload), &ev)
		state.Items[ev.UserId] = UserAccessListItem{
			UserId:    ev.UserId,
			Roles:     []string{ev.InitialRole},
			GrantedAt: ev.GrantedAt,
			IsActive:  true,
		}
	case EventUserRoleGranted:
		var ev UserRoleGranted
		_ = json.Unmarshal([]byte(payload), &ev)
		if item, ok := state.Items[ev.UserId]; ok {
			if !containsString(item.Roles, ev.Role) {
				item.Roles = append(item.Roles, ev.Role)
			}
			state.Items[ev.UserId] = item
		}
	}
	return state
}

// ---------------------------------------------------------------------------
// 6. Multi projector: RoomList
// ---------------------------------------------------------------------------

func ApplyRoomListEvent(state RoomListState, eventType, payload string) RoomListState {
	if state.Items == nil {
		state.Items = make(map[string]RoomListItem)
	}
	switch eventType {
	case EventRoomCreated:
		var ev RoomCreated
		_ = json.Unmarshal([]byte(payload), &ev)
		state.Items[ev.RoomId] = RoomListItem{
			RoomId:           ev.RoomId,
			Name:             ev.Name,
			Capacity:         ev.Capacity,
			Location:         ev.Location,
			Equipment:        ev.Equipment,
			RequiresApproval: ev.RequiresApproval,
			IsActive:         true,
		}
	case EventRoomUpdated:
		var ev RoomUpdated
		_ = json.Unmarshal([]byte(payload), &ev)
		if item, ok := state.Items[ev.RoomId]; ok {
			item.Name = ev.Name
			item.Capacity = ev.Capacity
			item.Location = ev.Location
			item.Equipment = ev.Equipment
			item.RequiresApproval = ev.RequiresApproval
			state.Items[ev.RoomId] = item
		}
	}
	return state
}

// ---------------------------------------------------------------------------
// 7. Multi projector: ReservationList
// ---------------------------------------------------------------------------

func ApplyReservationListEvent(state ReservationListState, eventType, payload string) ReservationListState {
	if state.Items == nil {
		state.Items = make(map[string]ReservationListItem)
	}
	switch eventType {
	case EventReservationDraftCreated:
		var ev ReservationDraftCreated
		_ = json.Unmarshal([]byte(payload), &ev)
		state.Items[ev.ReservationId] = ReservationListItem{
			ReservationId:     ev.ReservationId,
			RoomId:            ev.RoomId,
			OrganizerId:       ev.OrganizerId,
			OrganizerName:     ev.OrganizerName,
			StartTime:         ev.StartTime,
			EndTime:           ev.EndTime,
			Purpose:           ev.Purpose,
			SelectedEquipment: ev.SelectedEquipment,
			Status:            "Draft",
		}
	case EventReservationHoldCommitted:
		var ev ReservationHoldCommitted
		_ = json.Unmarshal([]byte(payload), &ev)
		if item, ok := state.Items[ev.ReservationId]; ok {
			item.Status = "Held"
			item.RequiresApproval = ev.RequiresApproval
			item.ApprovalRequestId = ev.ApprovalRequestId
			if ev.SelectedEquipment != nil {
				item.SelectedEquipment = ev.SelectedEquipment
			}
			state.Items[ev.ReservationId] = item
		}
	case EventReservationConfirmed:
		var ev ReservationConfirmed
		_ = json.Unmarshal([]byte(payload), &ev)
		if item, ok := state.Items[ev.ReservationId]; ok {
			item.Status = "Confirmed"
			confirmedAt := ev.ConfirmedAt
			item.ConfirmedAt = &confirmedAt
			state.Items[ev.ReservationId] = item
		}
	case EventReservationCancelled:
		var ev ReservationCancelled
		_ = json.Unmarshal([]byte(payload), &ev)
		if item, ok := state.Items[ev.ReservationId]; ok {
			item.Status = "Cancelled"
			cancelledAt := ev.CancelledAt
			item.CancelledAt = &cancelledAt
			state.Items[ev.ReservationId] = item
		}
	case EventReservationRejected:
		var ev ReservationRejected
		_ = json.Unmarshal([]byte(payload), &ev)
		if item, ok := state.Items[ev.ReservationId]; ok {
			item.Status = "Rejected"
			rejectedAt := ev.RejectedAt
			item.RejectedAt = &rejectedAt
			state.Items[ev.ReservationId] = item
		}
	}
	return state
}

// ---------------------------------------------------------------------------
// 8. Multi projector: ApprovalRequestList
// ---------------------------------------------------------------------------

func ApplyApprovalRequestListEvent(state ApprovalRequestListState, eventType, payload string) ApprovalRequestListState {
	if state.Items == nil {
		state.Items = make(map[string]ApprovalInboxItem)
	}
	switch eventType {
	case EventApprovalFlowStarted:
		var ev ApprovalFlowStarted
		_ = json.Unmarshal([]byte(payload), &ev)
		state.Items[ev.ApprovalRequestId] = ApprovalInboxItem{
			ApprovalRequestId: ev.ApprovalRequestId,
			ReservationId:     ev.ReservationId,
			RoomId:            ev.RoomId,
			RequesterId:       ev.RequesterId,
			ApproverIds:       ev.ApproverIds,
			RequestedAt:       ev.RequestedAt,
			RequestComment:    ev.RequestComment,
			Status:            "Pending",
		}
	case EventApprovalDecisionRecorded:
		var ev ApprovalDecisionRecorded
		_ = json.Unmarshal([]byte(payload), &ev)
		if item, ok := state.Items[ev.ApprovalRequestId]; ok {
			item.ApproverId = &ev.ApproverId
			item.DecisionComment = ev.Comment
			item.DecidedAt = ev.DecidedAt
			if ev.Decision == "Approved" {
				item.Status = "Approved"
			} else {
				item.Status = "Rejected"
			}
			state.Items[ev.ApprovalRequestId] = item
		}
	}
	return state
}

// ===========================================================================
// Query execution functions
// ===========================================================================

// ExecuteWeatherListQuery filters weather forecasts, excludes deleted, sorts by CreatedAt desc.
func ExecuteWeatherListQuery(state WeatherForecastListState, queryJson string) ([]WeatherForecastItem, error) {
	var query WeatherListQuery
	_ = json.Unmarshal([]byte(queryJson), &query)

	if state.Items == nil {
		return []WeatherForecastItem{}, nil
	}

	var items []WeatherForecastItem
	for _, item := range state.Items {
		if item.IsDeleted {
			continue
		}
		if query.LocationFilter != nil && *query.LocationFilter != "" {
			if !strings.Contains(strings.ToLower(item.Location), strings.ToLower(*query.LocationFilter)) {
				continue
			}
		}
		if query.ForecastId != nil && *query.ForecastId != "" {
			if item.ForecastId != *query.ForecastId {
				continue
			}
		}
		items = append(items, item)
	}

	// Sort by Date descending (as proxy for CreatedAt desc)
	sort.Slice(items, func(i, j int) bool {
		return items[i].Date > items[j].Date
	})

	return sekiban.ApplyPaging(items, query.PagingQuery), nil
}

// ExecuteWeatherCountQuery counts non-deleted weather forecasts.
func ExecuteWeatherCountQuery(state WeatherForecastListState, queryJson string) (sekiban.CountResult, error) {
	if state.Items == nil {
		return sekiban.CountResult{Count: 0}, nil
	}
	count := 0
	for _, item := range state.Items {
		if !item.IsDeleted {
			count++
		}
	}
	return sekiban.CountResult{Count: count}, nil
}

// ExecuteStudentListQuery returns students sorted by Name with paging.
func ExecuteStudentListQuery(state StudentListState, queryJson string) ([]StudentState, error) {
	var query sekiban.PagingQuery
	_ = json.Unmarshal([]byte(queryJson), &query)

	if state.Items == nil {
		return []StudentState{}, nil
	}

	var items []StudentState
	for _, s := range state.Items {
		items = append(items, s)
	}

	sort.Slice(items, func(i, j int) bool {
		return items[i].Name < items[j].Name
	})

	return sekiban.ApplyPaging(items, query), nil
}

// ExecuteClassRoomListQuery returns classroom items sorted by Name.
func ExecuteClassRoomListQuery(state ClassRoomListState, queryJson string) ([]ClassRoomItem, error) {
	var query sekiban.PagingQuery
	_ = json.Unmarshal([]byte(queryJson), &query)

	if state.Items == nil {
		return []ClassRoomItem{}, nil
	}

	var items []ClassRoomItem
	for _, item := range state.Items {
		items = append(items, item)
	}

	sort.Slice(items, func(i, j int) bool {
		return items[i].Name < items[j].Name
	})

	return sekiban.ApplyPaging(items, query), nil
}

// ExecuteRoomListQuery returns room list items sorted by Name.
func ExecuteRoomListQuery(state RoomListState, queryJson string) ([]RoomListItem, error) {
	var query sekiban.PagingQuery
	_ = json.Unmarshal([]byte(queryJson), &query)

	if state.Items == nil {
		return []RoomListItem{}, nil
	}

	var items []RoomListItem
	for _, item := range state.Items {
		items = append(items, item)
	}

	sort.Slice(items, func(i, j int) bool {
		return items[i].Name < items[j].Name
	})

	return sekiban.ApplyPaging(items, query), nil
}

// ExecuteReservationListQuery returns reservations, optionally filtered by roomId.
func ExecuteReservationListQuery(state ReservationListState, queryJson string) ([]ReservationListItem, error) {
	var query ReservationListQuery
	_ = json.Unmarshal([]byte(queryJson), &query)

	if state.Items == nil {
		return []ReservationListItem{}, nil
	}

	var items []ReservationListItem
	for _, item := range state.Items {
		if query.RoomId != nil && *query.RoomId != "" {
			if item.RoomId != *query.RoomId {
				continue
			}
		}
		items = append(items, item)
	}

	sort.Slice(items, func(i, j int) bool {
		return items[i].StartTime > items[j].StartTime
	})

	return sekiban.ApplyPaging(items, query.PagingQuery), nil
}

// ExecuteApprovalInboxQuery returns approval requests, optionally filtered to pending only.
func ExecuteApprovalInboxQuery(state ApprovalRequestListState, queryJson string) ([]ApprovalInboxItem, error) {
	var query ApprovalListQuery
	_ = json.Unmarshal([]byte(queryJson), &query)

	if state.Items == nil {
		return []ApprovalInboxItem{}, nil
	}

	var items []ApprovalInboxItem
	for _, item := range state.Items {
		if query.PendingOnly != nil && *query.PendingOnly && item.Status != "Pending" {
			continue
		}
		items = append(items, item)
	}

	sort.Slice(items, func(i, j int) bool {
		return items[i].RequestedAt > items[j].RequestedAt
	})

	return sekiban.ApplyPaging(items, query.PagingQuery), nil
}

// ExecuteUserDirectoryListQuery returns user directory items sorted by DisplayName.
func ExecuteUserDirectoryListQuery(state UserDirectoryListState, queryJson string) ([]UserDirectoryListItem, error) {
	var query UserDirectoryListQuery
	_ = json.Unmarshal([]byte(queryJson), &query)

	if state.Items == nil {
		return []UserDirectoryListItem{}, nil
	}

	var items []UserDirectoryListItem
	for _, item := range state.Items {
		if query.ActiveOnly != nil && *query.ActiveOnly && !item.IsActive {
			continue
		}
		items = append(items, item)
	}

	sort.Slice(items, func(i, j int) bool {
		return items[i].DisplayName < items[j].DisplayName
	})

	return sekiban.ApplyPaging(items, query.PagingQuery), nil
}

// ExecuteUserAccessListQuery returns user access items with optional role filtering.
func ExecuteUserAccessListQuery(state UserAccessListState, queryJson string) ([]UserAccessListItem, error) {
	var query UserAccessListQuery
	_ = json.Unmarshal([]byte(queryJson), &query)

	if state.Items == nil {
		return []UserAccessListItem{}, nil
	}

	var items []UserAccessListItem
	for _, item := range state.Items {
		if query.ActiveOnly != nil && *query.ActiveOnly && !item.IsActive {
			continue
		}
		if query.RoleFilter != nil && *query.RoleFilter != "" {
			if !containsString(item.Roles, *query.RoleFilter) {
				continue
			}
		}
		items = append(items, item)
	}

	sort.Slice(items, func(i, j int) bool {
		return items[i].GrantedAt > items[j].GrantedAt
	})

	return sekiban.ApplyPaging(items, query.PagingQuery), nil
}
