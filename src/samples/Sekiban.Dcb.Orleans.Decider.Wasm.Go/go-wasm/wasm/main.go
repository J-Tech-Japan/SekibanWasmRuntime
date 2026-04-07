package main

import (
	"encoding/json"
	"sort"
	"strings"

	"sekiban-dcb-decider-go/domain"

	"github.com/J-Tech-Japan/sekiban-go/wasm"
)

// ---------------------------------------------------------------------------
// Projector kind enum
// ---------------------------------------------------------------------------

const (
	kindUnknown           = 0
	kindWeatherTag        = 1
	kindStudentTag        = 2
	kindClassRoomTag      = 3
	kindUserDirectoryTag  = 4
	kindUserAccessTag     = 5
	kindRoomTag           = 6
	kindReservationTag    = 7
	kindApprovalTag       = 8
	kindWeatherList       = 9
	kindStudentList       = 10
	kindClassRoomList     = 11
	kindUserDirectoryList = 12
	kindUserAccessList    = 13
	kindRoomList          = 14
	kindReservationList   = 15
	kindApprovalList      = 16
)

// ---------------------------------------------------------------------------
// Instance storage
// ---------------------------------------------------------------------------

type instance struct {
	kind               int
	weatherTag         domain.WeatherForecastState
	studentTag         domain.StudentState
	classRoomTag       domain.ClassRoomState
	userDirectoryTag   domain.UserDirectoryState
	userAccessTag      domain.UserAccessState
	roomTag            domain.RoomState
	reservationTag     domain.ReservationState
	approvalRequestTag domain.ApprovalRequestState
	weatherList        domain.WeatherForecastListState
	studentList        domain.StudentListState
	classRoomList      domain.ClassRoomListState
	userDirectoryList  domain.UserDirectoryListState
	userAccessList     domain.UserAccessListState
	roomList           domain.RoomListState
	reservationList    domain.ReservationListState
	approvalList       domain.ApprovalRequestListState
}

func (inst *instance) reset() {
	inst.weatherList.Items = make(map[string]domain.WeatherForecastItem)
	inst.studentList.Items = make(map[string]domain.StudentState)
	inst.classRoomList.Items = make(map[string]domain.ClassRoomItem)
	inst.userDirectoryList.Items = make(map[string]domain.UserDirectoryListItem)
	inst.userAccessList.Items = make(map[string]domain.UserAccessListItem)
	inst.roomList.Items = make(map[string]domain.RoomListItem)
	inst.reservationList.Items = make(map[string]domain.ReservationListItem)
	inst.approvalList.Items = make(map[string]domain.ApprovalInboxItem)
}

var (
	instances       = make(map[int32]*instance)
	nextID    int32 = 1
)

func newInstance(kind int) *instance {
	inst := &instance{kind: kind}
	inst.reset()
	return inst
}

// ---------------------------------------------------------------------------
// Resolve projector kind from name
// ---------------------------------------------------------------------------

func resolveKind(name string) int {
	lower := strings.ToLower(name)
	switch {
	case lower == strings.ToLower(domain.ProjectorWeatherTag):
		return kindWeatherTag
	case lower == strings.ToLower(domain.ProjectorStudentTag):
		return kindStudentTag
	case lower == strings.ToLower(domain.ProjectorClassRoomTag):
		return kindClassRoomTag
	case lower == strings.ToLower(domain.ProjectorUserDirectoryTag):
		return kindUserDirectoryTag
	case lower == strings.ToLower(domain.ProjectorUserAccessTag):
		return kindUserAccessTag
	case lower == strings.ToLower(domain.ProjectorRoomTag):
		return kindRoomTag
	case lower == strings.ToLower(domain.ProjectorReservationTag):
		return kindReservationTag
	case lower == strings.ToLower(domain.ProjectorApprovalRequestTag):
		return kindApprovalTag
	case lower == strings.ToLower(domain.ProjectorWeatherList):
		return kindWeatherList
	case lower == strings.ToLower(domain.ProjectorStudentList):
		return kindStudentList
	case lower == strings.ToLower(domain.ProjectorClassRoomList):
		return kindClassRoomList
	case lower == strings.ToLower(domain.ProjectorUserDirectoryList):
		return kindUserDirectoryList
	case lower == strings.ToLower(domain.ProjectorUserAccessList):
		return kindUserAccessList
	case lower == strings.ToLower(domain.ProjectorRoomList):
		return kindRoomList
	case lower == strings.ToLower(domain.ProjectorReservationList):
		return kindReservationList
	case lower == strings.ToLower(domain.ProjectorApprovalRequestList):
		return kindApprovalList
	}
	return kindUnknown
}

// ---------------------------------------------------------------------------
// Memory exports
// ---------------------------------------------------------------------------

//export alloc
func alloc(size uint32) uint32 { return wasm.Alloc(size) }

//export dealloc
func dealloc(ptr, length uint32) { wasm.Dealloc(ptr, length) }

//export reset_allocations
func reset_allocations() { wasm.ResetAllocations() }

// ---------------------------------------------------------------------------
// Instance lifecycle exports
// ---------------------------------------------------------------------------

//export create_instance
func create_instance(namePtr, nameLen uint32) int32 {
	name := wasm.ReadString(namePtr, nameLen)
	kind := resolveKind(name)
	if kind == kindUnknown {
		return -1
	}
	id := nextID
	nextID++
	instances[id] = newInstance(kind)
	return id
}

// ---------------------------------------------------------------------------
// Event application
// ---------------------------------------------------------------------------

//export apply_event
func apply_event(instanceId int32, etPtr, etLen, pPtr, pLen uint32) {
	inst, ok := instances[instanceId]
	if !ok {
		return
	}
	eventType := wasm.ReadString(etPtr, etLen)
	payload := wasm.ReadString(pPtr, pLen)
	applyEventToInstance(inst, eventType, payload)
}

//export apply_event_with_metadata
func apply_event_with_metadata(instanceId int32, etPtr, etLen, pPtr, pLen, mPtr, mLen uint32) {
	// metadata is ignored
	apply_event(instanceId, etPtr, etLen, pPtr, pLen)
}

func applyEventToInstance(inst *instance, eventType, payload string) {
	switch inst.kind {
	case kindWeatherTag:
		applyWeatherTag(&inst.weatherTag, eventType, payload)
	case kindStudentTag:
		applyStudentTag(&inst.studentTag, eventType, payload)
	case kindClassRoomTag:
		applyClassRoomTag(&inst.classRoomTag, eventType, payload)
	case kindUserDirectoryTag:
		applyUserDirectoryTag(&inst.userDirectoryTag, eventType, payload)
	case kindUserAccessTag:
		applyUserAccessTag(&inst.userAccessTag, eventType, payload)
	case kindRoomTag:
		applyRoomTag(&inst.roomTag, eventType, payload)
	case kindReservationTag:
		applyReservationTag(&inst.reservationTag, eventType, payload)
	case kindApprovalTag:
		applyApprovalRequestTag(&inst.approvalRequestTag, eventType, payload)
	case kindWeatherList:
		applyWeatherList(&inst.weatherList, eventType, payload)
	case kindStudentList:
		applyStudentList(&inst.studentList, eventType, payload)
	case kindClassRoomList:
		applyClassRoomList(&inst.classRoomList, eventType, payload)
	case kindUserDirectoryList:
		applyUserDirectoryList(&inst.userDirectoryList, eventType, payload)
	case kindUserAccessList:
		applyUserAccessList(&inst.userAccessList, eventType, payload)
	case kindRoomList:
		applyRoomList(&inst.roomList, eventType, payload)
	case kindReservationList:
		applyReservationList(&inst.reservationList, eventType, payload)
	case kindApprovalList:
		applyApprovalInboxList(&inst.approvalList, eventType, payload)
	}
}

// ---------------------------------------------------------------------------
// State serialization / restoration
// ---------------------------------------------------------------------------

//export serialize_state
func serialize_state(instanceId int32) int64 {
	inst, ok := instances[instanceId]
	if !ok {
		return wasm.WriteString("{}")
	}
	return wasm.WriteString(serializeInstanceState(inst))
}

//export restore_state
func restore_state(instanceId int32, sPtr, sLen uint32) {
	inst, ok := instances[instanceId]
	if !ok {
		return
	}
	stateJSON := wasm.ReadString(sPtr, sLen)
	restoreInstanceState(inst, stateJSON)
}

func serializeInstanceState(inst *instance) string {
	switch inst.kind {
	case kindWeatherTag:
		if inst.weatherTag.ForecastId == "" {
			return "{}"
		}
		return wasm.MustJSON(inst.weatherTag)
	case kindStudentTag:
		if inst.studentTag.StudentId == "" {
			return "{}"
		}
		return wasm.MustJSON(inst.studentTag)
	case kindClassRoomTag:
		if inst.classRoomTag.ClassRoomId == "" {
			return "{}"
		}
		return wasm.MustJSON(inst.classRoomTag)
	case kindUserDirectoryTag:
		if inst.userDirectoryTag.UserId == "" {
			return "{}"
		}
		return wasm.MustJSON(inst.userDirectoryTag)
	case kindUserAccessTag:
		if inst.userAccessTag.UserId == "" {
			return "{}"
		}
		return wasm.MustJSON(inst.userAccessTag)
	case kindRoomTag:
		if inst.roomTag.RoomId == "" {
			return "{}"
		}
		return wasm.MustJSON(inst.roomTag)
	case kindReservationTag:
		if inst.reservationTag.ReservationId == "" {
			return "{}"
		}
		return wasm.MustJSON(inst.reservationTag)
	case kindApprovalTag:
		if inst.approvalRequestTag.ApprovalRequestId == "" {
			return "{}"
		}
		return wasm.MustJSON(inst.approvalRequestTag)
	case kindWeatherList:
		return wasm.MustJSON(inst.weatherList)
	case kindStudentList:
		return wasm.MustJSON(inst.studentList)
	case kindClassRoomList:
		return wasm.MustJSON(inst.classRoomList)
	case kindUserDirectoryList:
		return wasm.MustJSON(inst.userDirectoryList)
	case kindUserAccessList:
		return wasm.MustJSON(inst.userAccessList)
	case kindRoomList:
		return wasm.MustJSON(inst.roomList)
	case kindReservationList:
		return wasm.MustJSON(inst.reservationList)
	case kindApprovalList:
		return wasm.MustJSON(inst.approvalList)
	}
	return "{}"
}

func restoreInstanceState(inst *instance, stateJSON string) {
	trimmed := strings.TrimSpace(stateJSON)
	if trimmed == "" || trimmed == "{}" || trimmed == "null" {
		*inst = *newInstance(inst.kind)
		return
	}
	switch inst.kind {
	case kindWeatherTag:
		json.Unmarshal([]byte(stateJSON), &inst.weatherTag)
	case kindStudentTag:
		json.Unmarshal([]byte(stateJSON), &inst.studentTag)
	case kindClassRoomTag:
		json.Unmarshal([]byte(stateJSON), &inst.classRoomTag)
	case kindUserDirectoryTag:
		json.Unmarshal([]byte(stateJSON), &inst.userDirectoryTag)
	case kindUserAccessTag:
		json.Unmarshal([]byte(stateJSON), &inst.userAccessTag)
	case kindRoomTag:
		json.Unmarshal([]byte(stateJSON), &inst.roomTag)
	case kindReservationTag:
		json.Unmarshal([]byte(stateJSON), &inst.reservationTag)
	case kindApprovalTag:
		json.Unmarshal([]byte(stateJSON), &inst.approvalRequestTag)
	case kindWeatherList:
		json.Unmarshal([]byte(stateJSON), &inst.weatherList)
		if inst.weatherList.Items == nil {
			inst.weatherList.Items = make(map[string]domain.WeatherForecastItem)
		}
	case kindStudentList:
		json.Unmarshal([]byte(stateJSON), &inst.studentList)
		if inst.studentList.Items == nil {
			inst.studentList.Items = make(map[string]domain.StudentState)
		}
	case kindClassRoomList:
		json.Unmarshal([]byte(stateJSON), &inst.classRoomList)
		if inst.classRoomList.Items == nil {
			inst.classRoomList.Items = make(map[string]domain.ClassRoomItem)
		}
	case kindUserDirectoryList:
		json.Unmarshal([]byte(stateJSON), &inst.userDirectoryList)
		if inst.userDirectoryList.Items == nil {
			inst.userDirectoryList.Items = make(map[string]domain.UserDirectoryListItem)
		}
	case kindUserAccessList:
		json.Unmarshal([]byte(stateJSON), &inst.userAccessList)
		if inst.userAccessList.Items == nil {
			inst.userAccessList.Items = make(map[string]domain.UserAccessListItem)
		}
	case kindRoomList:
		json.Unmarshal([]byte(stateJSON), &inst.roomList)
		if inst.roomList.Items == nil {
			inst.roomList.Items = make(map[string]domain.RoomListItem)
		}
	case kindReservationList:
		json.Unmarshal([]byte(stateJSON), &inst.reservationList)
		if inst.reservationList.Items == nil {
			inst.reservationList.Items = make(map[string]domain.ReservationListItem)
		}
	case kindApprovalList:
		json.Unmarshal([]byte(stateJSON), &inst.approvalList)
		if inst.approvalList.Items == nil {
			inst.approvalList.Items = make(map[string]domain.ApprovalInboxItem)
		}
	}
}

// ---------------------------------------------------------------------------
// Query execution
// ---------------------------------------------------------------------------

//export execute_query
func execute_query(instanceId int32, qtPtr, qtLen, pPtr, pLen uint32) int64 {
	inst, ok := instances[instanceId]
	if !ok {
		return wasm.WriteString("null")
	}
	queryType := wasm.ReadString(qtPtr, qtLen)
	paramsJSON := wasm.ReadString(pPtr, pLen)
	return wasm.WriteString(executeQueryJSON(inst, queryType, paramsJSON))
}

//export execute_list_query
func execute_list_query(instanceId int32, qtPtr, qtLen, pPtr, pLen uint32) int64 {
	inst, ok := instances[instanceId]
	if !ok {
		return wasm.WriteString("[]")
	}
	queryType := wasm.ReadString(qtPtr, qtLen)
	paramsJSON := wasm.ReadString(pPtr, pLen)
	return wasm.WriteString(executeListQueryJSON(inst, queryType, paramsJSON))
}

// ---------------------------------------------------------------------------
// Query types
// ---------------------------------------------------------------------------

const (
	queryWeatherList       = "GetWeatherForecastListQuery"
	queryWeatherCount      = "GetWeatherForecastCountQuery"
	queryStudentList       = "GetStudentListQuery"
	queryClassRoomList     = "GetClassRoomListQuery"
	queryUserDirectoryList = "GetUserDirectoryListQuery"
	queryUserAccessList    = "GetUserAccessListQuery"
	queryRoomList          = "GetRoomListQuery"
	queryReservationList   = "GetReservationListQuery"
	queryApprovalInbox     = "GetApprovalInboxQuery"
)

type weatherListQuery struct {
	LocationFilter *string `json:"locationFilter"`
	ForecastId     *string `json:"forecastId"`
	PageNumber     *int    `json:"pageNumber"`
	PageSize       *int    `json:"pageSize"`
}

type weatherCountQuery struct {
	LocationFilter *string `json:"locationFilter"`
	ForecastId     *string `json:"forecastId"`
}

type pagingQuery struct {
	PageNumber *int `json:"pageNumber"`
	PageSize   *int `json:"pageSize"`
}

type reservationListQuery struct {
	PageNumber *int    `json:"pageNumber"`
	PageSize   *int    `json:"pageSize"`
	RoomId     *string `json:"roomId"`
}

type approvalListQuery struct {
	PageNumber  *int `json:"pageNumber"`
	PageSize    *int `json:"pageSize"`
	PendingOnly bool `json:"pendingOnly"`
}

type userDirectoryListQuery struct {
	PageNumber *int `json:"pageNumber"`
	PageSize   *int `json:"pageSize"`
	ActiveOnly bool `json:"activeOnly"`
}

type userAccessListQuery struct {
	PageNumber *int    `json:"pageNumber"`
	PageSize   *int    `json:"pageSize"`
	ActiveOnly bool    `json:"activeOnly"`
	RoleFilter *string `json:"roleFilter"`
}

type countResult struct {
	Count int `json:"count"`
}

func executeQueryJSON(inst *instance, queryType, paramsJSON string) string {
	if inst.kind == kindWeatherList && queryType == queryWeatherCount {
		var q weatherCountQuery
		json.Unmarshal([]byte(paramsJSON), &q)
		items := weatherItems(&inst.weatherList, &weatherListQuery{
			LocationFilter: q.LocationFilter,
			ForecastId:     q.ForecastId,
		})
		return wasm.MustJSON(countResult{Count: len(items)})
	}
	return "null"
}

func executeListQueryJSON(inst *instance, queryType, paramsJSON string) string {
	switch inst.kind {
	case kindWeatherList:
		if queryType == queryWeatherList {
			var q weatherListQuery
			json.Unmarshal([]byte(paramsJSON), &q)
			return wasm.MustJSON(weatherItems(&inst.weatherList, &q))
		}
	case kindStudentList:
		if queryType == queryStudentList {
			var q pagingQuery
			json.Unmarshal([]byte(paramsJSON), &q)
			return wasm.MustJSON(studentItems(&inst.studentList, &q))
		}
	case kindClassRoomList:
		if queryType == queryClassRoomList {
			var q pagingQuery
			json.Unmarshal([]byte(paramsJSON), &q)
			return wasm.MustJSON(classRoomItems(&inst.classRoomList, &q))
		}
	case kindUserDirectoryList:
		if queryType == queryUserDirectoryList {
			var q userDirectoryListQuery
			json.Unmarshal([]byte(paramsJSON), &q)
			return wasm.MustJSON(userDirectoryItems(&inst.userDirectoryList, &q))
		}
	case kindUserAccessList:
		if queryType == queryUserAccessList {
			var q userAccessListQuery
			json.Unmarshal([]byte(paramsJSON), &q)
			return wasm.MustJSON(userAccessItems(&inst.userAccessList, &q))
		}
	case kindRoomList:
		if queryType == queryRoomList {
			var q pagingQuery
			json.Unmarshal([]byte(paramsJSON), &q)
			return wasm.MustJSON(roomItems(&inst.roomList, &q))
		}
	case kindReservationList:
		if queryType == queryReservationList {
			var q reservationListQuery
			json.Unmarshal([]byte(paramsJSON), &q)
			return wasm.MustJSON(reservationItems(&inst.reservationList, &q))
		}
	case kindApprovalList:
		if queryType == queryApprovalInbox {
			var q approvalListQuery
			json.Unmarshal([]byte(paramsJSON), &q)
			return wasm.MustJSON(approvalItems(&inst.approvalList, &q))
		}
	}
	return "[]"
}

// ---------------------------------------------------------------------------
// Query item helpers
// ---------------------------------------------------------------------------

func matchesOptionalFilter(value string, filter *string) bool {
	if filter == nil || *filter == "" {
		return true
	}
	return strings.EqualFold(value, *filter)
}

func weatherItems(state *domain.WeatherForecastListState, q *weatherListQuery) []domain.WeatherForecastItem {
	items := make([]domain.WeatherForecastItem, 0, len(state.Items))
	for _, v := range state.Items {
		if q.ForecastId != nil && *q.ForecastId != "" && v.ForecastId != *q.ForecastId {
			continue
		}
		if !matchesOptionalFilter(v.Location, q.LocationFilter) {
			continue
		}
		items = append(items, v)
	}
	sort.Slice(items, func(i, j int) bool { return items[i].Location < items[j].Location })
	return applyPaging(items, q.PageNumber, q.PageSize)
}

func studentItems(state *domain.StudentListState, q *pagingQuery) []domain.StudentState {
	items := make([]domain.StudentState, 0, len(state.Items))
	for _, v := range state.Items {
		items = append(items, v)
	}
	sort.Slice(items, func(i, j int) bool { return items[i].Name < items[j].Name })
	return applyPaging(items, q.PageNumber, q.PageSize)
}

func classRoomItems(state *domain.ClassRoomListState, q *pagingQuery) []domain.ClassRoomItem {
	items := make([]domain.ClassRoomItem, 0, len(state.Items))
	for _, v := range state.Items {
		items = append(items, v)
	}
	sort.Slice(items, func(i, j int) bool { return items[i].Name < items[j].Name })
	return applyPaging(items, q.PageNumber, q.PageSize)
}

func userDirectoryItems(state *domain.UserDirectoryListState, q *userDirectoryListQuery) []domain.UserDirectoryListItem {
	items := make([]domain.UserDirectoryListItem, 0, len(state.Items))
	for _, v := range state.Items {
		if q.ActiveOnly && !v.IsActive {
			continue
		}
		items = append(items, v)
	}
	sort.Slice(items, func(i, j int) bool { return items[i].DisplayName < items[j].DisplayName })
	return applyPaging(items, q.PageNumber, q.PageSize)
}

func userAccessItems(state *domain.UserAccessListState, q *userAccessListQuery) []domain.UserAccessListItem {
	items := make([]domain.UserAccessListItem, 0, len(state.Items))
	for _, v := range state.Items {
		if q.ActiveOnly && !v.IsActive {
			continue
		}
		if q.RoleFilter != nil && *q.RoleFilter != "" {
			found := false
			for _, r := range v.Roles {
				if r == *q.RoleFilter {
					found = true
					break
				}
			}
			if !found {
				continue
			}
		}
		items = append(items, v)
	}
	sort.Slice(items, func(i, j int) bool { return items[i].GrantedAt > items[j].GrantedAt })
	return applyPaging(items, q.PageNumber, q.PageSize)
}

func roomItems(state *domain.RoomListState, q *pagingQuery) []domain.RoomListItem {
	items := make([]domain.RoomListItem, 0, len(state.Items))
	for _, v := range state.Items {
		items = append(items, v)
	}
	sort.Slice(items, func(i, j int) bool { return items[i].Name < items[j].Name })
	return applyPaging(items, q.PageNumber, q.PageSize)
}

func reservationItems(state *domain.ReservationListState, q *reservationListQuery) []domain.ReservationListItem {
	items := make([]domain.ReservationListItem, 0, len(state.Items))
	for _, v := range state.Items {
		if q.RoomId != nil && *q.RoomId != "" && v.RoomId != *q.RoomId {
			continue
		}
		items = append(items, v)
	}
	sort.Slice(items, func(i, j int) bool { return items[i].StartTime < items[j].StartTime })
	return applyPaging(items, q.PageNumber, q.PageSize)
}

func approvalItems(state *domain.ApprovalRequestListState, q *approvalListQuery) []domain.ApprovalInboxItem {
	items := make([]domain.ApprovalInboxItem, 0, len(state.Items))
	for _, v := range state.Items {
		if q.PendingOnly && v.Status != "Pending" {
			continue
		}
		items = append(items, v)
	}
	sort.Slice(items, func(i, j int) bool { return items[i].RequestedAt > items[j].RequestedAt })
	return applyPaging(items, q.PageNumber, q.PageSize)
}

func applyPaging[T any](items []T, pageNumber, pageSize *int) []T {
	if pageSize == nil || *pageSize <= 0 {
		return items
	}
	ps := *pageSize
	pn := 1
	if pageNumber != nil && *pageNumber > 0 {
		pn = *pageNumber
	}
	start := (pn - 1) * ps
	if start >= len(items) {
		return []T{}
	}
	end := start + ps
	if end > len(items) {
		end = len(items)
	}
	return items[start:end]
}

// ---------------------------------------------------------------------------
// Apply event helpers: Tag projectors
// ---------------------------------------------------------------------------

func applyWeatherTag(s *domain.WeatherForecastState, eventType, payload string) {
	switch eventType {
	case domain.EventWeatherForecastCreated:
		var ev domain.WeatherForecastCreated
		json.Unmarshal([]byte(payload), &ev)
		s.ForecastId = ev.ForecastId
		s.Location = ev.Location
		s.Date = ev.Date
		s.TemperatureC = ev.TemperatureC
		s.Summary = ev.Summary
		s.CreatedAt = ev.CreatedAt
		s.IsDeleted = false
		s.DeletedAt = nil
	case domain.EventWeatherForecastLocationUpdated:
		var ev domain.WeatherForecastLocationUpdated
		json.Unmarshal([]byte(payload), &ev)
		s.Location = ev.NewLocation
	case domain.EventWeatherForecastDeleted:
		var ev domain.WeatherForecastDeleted
		json.Unmarshal([]byte(payload), &ev)
		s.IsDeleted = true
		s.DeletedAt = &ev.DeletedAt
	}
}

func applyStudentTag(s *domain.StudentState, eventType, payload string) {
	switch eventType {
	case domain.EventStudentCreated:
		var ev domain.StudentCreated
		json.Unmarshal([]byte(payload), &ev)
		s.StudentId = ev.StudentId
		s.Name = ev.Name
		s.MaxClassCount = ev.MaxClassCount
		s.EnrolledClassRoomIds = []string{}
	case domain.EventStudentEnrolledInClassRoom:
		var ev domain.StudentEnrolledInClassRoom
		json.Unmarshal([]byte(payload), &ev)
		if !containsString(s.EnrolledClassRoomIds, ev.ClassRoomId) {
			s.EnrolledClassRoomIds = append(s.EnrolledClassRoomIds, ev.ClassRoomId)
		}
	case domain.EventStudentDroppedFromClassRoom:
		var ev domain.StudentDroppedFromClassRoom
		json.Unmarshal([]byte(payload), &ev)
		s.EnrolledClassRoomIds = removeString(s.EnrolledClassRoomIds, ev.ClassRoomId)
	}
}

func applyClassRoomTag(s *domain.ClassRoomState, eventType, payload string) {
	switch eventType {
	case domain.EventClassRoomCreated:
		var ev domain.ClassRoomCreated
		json.Unmarshal([]byte(payload), &ev)
		s.ClassRoomId = ev.ClassRoomId
		s.Name = ev.Name
		s.MaxStudents = ev.MaxStudents
		s.EnrolledStudentIds = []string{}
		s.IsFull = false
	case domain.EventStudentEnrolledInClassRoom:
		var ev domain.StudentEnrolledInClassRoom
		json.Unmarshal([]byte(payload), &ev)
		if !containsString(s.EnrolledStudentIds, ev.StudentId) {
			s.EnrolledStudentIds = append(s.EnrolledStudentIds, ev.StudentId)
		}
		s.IsFull = s.MaxStudents > 0 && len(s.EnrolledStudentIds) >= s.MaxStudents
	case domain.EventStudentDroppedFromClassRoom:
		var ev domain.StudentDroppedFromClassRoom
		json.Unmarshal([]byte(payload), &ev)
		s.EnrolledStudentIds = removeString(s.EnrolledStudentIds, ev.StudentId)
		s.IsFull = s.MaxStudents > 0 && len(s.EnrolledStudentIds) >= s.MaxStudents
	}
}

func applyUserDirectoryTag(s *domain.UserDirectoryState, eventType, payload string) {
	switch eventType {
	case domain.EventUserRegistered:
		var ev domain.UserRegistered
		json.Unmarshal([]byte(payload), &ev)
		s.UserId = ev.UserId
		s.DisplayName = ev.DisplayName
		s.Email = ev.Email
		s.Department = ev.Department
		s.RegisteredAt = ev.RegisteredAt
		s.MonthlyReservationLimit = ev.MonthlyReservationLimit
		s.ExternalProviders = []string{}
		s.IsActive = true
	case domain.EventUserProfileUpdated:
		var ev domain.UserProfileUpdated
		json.Unmarshal([]byte(payload), &ev)
		s.DisplayName = ev.DisplayName
		s.Email = ev.Email
		s.Department = ev.Department
		s.MonthlyReservationLimit = ev.MonthlyReservationLimit
	}
}

func applyUserAccessTag(s *domain.UserAccessState, eventType, payload string) {
	switch eventType {
	case domain.EventUserAccessGranted:
		var ev domain.UserAccessGranted
		json.Unmarshal([]byte(payload), &ev)
		s.UserId = ev.UserId
		s.Roles = []string{ev.InitialRole}
		s.GrantedAt = ev.GrantedAt
		s.IsActive = true
	case domain.EventUserRoleGranted:
		var ev domain.UserRoleGranted
		json.Unmarshal([]byte(payload), &ev)
		if !containsString(s.Roles, ev.Role) {
			s.Roles = append(s.Roles, ev.Role)
		}
	}
}

func applyRoomTag(s *domain.RoomState, eventType, payload string) {
	switch eventType {
	case domain.EventRoomCreated:
		var ev domain.RoomCreated
		json.Unmarshal([]byte(payload), &ev)
		s.RoomId = ev.RoomId
		s.Name = ev.Name
		s.Capacity = ev.Capacity
		s.Location = ev.Location
		s.Equipment = ev.Equipment
		s.RequiresApproval = ev.RequiresApproval
		s.IsActive = true
	case domain.EventRoomUpdated:
		var ev domain.RoomUpdated
		json.Unmarshal([]byte(payload), &ev)
		s.Name = ev.Name
		s.Capacity = ev.Capacity
		s.Location = ev.Location
		s.Equipment = ev.Equipment
		s.RequiresApproval = ev.RequiresApproval
	}
}

func applyReservationTag(s *domain.ReservationState, eventType, payload string) {
	switch eventType {
	case domain.EventReservationDraftCreated:
		var ev domain.ReservationDraftCreated
		json.Unmarshal([]byte(payload), &ev)
		s.ReservationId = ev.ReservationId
		s.RoomId = ev.RoomId
		s.OrganizerId = ev.OrganizerId
		s.OrganizerName = ev.OrganizerName
		s.StartTime = ev.StartTime
		s.EndTime = ev.EndTime
		s.Purpose = ev.Purpose
		s.SelectedEquipment = ev.SelectedEquipment
		s.Status = "Draft"
		s.RequiresApproval = false
		s.ApprovalRequestId = nil
		s.ApprovalRequestComment = nil
		s.ApprovalDecisionComment = nil
		s.ConfirmedAt = nil
		s.Reason = nil
	case domain.EventReservationHoldCommitted:
		var ev domain.ReservationHoldCommitted
		json.Unmarshal([]byte(payload), &ev)
		s.ReservationId = ev.ReservationId
		s.RoomId = ev.RoomId
		s.OrganizerId = ev.OrganizerId
		s.OrganizerName = ev.OrganizerName
		s.StartTime = ev.StartTime
		s.EndTime = ev.EndTime
		s.Purpose = ev.Purpose
		s.SelectedEquipment = ev.SelectedEquipment
		s.Status = "Held"
		s.RequiresApproval = ev.RequiresApproval
		s.ApprovalRequestId = ev.ApprovalRequestId
		s.ApprovalRequestComment = ev.ApprovalRequestComment
		s.ApprovalDecisionComment = nil
		s.ConfirmedAt = nil
		s.Reason = nil
	case domain.EventReservationConfirmed:
		var ev domain.ReservationConfirmed
		json.Unmarshal([]byte(payload), &ev)
		s.ReservationId = ev.ReservationId
		s.RoomId = ev.RoomId
		s.OrganizerId = ev.OrganizerId
		s.OrganizerName = ev.OrganizerName
		s.StartTime = ev.StartTime
		s.EndTime = ev.EndTime
		s.Purpose = ev.Purpose
		s.SelectedEquipment = ev.SelectedEquipment
		s.Status = "Confirmed"
		hasApproval := ev.ApprovalRequestId != nil && *ev.ApprovalRequestId != ""
		s.RequiresApproval = hasApproval
		s.ApprovalRequestId = ev.ApprovalRequestId
		s.ApprovalRequestComment = ev.ApprovalRequestComment
		s.ApprovalDecisionComment = ev.ApprovalDecisionComment
		s.ConfirmedAt = &ev.ConfirmedAt
		s.Reason = nil
	case domain.EventReservationCancelled:
		var ev domain.ReservationCancelled
		json.Unmarshal([]byte(payload), &ev)
		s.ReservationId = ev.ReservationId
		s.RoomId = ev.RoomId
		s.OrganizerId = ev.OrganizerId
		s.OrganizerName = ev.OrganizerName
		s.StartTime = ev.StartTime
		s.EndTime = ev.EndTime
		s.Purpose = ev.Purpose
		s.SelectedEquipment = ev.SelectedEquipment
		s.Status = "Cancelled"
		s.RequiresApproval = false
		s.ApprovalRequestId = nil
		s.ApprovalRequestComment = ev.ApprovalRequestComment
		s.ApprovalDecisionComment = &ev.Reason
		s.ConfirmedAt = nil
		s.Reason = &ev.Reason
	case domain.EventReservationRejected:
		var ev domain.ReservationRejected
		json.Unmarshal([]byte(payload), &ev)
		s.ReservationId = ev.ReservationId
		s.RoomId = ev.RoomId
		s.OrganizerId = ev.OrganizerId
		s.OrganizerName = ev.OrganizerName
		s.StartTime = ev.StartTime
		s.EndTime = ev.EndTime
		s.Purpose = ev.Purpose
		s.SelectedEquipment = ev.SelectedEquipment
		s.Status = "Rejected"
		s.RequiresApproval = true
		s.ApprovalRequestId = &ev.ApprovalRequestId
		s.ApprovalRequestComment = ev.ApprovalRequestComment
		s.ApprovalDecisionComment = &ev.Reason
		s.ConfirmedAt = nil
		s.Reason = &ev.Reason
	}
}

func applyApprovalRequestTag(s *domain.ApprovalRequestState, eventType, payload string) {
	switch eventType {
	case domain.EventApprovalFlowStarted:
		var ev domain.ApprovalFlowStarted
		json.Unmarshal([]byte(payload), &ev)
		s.ApprovalRequestId = ev.ApprovalRequestId
		s.ReservationId = ev.ReservationId
		s.RoomId = ev.RoomId
		s.RequesterId = ev.RequesterId
		s.ApproverIds = ev.ApproverIds
		s.RequestedAt = ev.RequestedAt
		s.RequestComment = ev.RequestComment
		s.Status = "Pending"
	case domain.EventApprovalDecisionRecorded:
		var ev domain.ApprovalDecisionRecorded
		json.Unmarshal([]byte(payload), &ev)
		if ev.Decision == "Rejected" {
			s.Status = "Rejected"
		} else {
			s.Status = "Approved"
		}
	}
}

// ---------------------------------------------------------------------------
// Apply event helpers: List projectors
// ---------------------------------------------------------------------------

func applyWeatherList(s *domain.WeatherForecastListState, eventType, payload string) {
	switch eventType {
	case domain.EventWeatherForecastCreated:
		var ev domain.WeatherForecastCreated
		json.Unmarshal([]byte(payload), &ev)
		s.Items[ev.ForecastId] = domain.WeatherForecastItem{
			ForecastId:   ev.ForecastId,
			Location:     ev.Location,
			Date:         ev.Date,
			TemperatureC: ev.TemperatureC,
			Summary:      ev.Summary,
			CreatedAt:    ev.CreatedAt,
		}
	case domain.EventWeatherForecastLocationUpdated:
		var ev domain.WeatherForecastLocationUpdated
		json.Unmarshal([]byte(payload), &ev)
		if cur, ok := s.Items[ev.ForecastId]; ok {
			cur.Location = ev.NewLocation
			s.Items[ev.ForecastId] = cur
		}
	case domain.EventWeatherForecastDeleted:
		var ev domain.WeatherForecastDeleted
		json.Unmarshal([]byte(payload), &ev)
		delete(s.Items, ev.ForecastId)
	}
}

func applyStudentList(s *domain.StudentListState, eventType, payload string) {
	switch eventType {
	case domain.EventStudentCreated:
		var ev domain.StudentCreated
		json.Unmarshal([]byte(payload), &ev)
		s.Items[ev.StudentId] = domain.StudentState{
			StudentId:            ev.StudentId,
			Name:                 ev.Name,
			MaxClassCount:        ev.MaxClassCount,
			EnrolledClassRoomIds: []string{},
		}
	case domain.EventStudentEnrolledInClassRoom:
		var ev domain.StudentEnrolledInClassRoom
		json.Unmarshal([]byte(payload), &ev)
		if cur, ok := s.Items[ev.StudentId]; ok {
			if !containsString(cur.EnrolledClassRoomIds, ev.ClassRoomId) {
				cur.EnrolledClassRoomIds = append(cur.EnrolledClassRoomIds, ev.ClassRoomId)
			}
			s.Items[ev.StudentId] = cur
		}
	case domain.EventStudentDroppedFromClassRoom:
		var ev domain.StudentDroppedFromClassRoom
		json.Unmarshal([]byte(payload), &ev)
		if cur, ok := s.Items[ev.StudentId]; ok {
			cur.EnrolledClassRoomIds = removeString(cur.EnrolledClassRoomIds, ev.ClassRoomId)
			s.Items[ev.StudentId] = cur
		}
	}
}

func applyClassRoomList(s *domain.ClassRoomListState, eventType, payload string) {
	switch eventType {
	case domain.EventClassRoomCreated:
		var ev domain.ClassRoomCreated
		json.Unmarshal([]byte(payload), &ev)
		s.Items[ev.ClassRoomId] = domain.ClassRoomItem{
			ClassRoomId:       ev.ClassRoomId,
			Name:              ev.Name,
			MaxStudents:       ev.MaxStudents,
			EnrolledCount:     0,
			IsFull:            false,
			RemainingCapacity: ev.MaxStudents,
		}
	case domain.EventStudentEnrolledInClassRoom:
		var ev domain.StudentEnrolledInClassRoom
		json.Unmarshal([]byte(payload), &ev)
		if cur, ok := s.Items[ev.ClassRoomId]; ok {
			enrolled := cur.EnrolledCount + 1
			remaining := cur.MaxStudents - enrolled
			if remaining < 0 {
				remaining = 0
			}
			cur.EnrolledCount = enrolled
			cur.IsFull = cur.MaxStudents > 0 && enrolled >= cur.MaxStudents
			cur.RemainingCapacity = remaining
			s.Items[ev.ClassRoomId] = cur
		}
	case domain.EventStudentDroppedFromClassRoom:
		var ev domain.StudentDroppedFromClassRoom
		json.Unmarshal([]byte(payload), &ev)
		if cur, ok := s.Items[ev.ClassRoomId]; ok {
			enrolled := cur.EnrolledCount - 1
			if enrolled < 0 {
				enrolled = 0
			}
			remaining := cur.MaxStudents - enrolled
			if remaining < 0 {
				remaining = 0
			}
			cur.EnrolledCount = enrolled
			cur.IsFull = cur.MaxStudents > 0 && enrolled >= cur.MaxStudents
			cur.RemainingCapacity = remaining
			s.Items[ev.ClassRoomId] = cur
		}
	}
}

func applyUserDirectoryList(s *domain.UserDirectoryListState, eventType, payload string) {
	switch eventType {
	case domain.EventUserRegistered:
		var ev domain.UserRegistered
		json.Unmarshal([]byte(payload), &ev)
		s.Items[ev.UserId] = domain.UserDirectoryListItem{
			UserId:                  ev.UserId,
			DisplayName:             ev.DisplayName,
			Email:                   ev.Email,
			Department:              ev.Department,
			RegisteredAt:            ev.RegisteredAt,
			MonthlyReservationLimit: ev.MonthlyReservationLimit,
			Roles:                   []string{},
			IsActive:                true,
		}
	case domain.EventUserProfileUpdated:
		var ev domain.UserProfileUpdated
		json.Unmarshal([]byte(payload), &ev)
		if cur, ok := s.Items[ev.UserId]; ok {
			cur.DisplayName = ev.DisplayName
			cur.Email = ev.Email
			cur.Department = ev.Department
			cur.MonthlyReservationLimit = ev.MonthlyReservationLimit
			s.Items[ev.UserId] = cur
		}
	}
}

func applyUserAccessList(s *domain.UserAccessListState, eventType, payload string) {
	switch eventType {
	case domain.EventUserAccessGranted:
		var ev domain.UserAccessGranted
		json.Unmarshal([]byte(payload), &ev)
		s.Items[ev.UserId] = domain.UserAccessListItem{
			UserId:    ev.UserId,
			Roles:     []string{ev.InitialRole},
			GrantedAt: ev.GrantedAt,
			IsActive:  true,
		}
	case domain.EventUserRoleGranted:
		var ev domain.UserRoleGranted
		json.Unmarshal([]byte(payload), &ev)
		if cur, ok := s.Items[ev.UserId]; ok {
			if !containsString(cur.Roles, ev.Role) {
				cur.Roles = append(cur.Roles, ev.Role)
			}
			s.Items[ev.UserId] = cur
		}
	}
}

func applyRoomList(s *domain.RoomListState, eventType, payload string) {
	switch eventType {
	case domain.EventRoomCreated:
		var ev domain.RoomCreated
		json.Unmarshal([]byte(payload), &ev)
		s.Items[ev.RoomId] = domain.RoomListItem{
			RoomId:           ev.RoomId,
			Name:             ev.Name,
			Capacity:         ev.Capacity,
			Location:         ev.Location,
			Equipment:        ev.Equipment,
			RequiresApproval: ev.RequiresApproval,
			IsActive:         true,
		}
	case domain.EventRoomUpdated:
		var ev domain.RoomUpdated
		json.Unmarshal([]byte(payload), &ev)
		if cur, ok := s.Items[ev.RoomId]; ok {
			cur.Name = ev.Name
			cur.Capacity = ev.Capacity
			cur.Location = ev.Location
			cur.Equipment = ev.Equipment
			cur.RequiresApproval = ev.RequiresApproval
			s.Items[ev.RoomId] = cur
		}
	}
}

func applyReservationList(s *domain.ReservationListState, eventType, payload string) {
	switch eventType {
	case domain.EventReservationDraftCreated:
		var ev domain.ReservationDraftCreated
		json.Unmarshal([]byte(payload), &ev)
		s.Items[ev.ReservationId] = domain.ReservationListItem{
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
	case domain.EventReservationHoldCommitted:
		var ev domain.ReservationHoldCommitted
		json.Unmarshal([]byte(payload), &ev)
		if cur, ok := s.Items[ev.ReservationId]; ok {
			cur.Status = "Held"
			s.Items[ev.ReservationId] = cur
		}
	case domain.EventReservationConfirmed:
		var ev domain.ReservationConfirmed
		json.Unmarshal([]byte(payload), &ev)
		if cur, ok := s.Items[ev.ReservationId]; ok {
			cur.Status = "Confirmed"
			s.Items[ev.ReservationId] = cur
		}
	case domain.EventReservationCancelled:
		var ev domain.ReservationCancelled
		json.Unmarshal([]byte(payload), &ev)
		if cur, ok := s.Items[ev.ReservationId]; ok {
			cur.Status = "Cancelled"
			s.Items[ev.ReservationId] = cur
		}
	case domain.EventReservationRejected:
		var ev domain.ReservationRejected
		json.Unmarshal([]byte(payload), &ev)
		if cur, ok := s.Items[ev.ReservationId]; ok {
			cur.Status = "Rejected"
			s.Items[ev.ReservationId] = cur
		}
	}
}

func applyApprovalInboxList(s *domain.ApprovalRequestListState, eventType, payload string) {
	switch eventType {
	case domain.EventApprovalFlowStarted:
		var ev domain.ApprovalFlowStarted
		json.Unmarshal([]byte(payload), &ev)
		s.Items[ev.ApprovalRequestId] = domain.ApprovalInboxItem{
			ApprovalRequestId: ev.ApprovalRequestId,
			ReservationId:     ev.ReservationId,
			RoomId:            ev.RoomId,
			RequesterId:       ev.RequesterId,
			ApproverIds:       ev.ApproverIds,
			RequestedAt:       ev.RequestedAt,
			RequestComment:    ev.RequestComment,
			Status:            "Pending",
		}
	case domain.EventApprovalDecisionRecorded:
		var ev domain.ApprovalDecisionRecorded
		json.Unmarshal([]byte(payload), &ev)
		if cur, ok := s.Items[ev.ApprovalRequestId]; ok {
			if ev.Decision == "Rejected" {
				cur.Status = "Rejected"
			} else {
				cur.Status = "Approved"
			}
			cur.RequestedAt = ev.DecidedAt
			s.Items[ev.ApprovalRequestId] = cur
		}
	}
}

// ---------------------------------------------------------------------------
// String helpers
// ---------------------------------------------------------------------------

func containsString(list []string, value string) bool {
	for _, v := range list {
		if v == value {
			return true
		}
	}
	return false
}

func removeString(list []string, value string) []string {
	result := make([]string, 0, len(list))
	for _, v := range list {
		if v != value {
			result = append(result, v)
		}
	}
	return result
}

// ---------------------------------------------------------------------------
// Command types (request structs)
// ---------------------------------------------------------------------------

type wasmResult struct {
	Ok    *wasmCommandOutput `json:"ok,omitempty"`
	Error *wasmError         `json:"error,omitempty"`
}

type wasmCommandOutput struct {
	Events []wasmEventPayload `json:"events"`
	Tags   []string           `json:"tags"`
	Noop   bool               `json:"noop,omitempty"`
}

type wasmEventPayload struct {
	EventType string `json:"eventType"`
	Payload   string `json:"payload"`
}

type wasmError struct {
	Status  int    `json:"status"`
	Code    string `json:"code"`
	Message string `json:"message"`
}

func wasmOk(eventType, payloadJSON string, tags []string) string {
	r := wasmResult{
		Ok: &wasmCommandOutput{
			Events: []wasmEventPayload{{EventType: eventType, Payload: payloadJSON}},
			Tags:   tags,
		},
	}
	return wasm.MustJSON(r)
}

func wasmOkNoop() string {
	r := wasmResult{
		Ok: &wasmCommandOutput{
			Events: []wasmEventPayload{},
			Tags:   []string{},
			Noop:   true,
		},
	}
	return wasm.MustJSON(r)
}

func wasmErr(status int, code, message string) string {
	r := wasmResult{
		Error: &wasmError{Status: status, Code: code, Message: message},
	}
	return wasm.MustJSON(r)
}

func tagString(group, id string) string {
	return group + ":" + id
}

// ---------------------------------------------------------------------------
// Command request structs
// ---------------------------------------------------------------------------

type createWeatherReq struct {
	ForecastId   string  `json:"forecastId"`
	Location     string  `json:"location"`
	Date         string  `json:"date"`
	TemperatureC int     `json:"temperatureC"`
	Summary      *string `json:"summary"`
	NowIso       string  `json:"nowIso"`
}

type updateLocationReq struct {
	ForecastId  string `json:"forecastId"`
	NewLocation string `json:"newLocation"`
	NowIso      string `json:"nowIso"`
}

type deleteWeatherReq struct {
	ForecastId string `json:"forecastId"`
	NowIso     string `json:"nowIso"`
}

type createStudentReq struct {
	StudentId     string `json:"studentId"`
	Name          string `json:"name"`
	MaxClassCount int    `json:"maxClassCount"`
}

type createClassRoomReq struct {
	ClassRoomId string `json:"classRoomId"`
	Name        string `json:"name"`
	MaxStudents int    `json:"maxStudents"`
}

type enrollmentReq struct {
	StudentId   string `json:"studentId"`
	ClassRoomId string `json:"classRoomId"`
}

type registerUserReq struct {
	UserId                  string  `json:"userId"`
	DisplayName             string  `json:"displayName"`
	Email                   string  `json:"email"`
	Department              *string `json:"department"`
	MonthlyReservationLimit int     `json:"monthlyReservationLimit"`
	NowIso                  string  `json:"nowIso"`
}

type updateUserMonthlyLimitReq struct {
	UserId                  string `json:"userId"`
	MonthlyReservationLimit int    `json:"monthlyReservationLimit"`
}

type grantUserAccessReq struct {
	UserId      string `json:"userId"`
	InitialRole string `json:"initialRole"`
	NowIso      string `json:"nowIso"`
}

type grantUserRoleReq struct {
	UserId string `json:"userId"`
	Role   string `json:"role"`
	NowIso string `json:"nowIso"`
}

type createRoomReq struct {
	RoomId           string   `json:"roomId"`
	Name             string   `json:"name"`
	Capacity         int      `json:"capacity"`
	Location         string   `json:"location"`
	Equipment        []string `json:"equipment"`
	RequiresApproval bool     `json:"requiresApproval"`
}

type updateRoomReq struct {
	RoomId           string   `json:"roomId"`
	Name             string   `json:"name"`
	Capacity         int      `json:"capacity"`
	Location         string   `json:"location"`
	Equipment        []string `json:"equipment"`
	RequiresApproval bool     `json:"requiresApproval"`
}

type createReservationDraftReq struct {
	ReservationId     string   `json:"reservationId"`
	RoomId            string   `json:"roomId"`
	OrganizerId       string   `json:"organizerId"`
	OrganizerName     string   `json:"organizerName"`
	StartTime         string   `json:"startTime"`
	EndTime           string   `json:"endTime"`
	Purpose           string   `json:"purpose"`
	SelectedEquipment []string `json:"selectedEquipment"`
}

type commitReservationHoldReq struct {
	ReservationId          string  `json:"reservationId"`
	RoomId                 string  `json:"roomId"`
	RequiresApproval       bool    `json:"requiresApproval"`
	ApprovalRequestId      *string `json:"approvalRequestId"`
	ApprovalRequestComment *string `json:"approvalRequestComment"`
}

type confirmReservationReq struct {
	ReservationId string `json:"reservationId"`
	RoomId        string `json:"roomId"`
	NowIso        string `json:"nowIso"`
}

type cancelReservationReq struct {
	ReservationId string `json:"reservationId"`
	RoomId        string `json:"roomId"`
	Reason        string `json:"reason"`
	NowIso        string `json:"nowIso"`
}

type rejectReservationReq struct {
	ReservationId     string `json:"reservationId"`
	RoomId            string `json:"roomId"`
	ApprovalRequestId string `json:"approvalRequestId"`
	Reason            string `json:"reason"`
	NowIso            string `json:"nowIso"`
}

type startApprovalFlowReq struct {
	ApprovalRequestId string   `json:"approvalRequestId"`
	ReservationId     string   `json:"reservationId"`
	RoomId            string   `json:"roomId"`
	RequesterId       string   `json:"requesterId"`
	ApproverIds       []string `json:"approverIds"`
	RequestComment    *string  `json:"requestComment"`
	NowIso            string   `json:"nowIso"`
}

type recordApprovalDecisionReq struct {
	ApprovalRequestId string  `json:"approvalRequestId"`
	ReservationId     string  `json:"reservationId"`
	ApproverId        string  `json:"approverId"`
	Decision          string  `json:"decision"`
	Comment           *string `json:"comment"`
	NowIso            string  `json:"nowIso"`
}

// ---------------------------------------------------------------------------
// Command handler helpers
// ---------------------------------------------------------------------------

func weatherSummary(s *string) string {
	if s == nil {
		return ""
	}
	return *s
}

func handleCreateWeather(stateJSON string, _ int, reqJSON string) string {
	var state domain.WeatherForecastState
	json.Unmarshal([]byte(stateJSON), &state)
	if state.ForecastId != "" {
		return wasmErr(409, "AlreadyExists", "forecast already exists")
	}
	var req createWeatherReq
	json.Unmarshal([]byte(reqJSON), &req)
	if req.ForecastId == "" {
		return wasmErr(400, "Validation", "forecastId is required")
	}
	ev := domain.WeatherForecastCreated{
		ForecastId:   req.ForecastId,
		Location:     req.Location,
		Date:         req.Date,
		TemperatureC: req.TemperatureC,
		Summary:      weatherSummary(req.Summary),
		CreatedAt:    req.NowIso,
	}
	return wasmOk(domain.EventWeatherForecastCreated, wasm.MustJSON(ev),
		[]string{tagString(domain.TagGroupWeather, req.ForecastId)})
}

func handleUpdateWeatherLocation(stateJSON string, _ int, reqJSON string) string {
	var state domain.WeatherForecastState
	json.Unmarshal([]byte(stateJSON), &state)
	if state.ForecastId == "" {
		return wasmErr(404, "NotFound", "forecast not found")
	}
	if state.IsDeleted {
		return wasmErr(404, "Deleted", "forecast already deleted")
	}
	var req updateLocationReq
	json.Unmarshal([]byte(reqJSON), &req)
	if req.ForecastId == "" {
		return wasmErr(400, "Validation", "forecastId is required")
	}
	if state.Location == req.NewLocation {
		return wasmOkNoop()
	}
	ev := domain.WeatherForecastLocationUpdated{
		ForecastId:  req.ForecastId,
		NewLocation: req.NewLocation,
		UpdatedAt:   req.NowIso,
	}
	return wasmOk(domain.EventWeatherForecastLocationUpdated, wasm.MustJSON(ev),
		[]string{tagString(domain.TagGroupWeather, req.ForecastId)})
}

func handleDeleteWeather(stateJSON string, _ int, reqJSON string) string {
	var state domain.WeatherForecastState
	json.Unmarshal([]byte(stateJSON), &state)
	if state.ForecastId == "" {
		return wasmErr(404, "NotFound", "forecast not found")
	}
	if state.IsDeleted {
		return wasmOkNoop()
	}
	var req deleteWeatherReq
	json.Unmarshal([]byte(reqJSON), &req)
	if req.ForecastId == "" {
		return wasmErr(400, "Validation", "forecastId is required")
	}
	ev := domain.WeatherForecastDeleted{ForecastId: req.ForecastId, DeletedAt: req.NowIso}
	return wasmOk(domain.EventWeatherForecastDeleted, wasm.MustJSON(ev),
		[]string{tagString(domain.TagGroupWeather, req.ForecastId)})
}

func handleCreateStudent(stateJSON string, _ int, reqJSON string) string {
	var state domain.StudentState
	json.Unmarshal([]byte(stateJSON), &state)
	if state.StudentId != "" {
		return wasmErr(409, "AlreadyExists", "student already exists")
	}
	var req createStudentReq
	json.Unmarshal([]byte(reqJSON), &req)
	if req.StudentId == "" {
		return wasmErr(400, "Validation", "studentId is required")
	}
	maxCC := req.MaxClassCount
	if maxCC <= 0 {
		maxCC = 1
	}
	ev := domain.StudentCreated{StudentId: req.StudentId, Name: req.Name, MaxClassCount: maxCC}
	return wasmOk(domain.EventStudentCreated, wasm.MustJSON(ev),
		[]string{tagString(domain.TagGroupStudent, req.StudentId)})
}

func handleCreateClassRoom(stateJSON string, _ int, reqJSON string) string {
	var state domain.ClassRoomState
	json.Unmarshal([]byte(stateJSON), &state)
	if state.ClassRoomId != "" {
		return wasmErr(409, "AlreadyExists", "classroom already exists")
	}
	var req createClassRoomReq
	json.Unmarshal([]byte(reqJSON), &req)
	if req.ClassRoomId == "" {
		return wasmErr(400, "Validation", "classRoomId is required")
	}
	maxS := req.MaxStudents
	if maxS <= 0 {
		maxS = 1
	}
	ev := domain.ClassRoomCreated{ClassRoomId: req.ClassRoomId, Name: req.Name, MaxStudents: maxS}
	return wasmOk(domain.EventClassRoomCreated, wasm.MustJSON(ev),
		[]string{tagString(domain.TagGroupClassRoom, req.ClassRoomId)})
}

func handleEnroll(studentStateJSON string, _ int, classStateJSON string, _ int, reqJSON string) string {
	var student domain.StudentState
	json.Unmarshal([]byte(studentStateJSON), &student)
	var class domain.ClassRoomState
	json.Unmarshal([]byte(classStateJSON), &class)
	var req enrollmentReq
	json.Unmarshal([]byte(reqJSON), &req)

	if req.StudentId == "" || req.ClassRoomId == "" {
		return wasmErr(400, "Validation", "studentId and classRoomId are required")
	}
	if student.StudentId == "" {
		return wasmErr(404, "NotFound", "student not found")
	}
	if class.ClassRoomId == "" {
		return wasmErr(404, "NotFound", "classroom not found")
	}
	if containsString(student.EnrolledClassRoomIds, req.ClassRoomId) || containsString(class.EnrolledStudentIds, req.StudentId) {
		return wasmOkNoop()
	}
	if student.MaxClassCount > 0 && len(student.EnrolledClassRoomIds) >= student.MaxClassCount {
		return wasmErr(400, "Validation", "Student reached max class count")
	}
	if class.MaxStudents > 0 && len(class.EnrolledStudentIds) >= class.MaxStudents {
		return wasmErr(400, "Validation", "Class room is full")
	}
	ev := domain.StudentEnrolledInClassRoom{StudentId: req.StudentId, ClassRoomId: req.ClassRoomId}
	return wasmOk(domain.EventStudentEnrolledInClassRoom, wasm.MustJSON(ev),
		[]string{tagString(domain.TagGroupStudent, req.StudentId), tagString(domain.TagGroupClassRoom, req.ClassRoomId)})
}

func handleDrop(studentStateJSON string, _ int, classStateJSON string, _ int, reqJSON string) string {
	var student domain.StudentState
	json.Unmarshal([]byte(studentStateJSON), &student)
	var class domain.ClassRoomState
	json.Unmarshal([]byte(classStateJSON), &class)
	var req enrollmentReq
	json.Unmarshal([]byte(reqJSON), &req)

	if req.StudentId == "" || req.ClassRoomId == "" {
		return wasmErr(400, "Validation", "studentId and classRoomId are required")
	}
	if student.StudentId == "" || class.ClassRoomId == "" {
		return wasmErr(404, "NotFound", "Enrollment not found")
	}
	if !containsString(student.EnrolledClassRoomIds, req.ClassRoomId) {
		return wasmOkNoop()
	}
	ev := domain.StudentDroppedFromClassRoom{StudentId: req.StudentId, ClassRoomId: req.ClassRoomId}
	return wasmOk(domain.EventStudentDroppedFromClassRoom, wasm.MustJSON(ev),
		[]string{tagString(domain.TagGroupStudent, req.StudentId), tagString(domain.TagGroupClassRoom, req.ClassRoomId)})
}

func handleRegisterUser(stateJSON string, _ int, reqJSON string) string {
	var state domain.UserDirectoryState
	json.Unmarshal([]byte(stateJSON), &state)
	if state.UserId != "" {
		return wasmErr(409, "AlreadyExists", "user already exists")
	}
	var req registerUserReq
	json.Unmarshal([]byte(reqJSON), &req)
	if req.UserId == "" {
		return wasmErr(400, "Validation", "userId is required")
	}
	limit := req.MonthlyReservationLimit
	if limit <= 0 {
		limit = 1
	}
	ev := domain.UserRegistered{
		UserId:                  req.UserId,
		DisplayName:             req.DisplayName,
		Email:                   req.Email,
		Department:              req.Department,
		RegisteredAt:            req.NowIso,
		MonthlyReservationLimit: limit,
	}
	return wasmOk(domain.EventUserRegistered, wasm.MustJSON(ev),
		[]string{tagString(domain.TagGroupUser, req.UserId)})
}

func handleUpdateUserMonthlyLimit(stateJSON string, _ int, reqJSON string) string {
	var state domain.UserDirectoryState
	json.Unmarshal([]byte(stateJSON), &state)
	if state.UserId == "" || !state.IsActive {
		return wasmErr(404, "NotFound", "user not found")
	}
	var req updateUserMonthlyLimitReq
	json.Unmarshal([]byte(reqJSON), &req)
	if req.UserId == "" {
		return wasmErr(400, "Validation", "userId is required")
	}
	limit := req.MonthlyReservationLimit
	if limit <= 0 {
		limit = 1
	}
	ev := domain.UserProfileUpdated{
		UserId:                  req.UserId,
		DisplayName:             state.DisplayName,
		Email:                   state.Email,
		Department:              state.Department,
		MonthlyReservationLimit: limit,
	}
	return wasmOk(domain.EventUserProfileUpdated, wasm.MustJSON(ev),
		[]string{tagString(domain.TagGroupUser, req.UserId)})
}

func handleGrantUserAccess(stateJSON string, _ int, reqJSON string) string {
	var state domain.UserAccessState
	json.Unmarshal([]byte(stateJSON), &state)
	if state.UserId != "" {
		return wasmErr(409, "AlreadyExists", "user access already exists")
	}
	var req grantUserAccessReq
	json.Unmarshal([]byte(reqJSON), &req)
	if req.UserId == "" {
		return wasmErr(400, "Validation", "userId is required")
	}
	ev := domain.UserAccessGranted{UserId: req.UserId, InitialRole: req.InitialRole, GrantedAt: req.NowIso}
	return wasmOk(domain.EventUserAccessGranted, wasm.MustJSON(ev),
		[]string{tagString(domain.TagGroupUserAccess, req.UserId)})
}

func handleGrantUserRole(stateJSON string, _ int, reqJSON string) string {
	var state domain.UserAccessState
	json.Unmarshal([]byte(stateJSON), &state)
	if state.UserId == "" || !state.IsActive {
		return wasmErr(404, "NotFound", "user access not found")
	}
	var req grantUserRoleReq
	json.Unmarshal([]byte(reqJSON), &req)
	if req.UserId == "" {
		return wasmErr(400, "Validation", "userId is required")
	}
	if containsString(state.Roles, req.Role) {
		return wasmOkNoop()
	}
	ev := domain.UserRoleGranted{UserId: req.UserId, Role: req.Role, GrantedAt: req.NowIso}
	return wasmOk(domain.EventUserRoleGranted, wasm.MustJSON(ev),
		[]string{tagString(domain.TagGroupUserAccess, req.UserId)})
}

func handleCreateRoom(stateJSON string, _ int, reqJSON string) string {
	var state domain.RoomState
	json.Unmarshal([]byte(stateJSON), &state)
	if state.RoomId != "" {
		return wasmErr(409, "AlreadyExists", "room already exists")
	}
	var req createRoomReq
	json.Unmarshal([]byte(reqJSON), &req)
	if req.RoomId == "" {
		return wasmErr(400, "Validation", "roomId is required")
	}
	ev := domain.RoomCreated{
		RoomId: req.RoomId, Name: req.Name, Capacity: req.Capacity,
		Location: req.Location, Equipment: req.Equipment, RequiresApproval: req.RequiresApproval,
	}
	return wasmOk(domain.EventRoomCreated, wasm.MustJSON(ev),
		[]string{tagString(domain.TagGroupRoom, req.RoomId)})
}

func handleUpdateRoom(stateJSON string, _ int, reqJSON string) string {
	var state domain.RoomState
	json.Unmarshal([]byte(stateJSON), &state)
	if state.RoomId == "" {
		return wasmErr(404, "NotFound", "room not found")
	}
	var req updateRoomReq
	json.Unmarshal([]byte(reqJSON), &req)
	if req.RoomId == "" {
		return wasmErr(400, "Validation", "roomId is required")
	}
	ev := domain.RoomUpdated{
		RoomId: req.RoomId, Name: req.Name, Capacity: req.Capacity,
		Location: req.Location, Equipment: req.Equipment, RequiresApproval: req.RequiresApproval,
	}
	return wasmOk(domain.EventRoomUpdated, wasm.MustJSON(ev),
		[]string{tagString(domain.TagGroupRoom, req.RoomId)})
}

func handleCreateReservationDraft(resStateJSON string, _ int, roomStateJSON string, _ int, reqJSON string) string {
	var reservation domain.ReservationState
	json.Unmarshal([]byte(resStateJSON), &reservation)
	if reservation.ReservationId != "" {
		return wasmErr(409, "AlreadyExists", "reservation already exists")
	}
	var room domain.RoomState
	json.Unmarshal([]byte(roomStateJSON), &room)
	if room.RoomId == "" {
		return wasmErr(404, "NotFound", "room not found")
	}
	var req createReservationDraftReq
	json.Unmarshal([]byte(reqJSON), &req)
	if req.ReservationId == "" || req.RoomId == "" {
		return wasmErr(400, "Validation", "reservationId and roomId are required")
	}
	ev := domain.ReservationDraftCreated{
		ReservationId: req.ReservationId, RoomId: req.RoomId,
		OrganizerId: req.OrganizerId, OrganizerName: req.OrganizerName,
		StartTime: req.StartTime, EndTime: req.EndTime,
		Purpose: req.Purpose, SelectedEquipment: req.SelectedEquipment,
	}
	return wasmOk(domain.EventReservationDraftCreated, wasm.MustJSON(ev),
		[]string{tagString(domain.TagGroupReservation, req.ReservationId)})
}

func handleCommitReservationHold(stateJSON string, _ int, reqJSON string) string {
	var state domain.ReservationState
	json.Unmarshal([]byte(stateJSON), &state)
	if state.ReservationId == "" {
		return wasmErr(404, "NotFound", "reservation not found")
	}
	if state.Status != "Draft" {
		return wasmOkNoop()
	}
	var req commitReservationHoldReq
	json.Unmarshal([]byte(reqJSON), &req)
	ev := domain.ReservationHoldCommitted{
		ReservationId: state.ReservationId, RoomId: req.RoomId,
		OrganizerId: state.OrganizerId, OrganizerName: state.OrganizerName,
		StartTime: state.StartTime, EndTime: state.EndTime,
		Purpose: state.Purpose, SelectedEquipment: state.SelectedEquipment,
		RequiresApproval:  req.RequiresApproval,
		ApprovalRequestId: req.ApprovalRequestId, ApprovalRequestComment: req.ApprovalRequestComment,
	}
	return wasmOk(domain.EventReservationHoldCommitted, wasm.MustJSON(ev),
		[]string{tagString(domain.TagGroupReservation, state.ReservationId)})
}

func handleConfirmReservation(stateJSON string, _ int, reqJSON string) string {
	var state domain.ReservationState
	json.Unmarshal([]byte(stateJSON), &state)
	if state.ReservationId == "" {
		return wasmErr(404, "NotFound", "reservation not found")
	}
	if state.Status == "Confirmed" {
		return wasmOkNoop()
	}
	var req confirmReservationReq
	json.Unmarshal([]byte(reqJSON), &req)
	ev := domain.ReservationConfirmed{
		ReservationId: state.ReservationId, RoomId: req.RoomId,
		OrganizerId: state.OrganizerId, OrganizerName: state.OrganizerName,
		StartTime: state.StartTime, EndTime: state.EndTime,
		Purpose: state.Purpose, SelectedEquipment: state.SelectedEquipment,
		ConfirmedAt:       req.NowIso,
		ApprovalRequestId: state.ApprovalRequestId, ApprovalRequestComment: state.ApprovalRequestComment,
		ApprovalDecisionComment: state.ApprovalDecisionComment,
	}
	return wasmOk(domain.EventReservationConfirmed, wasm.MustJSON(ev),
		[]string{tagString(domain.TagGroupReservation, state.ReservationId)})
}

func handleCancelReservation(stateJSON string, _ int, reqJSON string) string {
	var state domain.ReservationState
	json.Unmarshal([]byte(stateJSON), &state)
	if state.ReservationId == "" {
		return wasmErr(404, "NotFound", "reservation not found")
	}
	var req cancelReservationReq
	json.Unmarshal([]byte(reqJSON), &req)
	ev := domain.ReservationCancelled{
		ReservationId: state.ReservationId, RoomId: req.RoomId,
		OrganizerId: state.OrganizerId, OrganizerName: state.OrganizerName,
		StartTime: state.StartTime, EndTime: state.EndTime,
		Purpose: state.Purpose, SelectedEquipment: state.SelectedEquipment,
		ApprovalRequestComment: state.ApprovalRequestComment,
		Reason:                 req.Reason, CancelledAt: req.NowIso,
	}
	return wasmOk(domain.EventReservationCancelled, wasm.MustJSON(ev),
		[]string{tagString(domain.TagGroupReservation, state.ReservationId)})
}

func handleRejectReservation(stateJSON string, _ int, reqJSON string) string {
	var state domain.ReservationState
	json.Unmarshal([]byte(stateJSON), &state)
	if state.ReservationId == "" {
		return wasmErr(404, "NotFound", "reservation not found")
	}
	var req rejectReservationReq
	json.Unmarshal([]byte(reqJSON), &req)
	ev := domain.ReservationRejected{
		ReservationId: state.ReservationId, RoomId: req.RoomId,
		OrganizerId: state.OrganizerId, OrganizerName: state.OrganizerName,
		StartTime: state.StartTime, EndTime: state.EndTime,
		Purpose: state.Purpose, SelectedEquipment: state.SelectedEquipment,
		ApprovalRequestId:      req.ApprovalRequestId,
		ApprovalRequestComment: state.ApprovalRequestComment,
		Reason:                 req.Reason, RejectedAt: req.NowIso,
	}
	return wasmOk(domain.EventReservationRejected, wasm.MustJSON(ev),
		[]string{tagString(domain.TagGroupReservation, state.ReservationId)})
}

func handleStartApprovalFlow(stateJSON string, _ int, reqJSON string) string {
	var state domain.ApprovalRequestState
	json.Unmarshal([]byte(stateJSON), &state)
	if state.ApprovalRequestId != "" {
		return wasmErr(409, "AlreadyExists", "approval request already exists")
	}
	var req startApprovalFlowReq
	json.Unmarshal([]byte(reqJSON), &req)
	ev := domain.ApprovalFlowStarted{
		ApprovalRequestId: req.ApprovalRequestId, ReservationId: req.ReservationId,
		RoomId: req.RoomId, RequesterId: req.RequesterId,
		ApproverIds: req.ApproverIds, RequestedAt: req.NowIso,
		RequestComment: req.RequestComment,
	}
	return wasmOk(domain.EventApprovalFlowStarted, wasm.MustJSON(ev),
		[]string{tagString(domain.TagGroupApprovalRequest, req.ApprovalRequestId)})
}

func handleRecordApprovalDecision(stateJSON string, _ int, reqJSON string) string {
	var state domain.ApprovalRequestState
	json.Unmarshal([]byte(stateJSON), &state)
	if state.ApprovalRequestId == "" {
		return wasmErr(404, "NotFound", "approval request not found")
	}
	if state.Status != "Pending" {
		return wasmOkNoop()
	}
	var req recordApprovalDecisionReq
	json.Unmarshal([]byte(reqJSON), &req)
	ev := domain.ApprovalDecisionRecorded{
		ApprovalRequestId: req.ApprovalRequestId, ReservationId: req.ReservationId,
		ApproverId: req.ApproverId, Decision: req.Decision,
		Comment: req.Comment, DecidedAt: req.NowIso,
	}
	return wasmOk(domain.EventApprovalDecisionRecorded, wasm.MustJSON(ev),
		[]string{tagString(domain.TagGroupApprovalRequest, req.ApprovalRequestId)})
}

// ---------------------------------------------------------------------------
// Unary command exports
// ---------------------------------------------------------------------------

//export cmd_create_weather
func cmd_create_weather(statePtr, stateLen uint32, version int32, reqPtr, reqLen uint32) int64 {
	stateJSON := wasm.ReadString(statePtr, stateLen)
	reqJSON := wasm.ReadString(reqPtr, reqLen)
	return wasm.WriteString(handleCreateWeather(stateJSON, int(version), reqJSON))
}

//export cmd_update_weather_location
func cmd_update_weather_location(statePtr, stateLen uint32, version int32, reqPtr, reqLen uint32) int64 {
	stateJSON := wasm.ReadString(statePtr, stateLen)
	reqJSON := wasm.ReadString(reqPtr, reqLen)
	return wasm.WriteString(handleUpdateWeatherLocation(stateJSON, int(version), reqJSON))
}

//export cmd_delete_weather
func cmd_delete_weather(statePtr, stateLen uint32, version int32, reqPtr, reqLen uint32) int64 {
	stateJSON := wasm.ReadString(statePtr, stateLen)
	reqJSON := wasm.ReadString(reqPtr, reqLen)
	return wasm.WriteString(handleDeleteWeather(stateJSON, int(version), reqJSON))
}

//export cmd_create_student
func cmd_create_student(statePtr, stateLen uint32, version int32, reqPtr, reqLen uint32) int64 {
	stateJSON := wasm.ReadString(statePtr, stateLen)
	reqJSON := wasm.ReadString(reqPtr, reqLen)
	return wasm.WriteString(handleCreateStudent(stateJSON, int(version), reqJSON))
}

//export cmd_create_classroom
func cmd_create_classroom(statePtr, stateLen uint32, version int32, reqPtr, reqLen uint32) int64 {
	stateJSON := wasm.ReadString(statePtr, stateLen)
	reqJSON := wasm.ReadString(reqPtr, reqLen)
	return wasm.WriteString(handleCreateClassRoom(stateJSON, int(version), reqJSON))
}

//export cmd_register_user
func cmd_register_user(statePtr, stateLen uint32, version int32, reqPtr, reqLen uint32) int64 {
	stateJSON := wasm.ReadString(statePtr, stateLen)
	reqJSON := wasm.ReadString(reqPtr, reqLen)
	return wasm.WriteString(handleRegisterUser(stateJSON, int(version), reqJSON))
}

//export cmd_update_user_monthly_limit
func cmd_update_user_monthly_limit(statePtr, stateLen uint32, version int32, reqPtr, reqLen uint32) int64 {
	stateJSON := wasm.ReadString(statePtr, stateLen)
	reqJSON := wasm.ReadString(reqPtr, reqLen)
	return wasm.WriteString(handleUpdateUserMonthlyLimit(stateJSON, int(version), reqJSON))
}

//export cmd_grant_user_access
func cmd_grant_user_access(statePtr, stateLen uint32, version int32, reqPtr, reqLen uint32) int64 {
	stateJSON := wasm.ReadString(statePtr, stateLen)
	reqJSON := wasm.ReadString(reqPtr, reqLen)
	return wasm.WriteString(handleGrantUserAccess(stateJSON, int(version), reqJSON))
}

//export cmd_grant_user_role
func cmd_grant_user_role(statePtr, stateLen uint32, version int32, reqPtr, reqLen uint32) int64 {
	stateJSON := wasm.ReadString(statePtr, stateLen)
	reqJSON := wasm.ReadString(reqPtr, reqLen)
	return wasm.WriteString(handleGrantUserRole(stateJSON, int(version), reqJSON))
}

//export cmd_create_room
func cmd_create_room(statePtr, stateLen uint32, version int32, reqPtr, reqLen uint32) int64 {
	stateJSON := wasm.ReadString(statePtr, stateLen)
	reqJSON := wasm.ReadString(reqPtr, reqLen)
	return wasm.WriteString(handleCreateRoom(stateJSON, int(version), reqJSON))
}

//export cmd_update_room
func cmd_update_room(statePtr, stateLen uint32, version int32, reqPtr, reqLen uint32) int64 {
	stateJSON := wasm.ReadString(statePtr, stateLen)
	reqJSON := wasm.ReadString(reqPtr, reqLen)
	return wasm.WriteString(handleUpdateRoom(stateJSON, int(version), reqJSON))
}

//export cmd_commit_reservation_hold
func cmd_commit_reservation_hold(statePtr, stateLen uint32, version int32, reqPtr, reqLen uint32) int64 {
	stateJSON := wasm.ReadString(statePtr, stateLen)
	reqJSON := wasm.ReadString(reqPtr, reqLen)
	return wasm.WriteString(handleCommitReservationHold(stateJSON, int(version), reqJSON))
}

//export cmd_confirm_reservation
func cmd_confirm_reservation(statePtr, stateLen uint32, version int32, reqPtr, reqLen uint32) int64 {
	stateJSON := wasm.ReadString(statePtr, stateLen)
	reqJSON := wasm.ReadString(reqPtr, reqLen)
	return wasm.WriteString(handleConfirmReservation(stateJSON, int(version), reqJSON))
}

//export cmd_cancel_reservation
func cmd_cancel_reservation(statePtr, stateLen uint32, version int32, reqPtr, reqLen uint32) int64 {
	stateJSON := wasm.ReadString(statePtr, stateLen)
	reqJSON := wasm.ReadString(reqPtr, reqLen)
	return wasm.WriteString(handleCancelReservation(stateJSON, int(version), reqJSON))
}

//export cmd_reject_reservation
func cmd_reject_reservation(statePtr, stateLen uint32, version int32, reqPtr, reqLen uint32) int64 {
	stateJSON := wasm.ReadString(statePtr, stateLen)
	reqJSON := wasm.ReadString(reqPtr, reqLen)
	return wasm.WriteString(handleRejectReservation(stateJSON, int(version), reqJSON))
}

//export cmd_start_approval_flow
func cmd_start_approval_flow(statePtr, stateLen uint32, version int32, reqPtr, reqLen uint32) int64 {
	stateJSON := wasm.ReadString(statePtr, stateLen)
	reqJSON := wasm.ReadString(reqPtr, reqLen)
	return wasm.WriteString(handleStartApprovalFlow(stateJSON, int(version), reqJSON))
}

//export cmd_record_approval_decision
func cmd_record_approval_decision(statePtr, stateLen uint32, version int32, reqPtr, reqLen uint32) int64 {
	stateJSON := wasm.ReadString(statePtr, stateLen)
	reqJSON := wasm.ReadString(reqPtr, reqLen)
	return wasm.WriteString(handleRecordApprovalDecision(stateJSON, int(version), reqJSON))
}

// ---------------------------------------------------------------------------
// Binary command exports (two state+version pairs)
// ---------------------------------------------------------------------------

//export cmd_enroll
func cmd_enroll(firstStatePtr, firstStateLen uint32, firstVersion int32,
	secondStatePtr, secondStateLen uint32, secondVersion int32,
	reqPtr, reqLen uint32) int64 {
	studentJSON := wasm.ReadString(firstStatePtr, firstStateLen)
	classJSON := wasm.ReadString(secondStatePtr, secondStateLen)
	reqJSON := wasm.ReadString(reqPtr, reqLen)
	return wasm.WriteString(handleEnroll(studentJSON, int(firstVersion), classJSON, int(secondVersion), reqJSON))
}

//export cmd_drop
func cmd_drop(firstStatePtr, firstStateLen uint32, firstVersion int32,
	secondStatePtr, secondStateLen uint32, secondVersion int32,
	reqPtr, reqLen uint32) int64 {
	studentJSON := wasm.ReadString(firstStatePtr, firstStateLen)
	classJSON := wasm.ReadString(secondStatePtr, secondStateLen)
	reqJSON := wasm.ReadString(reqPtr, reqLen)
	return wasm.WriteString(handleDrop(studentJSON, int(firstVersion), classJSON, int(secondVersion), reqJSON))
}

//export cmd_create_reservation_draft
func cmd_create_reservation_draft(firstStatePtr, firstStateLen uint32, firstVersion int32,
	secondStatePtr, secondStateLen uint32, secondVersion int32,
	reqPtr, reqLen uint32) int64 {
	resJSON := wasm.ReadString(firstStatePtr, firstStateLen)
	roomJSON := wasm.ReadString(secondStatePtr, secondStateLen)
	reqJSON := wasm.ReadString(reqPtr, reqLen)
	return wasm.WriteString(handleCreateReservationDraft(resJSON, int(firstVersion), roomJSON, int(secondVersion), reqJSON))
}

// ---------------------------------------------------------------------------
// Required for TinyGo WASI
// ---------------------------------------------------------------------------

func main() {}
