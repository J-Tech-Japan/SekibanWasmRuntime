package main

import (
	"encoding/json"
	"errors"
	"fmt"
	"log"
	"math/rand"
	"net/http"
	"os"
	"strconv"
	"time"

	"github.com/go-chi/chi/v5"
	"github.com/go-chi/chi/v5/middleware"
	"github.com/google/uuid"

	"sekiban-dcb-decider-go/domain"

	"github.com/J-Tech-Japan/sekiban-go/client"
	sekiban "github.com/J-Tech-Japan/sekiban-go/domain"
)

// ---------------------------------------------------------------------------
// Application state
// ---------------------------------------------------------------------------

type appState struct {
	runtime *client.SekibanRuntimeClient
}

// ---------------------------------------------------------------------------
// Main
// ---------------------------------------------------------------------------

func main() {
	port := resolvePort()
	wasmServerURL := resolveWasmServerURL()

	log.Printf("WasmServer URL: %s", wasmServerURL)
	log.Printf("Starting Go ClientAPI on port %d", port)

	state := &appState{
		runtime: client.NewSekibanRuntimeClient(wasmServerURL, domain.TagProjectorMap),
	}

	r := chi.NewRouter()
	r.Use(middleware.Logger)
	r.Use(middleware.Recoverer)

	// Health
	r.Get("/health", state.handleHealth)

	// Weather
	r.Get("/api/weatherforecast", state.handleGetWeatherForecasts)
	r.Get("/api/weatherforecast/count", state.handleGetWeatherForecastCount)
	r.Post("/api/weatherforecast", state.handleCreateWeatherForecast)
	r.Post("/api/weatherforecast/update-location", state.handleUpdateWeatherLocation)
	r.Post("/api/weatherforecast/delete", state.handleDeleteWeatherForecast)

	// Students
	r.Get("/api/students", state.handleGetStudents)
	r.Post("/api/students", state.handleCreateStudent)

	// ClassRooms
	r.Get("/api/classrooms", state.handleGetClassRooms)
	r.Post("/api/classrooms", state.handleCreateClassRoom)

	// Enrollments
	r.Post("/api/enrollments/add", state.handleEnroll)
	r.Post("/api/enrollments/drop", state.handleDrop)

	// Users
	r.Get("/api/users", state.handleGetUsers)
	r.Post("/api/users", state.handleRegisterUser)
	r.Post("/api/users/{userId}/monthly-limit", state.handleUpdateUserMonthlyLimit)

	// Rooms
	r.Get("/api/rooms", state.handleGetRooms)
	r.Post("/api/rooms", state.handleCreateRoom)
	r.Put("/api/rooms/{roomId}", state.handleUpdateRoom)

	// Reservations
	r.Get("/api/reservations", state.handleGetReservations)
	r.Get("/api/reservations/by-room/{roomId}", state.handleGetReservationsByRoom)
	r.Post("/api/reservations/draft", state.handleCreateReservationDraft)
	r.Post("/api/reservations/quick", state.handleQuickReservation)
	r.Post("/api/reservations/{reservationId}/hold", state.handleCommitReservationHold)
	r.Post("/api/reservations/{reservationId}/confirm", state.handleConfirmReservation)
	r.Post("/api/reservations/{reservationId}/cancel", state.handleCancelReservation)
	r.Post("/api/reservations/{reservationId}/reject", state.handleRejectReservation)

	// Approvals
	r.Get("/api/approvals", state.handleGetApprovals)
	r.Post("/api/approvals/{approvalRequestId}/decision", state.handleRecordApprovalDecision)

	// Test data
	r.Post("/api/test-data/generate", state.handleGenerateTestData)
	r.Post("/api/test-data/generate-rooms", state.handleGenerateRooms)
	r.Post("/api/test-data/generate-reservations", state.handleGenerateReservations)

	addr := fmt.Sprintf(":%d", port)
	log.Printf("Listening on %s", addr)
	if err := http.ListenAndServe(addr, r); err != nil {
		log.Fatalf("Server error: %v", err)
	}
}

// ---------------------------------------------------------------------------
// Port and URL resolution
// ---------------------------------------------------------------------------

func resolvePort() int {
	if p := os.Getenv("PORT"); p != "" {
		if v, err := strconv.Atoi(p); err == nil {
			return v
		}
	}
	return 8080
}

func resolveWasmServerURL() string {
	if v := os.Getenv("WASM_SERVER_URL"); v != "" {
		return v
	}
	if v := os.Getenv("services__wasmserver__http__0"); v != "" {
		return v
	}
	if v := os.Getenv("services__wasmserver__https__0"); v != "" {
		return v
	}
	if v := os.Getenv("services__wasmserver__0"); v != "" {
		return v
	}
	return "http://localhost:5000"
}

// ---------------------------------------------------------------------------
// JSON helpers
// ---------------------------------------------------------------------------

func writeJSON(w http.ResponseWriter, status int, v any) {
	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(status)
	json.NewEncoder(w).Encode(v)
}

func writeRawJSON(w http.ResponseWriter, status int, raw string) {
	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(status)
	w.Write([]byte(raw))
}

func writeErrorJSON(w http.ResponseWriter, status int, code, message string) {
	writeJSON(w, status, map[string]string{"error": code, "message": message})
}

func writeErrorFromCommand(w http.ResponseWriter, err error) {
	switch {
	case errors.Is(err, sekiban.ErrAlreadyExists):
		writeErrorJSON(w, http.StatusConflict, "AlreadyExists", err.Error())
	case errors.Is(err, sekiban.ErrNotFound):
		writeErrorJSON(w, http.StatusNotFound, "NotFound", err.Error())
	case errors.Is(err, sekiban.ErrValidation):
		writeErrorJSON(w, http.StatusBadRequest, "Validation", err.Error())
	default:
		writeErrorJSON(w, http.StatusInternalServerError, "InternalError", err.Error())
	}
}

func parseBody(r *http.Request, v any) error {
	defer r.Body.Close()
	return json.NewDecoder(r.Body).Decode(v)
}

// ---------------------------------------------------------------------------
// Health
// ---------------------------------------------------------------------------

func (s *appState) handleHealth(w http.ResponseWriter, r *http.Request) {
	writeJSON(w, http.StatusOK, map[string]string{"message": "Sekiban decider Go ClientApi is running"})
}

// ---------------------------------------------------------------------------
// Weather handlers
// ---------------------------------------------------------------------------

func (s *appState) handleGetWeatherForecasts(w http.ResponseWriter, r *http.Request) {
	params := map[string]any{}
	if loc := r.URL.Query().Get("location"); loc != "" {
		params["locationFilter"] = loc
	}
	if loc := r.URL.Query().Get("locationFilter"); loc != "" {
		params["locationFilter"] = loc
	}
	if fid := r.URL.Query().Get("forecastId"); fid != "" {
		params["forecastId"] = fid
	}
	paramsJSON, _ := json.Marshal(params)
	result, err := s.runtime.ExecuteListQuery("GetWeatherForecastListQuery", string(paramsJSON), nil)
	if err != nil {
		writeErrorJSON(w, http.StatusBadGateway, "RuntimeQueryFailed", err.Error())
		return
	}
	writeRawJSON(w, http.StatusOK, result)
}

func (s *appState) handleGetWeatherForecastCount(w http.ResponseWriter, r *http.Request) {
	params := map[string]any{}
	if loc := r.URL.Query().Get("location"); loc != "" {
		params["locationFilter"] = loc
	}
	if loc := r.URL.Query().Get("locationFilter"); loc != "" {
		params["locationFilter"] = loc
	}
	paramsJSON, _ := json.Marshal(params)
	result, err := s.runtime.ExecuteQuery("GetWeatherForecastCountQuery", string(paramsJSON), nil)
	if err != nil {
		writeErrorJSON(w, http.StatusBadGateway, "RuntimeQueryFailed", err.Error())
		return
	}
	writeRawJSON(w, http.StatusOK, result)
}

func (s *appState) handleCreateWeatherForecast(w http.ResponseWriter, r *http.Request) {
	var body domain.CreateWeatherForecast
	if err := parseBody(r, &body); err != nil {
		writeErrorJSON(w, http.StatusBadRequest, "InvalidJson", "request body must be valid JSON")
		return
	}
	resp, err := s.runtime.FinalizeCommand(body)
	if err != nil {
		writeErrorFromCommand(w, err)
		return
	}
	writeJSON(w, http.StatusOK, resp)
}

func (s *appState) handleUpdateWeatherLocation(w http.ResponseWriter, r *http.Request) {
	var body domain.UpdateWeatherForecastLocation
	if err := parseBody(r, &body); err != nil {
		writeErrorJSON(w, http.StatusBadRequest, "InvalidJson", "request body must be valid JSON")
		return
	}
	resp, err := s.runtime.FinalizeCommand(body)
	if err != nil {
		writeErrorFromCommand(w, err)
		return
	}
	writeJSON(w, http.StatusOK, resp)
}

func (s *appState) handleDeleteWeatherForecast(w http.ResponseWriter, r *http.Request) {
	var body domain.DeleteWeatherForecast
	if err := parseBody(r, &body); err != nil {
		writeErrorJSON(w, http.StatusBadRequest, "InvalidJson", "request body must be valid JSON")
		return
	}
	resp, err := s.runtime.FinalizeCommand(body)
	if err != nil {
		writeErrorFromCommand(w, err)
		return
	}
	writeJSON(w, http.StatusOK, resp)
}

// ---------------------------------------------------------------------------
// Student handlers
// ---------------------------------------------------------------------------

func (s *appState) handleGetStudents(w http.ResponseWriter, r *http.Request) {
	result, err := s.runtime.ExecuteListQuery("GetStudentListQuery", "{}", nil)
	if err != nil {
		writeErrorJSON(w, http.StatusBadGateway, "RuntimeQueryFailed", err.Error())
		return
	}
	writeRawJSON(w, http.StatusOK, result)
}

func (s *appState) handleCreateStudent(w http.ResponseWriter, r *http.Request) {
	var body domain.CreateStudent
	if err := parseBody(r, &body); err != nil {
		writeErrorJSON(w, http.StatusBadRequest, "InvalidJson", "request body must be valid JSON")
		return
	}
	resp, err := s.runtime.FinalizeCommand(body)
	if err != nil {
		writeErrorFromCommand(w, err)
		return
	}
	writeJSON(w, http.StatusOK, resp)
}

// ---------------------------------------------------------------------------
// ClassRoom handlers
// ---------------------------------------------------------------------------

func (s *appState) handleGetClassRooms(w http.ResponseWriter, r *http.Request) {
	result, err := s.runtime.ExecuteListQuery("GetClassRoomListQuery", "{}", nil)
	if err != nil {
		writeErrorJSON(w, http.StatusBadGateway, "RuntimeQueryFailed", err.Error())
		return
	}
	writeRawJSON(w, http.StatusOK, result)
}

func (s *appState) handleCreateClassRoom(w http.ResponseWriter, r *http.Request) {
	var body domain.CreateClassRoom
	if err := parseBody(r, &body); err != nil {
		writeErrorJSON(w, http.StatusBadRequest, "InvalidJson", "request body must be valid JSON")
		return
	}
	resp, err := s.runtime.FinalizeCommand(body)
	if err != nil {
		writeErrorFromCommand(w, err)
		return
	}
	writeJSON(w, http.StatusOK, resp)
}

// ---------------------------------------------------------------------------
// Enrollment handlers
// ---------------------------------------------------------------------------

func (s *appState) handleEnroll(w http.ResponseWriter, r *http.Request) {
	var body domain.EnrollStudentInClassRoom
	if err := parseBody(r, &body); err != nil {
		writeErrorJSON(w, http.StatusBadRequest, "InvalidJson", "request body must be valid JSON")
		return
	}
	resp, err := s.runtime.FinalizeCommand(body)
	if err != nil {
		writeErrorFromCommand(w, err)
		return
	}
	writeJSON(w, http.StatusOK, resp)
}

func (s *appState) handleDrop(w http.ResponseWriter, r *http.Request) {
	var body domain.DropStudentFromClassRoom
	if err := parseBody(r, &body); err != nil {
		writeErrorJSON(w, http.StatusBadRequest, "InvalidJson", "request body must be valid JSON")
		return
	}
	resp, err := s.runtime.FinalizeCommand(body)
	if err != nil {
		writeErrorFromCommand(w, err)
		return
	}
	writeJSON(w, http.StatusOK, resp)
}

// ---------------------------------------------------------------------------
// User handlers
// ---------------------------------------------------------------------------

func (s *appState) handleGetUsers(w http.ResponseWriter, r *http.Request) {
	params := map[string]any{}
	if ao := r.URL.Query().Get("activeOnly"); ao == "true" {
		params["activeOnly"] = true
	}
	paramsJSON, _ := json.Marshal(params)
	result, err := s.runtime.ExecuteListQuery("GetUserDirectoryListQuery", string(paramsJSON), nil)
	if err != nil {
		writeErrorJSON(w, http.StatusBadGateway, "RuntimeQueryFailed", err.Error())
		return
	}
	writeRawJSON(w, http.StatusOK, result)
}

func (s *appState) handleRegisterUser(w http.ResponseWriter, r *http.Request) {
	var body domain.RegisterUser
	if err := parseBody(r, &body); err != nil {
		writeErrorJSON(w, http.StatusBadRequest, "InvalidJson", "request body must be valid JSON")
		return
	}
	resp, err := s.runtime.FinalizeCommand(body)
	if err != nil {
		writeErrorFromCommand(w, err)
		return
	}
	writeJSON(w, http.StatusOK, resp)
}

func (s *appState) handleUpdateUserMonthlyLimit(w http.ResponseWriter, r *http.Request) {
	userId := chi.URLParam(r, "userId")
	var body struct {
		MonthlyReservationLimit int `json:"monthlyReservationLimit"`
	}
	if err := parseBody(r, &body); err != nil {
		writeErrorJSON(w, http.StatusBadRequest, "InvalidJson", "request body must be valid JSON")
		return
	}
	cmd := domain.UpdateUserMonthlyReservationLimit{
		UserId:                  userId,
		MonthlyReservationLimit: body.MonthlyReservationLimit,
	}
	resp, err := s.runtime.FinalizeCommand(cmd)
	if err != nil {
		writeErrorFromCommand(w, err)
		return
	}
	writeJSON(w, http.StatusOK, resp)
}

// ---------------------------------------------------------------------------
// Room handlers
// ---------------------------------------------------------------------------

func (s *appState) handleGetRooms(w http.ResponseWriter, r *http.Request) {
	result, err := s.runtime.ExecuteListQuery("GetRoomListQuery", "{}", nil)
	if err != nil {
		writeErrorJSON(w, http.StatusBadGateway, "RuntimeQueryFailed", err.Error())
		return
	}
	writeRawJSON(w, http.StatusOK, result)
}

func (s *appState) handleCreateRoom(w http.ResponseWriter, r *http.Request) {
	var body domain.CreateRoom
	if err := parseBody(r, &body); err != nil {
		writeErrorJSON(w, http.StatusBadRequest, "InvalidJson", "request body must be valid JSON")
		return
	}
	resp, err := s.runtime.FinalizeCommand(body)
	if err != nil {
		writeErrorFromCommand(w, err)
		return
	}
	writeJSON(w, http.StatusOK, resp)
}

func (s *appState) handleUpdateRoom(w http.ResponseWriter, r *http.Request) {
	roomId := chi.URLParam(r, "roomId")
	var body domain.UpdateRoom
	if err := parseBody(r, &body); err != nil {
		writeErrorJSON(w, http.StatusBadRequest, "InvalidJson", "request body must be valid JSON")
		return
	}
	body.RoomId = roomId
	resp, err := s.runtime.FinalizeCommand(body)
	if err != nil {
		writeErrorFromCommand(w, err)
		return
	}
	writeJSON(w, http.StatusOK, resp)
}

// ---------------------------------------------------------------------------
// Reservation handlers
// ---------------------------------------------------------------------------

func (s *appState) handleGetReservations(w http.ResponseWriter, r *http.Request) {
	params := map[string]any{}
	if roomId := r.URL.Query().Get("roomId"); roomId != "" {
		params["roomId"] = roomId
	}
	paramsJSON, _ := json.Marshal(params)
	result, err := s.runtime.ExecuteListQuery("GetReservationListQuery", string(paramsJSON), nil)
	if err != nil {
		writeErrorJSON(w, http.StatusBadGateway, "RuntimeQueryFailed", err.Error())
		return
	}
	writeRawJSON(w, http.StatusOK, result)
}

func (s *appState) handleGetReservationsByRoom(w http.ResponseWriter, r *http.Request) {
	roomId := chi.URLParam(r, "roomId")
	params := map[string]any{"roomId": roomId}
	paramsJSON, _ := json.Marshal(params)
	result, err := s.runtime.ExecuteListQuery("GetReservationListQuery", string(paramsJSON), nil)
	if err != nil {
		writeErrorJSON(w, http.StatusBadGateway, "RuntimeQueryFailed", err.Error())
		return
	}
	writeRawJSON(w, http.StatusOK, result)
}

func (s *appState) handleCreateReservationDraft(w http.ResponseWriter, r *http.Request) {
	var body domain.CreateReservationDraft
	if err := parseBody(r, &body); err != nil {
		writeErrorJSON(w, http.StatusBadRequest, "InvalidJson", "request body must be valid JSON")
		return
	}
	resp, err := s.runtime.FinalizeCommand(body)
	if err != nil {
		writeErrorFromCommand(w, err)
		return
	}
	writeJSON(w, http.StatusOK, resp)
}

func (s *appState) handleCommitReservationHold(w http.ResponseWriter, r *http.Request) {
	reservationId := chi.URLParam(r, "reservationId")
	var body domain.CommitReservationHold
	if err := parseBody(r, &body); err != nil {
		writeErrorJSON(w, http.StatusBadRequest, "InvalidJson", "request body must be valid JSON")
		return
	}
	body.ReservationId = reservationId
	resp, err := s.runtime.FinalizeCommand(body)
	if err != nil {
		writeErrorFromCommand(w, err)
		return
	}
	writeJSON(w, http.StatusOK, resp)
}

func (s *appState) handleConfirmReservation(w http.ResponseWriter, r *http.Request) {
	reservationId := chi.URLParam(r, "reservationId")
	var body domain.ConfirmReservation
	if err := parseBody(r, &body); err != nil {
		writeErrorJSON(w, http.StatusBadRequest, "InvalidJson", "request body must be valid JSON")
		return
	}
	body.ReservationId = reservationId
	resp, err := s.runtime.FinalizeCommand(body)
	if err != nil {
		writeErrorFromCommand(w, err)
		return
	}
	writeJSON(w, http.StatusOK, resp)
}

func (s *appState) handleCancelReservation(w http.ResponseWriter, r *http.Request) {
	reservationId := chi.URLParam(r, "reservationId")
	var body domain.CancelReservation
	if err := parseBody(r, &body); err != nil {
		writeErrorJSON(w, http.StatusBadRequest, "InvalidJson", "request body must be valid JSON")
		return
	}
	body.ReservationId = reservationId
	resp, err := s.runtime.FinalizeCommand(body)
	if err != nil {
		writeErrorFromCommand(w, err)
		return
	}
	writeJSON(w, http.StatusOK, resp)
}

func (s *appState) handleRejectReservation(w http.ResponseWriter, r *http.Request) {
	reservationId := chi.URLParam(r, "reservationId")
	var body domain.RejectReservation
	if err := parseBody(r, &body); err != nil {
		writeErrorJSON(w, http.StatusBadRequest, "InvalidJson", "request body must be valid JSON")
		return
	}
	body.ReservationId = reservationId
	resp, err := s.runtime.FinalizeCommand(body)
	if err != nil {
		writeErrorFromCommand(w, err)
		return
	}
	writeJSON(w, http.StatusOK, resp)
}

// Quick reservation: single command that emits draft + hold (+ confirm)
func (s *appState) handleQuickReservation(w http.ResponseWriter, r *http.Request) {
	var body domain.CreateQuickReservation
	if err := parseBody(r, &body); err != nil {
		writeErrorJSON(w, http.StatusBadRequest, "InvalidJson", "request body must be valid JSON")
		return
	}

	// Ensure reservation ID is set before creating draft
	resId := ""
	if body.ReservationId != nil && *body.ReservationId != "" {
		resId = *body.ReservationId
	} else {
		resId = uuid.New().String()
		body.ReservationId = &resId
	}

	confirmResp, err := s.runtime.FinalizeCommand(body)
	if err != nil {
		writeErrorFromCommand(w, err)
		return
	}

	writeJSON(w, http.StatusOK, map[string]any{
		"reservationId": resId,
		"status":        "Confirmed",
		"commit":        confirmResp,
	})
}

// ---------------------------------------------------------------------------
// Approval handlers
// ---------------------------------------------------------------------------

func (s *appState) handleGetApprovals(w http.ResponseWriter, r *http.Request) {
	params := map[string]any{"pendingOnly": true}
	if po := r.URL.Query().Get("pendingOnly"); po == "false" {
		params["pendingOnly"] = false
	}
	paramsJSON, _ := json.Marshal(params)
	result, err := s.runtime.ExecuteListQuery("GetApprovalInboxQuery", string(paramsJSON), nil)
	if err != nil {
		writeErrorJSON(w, http.StatusBadGateway, "RuntimeQueryFailed", err.Error())
		return
	}
	writeRawJSON(w, http.StatusOK, result)
}

func (s *appState) handleRecordApprovalDecision(w http.ResponseWriter, r *http.Request) {
	approvalRequestId := chi.URLParam(r, "approvalRequestId")
	var body domain.RecordApprovalDecision
	if err := parseBody(r, &body); err != nil {
		writeErrorJSON(w, http.StatusBadRequest, "InvalidJson", "request body must be valid JSON")
		return
	}
	body.ApprovalRequestId = approvalRequestId
	resp, err := s.runtime.FinalizeCommand(body)
	if err != nil {
		writeErrorFromCommand(w, err)
		return
	}
	writeJSON(w, http.StatusOK, resp)
}

// ---------------------------------------------------------------------------
// Test data generation
// ---------------------------------------------------------------------------

func (s *appState) handleGenerateTestData(w http.ResponseWriter, r *http.Request) {
	countStr := r.URL.Query().Get("count")
	count := 100
	if countStr != "" {
		if v, err := strconv.Atoi(countStr); err == nil && v > 0 {
			count = v
		}
	}

	roomResults := generateRooms(s)
	reservationResults := generateReservations(s, count)

	writeJSON(w, http.StatusOK, map[string]any{
		"rooms":        roomResults,
		"reservations": reservationResults,
	})
}

func (s *appState) handleGenerateRooms(w http.ResponseWriter, r *http.Request) {
	results := generateRooms(s)
	writeJSON(w, http.StatusOK, results)
}

func (s *appState) handleGenerateReservations(w http.ResponseWriter, r *http.Request) {
	countStr := r.URL.Query().Get("count")
	count := 100
	if countStr != "" {
		if v, err := strconv.Atoi(countStr); err == nil && v > 0 {
			count = v
		}
	}
	results := generateReservations(s, count)
	writeJSON(w, http.StatusOK, results)
}

type roomSpec struct {
	Name             string
	Capacity         int
	Location         string
	Equipment        []string
	RequiresApproval bool
}

func generateRooms(s *appState) map[string]any {
	rooms := []roomSpec{
		{"Conference Room A", 10, "Floor 1", []string{"projector", "whiteboard"}, false},
		{"Conference Room B", 20, "Floor 1", []string{"projector", "whiteboard", "video"}, false},
		{"Board Room", 30, "Floor 2", []string{"projector", "whiteboard", "video", "phone"}, true},
		{"Training Room 1", 40, "Floor 2", []string{"projector", "whiteboard"}, false},
		{"Training Room 2", 40, "Floor 2", []string{"projector"}, false},
		{"Executive Suite", 8, "Floor 3", []string{"projector", "whiteboard", "video", "phone"}, true},
		{"Huddle Room 1", 4, "Floor 1", []string{"whiteboard"}, false},
		{"Huddle Room 2", 4, "Floor 1", []string{"whiteboard"}, false},
		{"Huddle Room 3", 4, "Floor 2", []string{"whiteboard"}, false},
		{"Auditorium", 100, "Floor 1", []string{"projector", "microphone", "video"}, true},
		{"Innovation Lab", 15, "Floor 3", []string{"projector", "whiteboard", "3d-printer"}, false},
		{"Quiet Room", 2, "Floor 2", []string{}, false},
		{"Phone Booth 1", 1, "Floor 1", []string{"phone"}, false},
		{"Phone Booth 2", 1, "Floor 1", []string{"phone"}, false},
		{"Phone Booth 3", 1, "Floor 2", []string{"phone"}, false},
		{"Workshop Room", 25, "Floor 3", []string{"projector", "whiteboard", "tools"}, false},
		{"Media Room", 12, "Floor 3", []string{"projector", "video", "sound-system"}, false},
		{"Lounge A", 15, "Floor 1", []string{}, false},
		{"Lounge B", 15, "Floor 2", []string{}, false},
		{"Rooftop Terrace", 50, "Rooftop", []string{}, true},
	}

	created := 0
	failed := 0
	for _, rm := range rooms {
		roomId := uuid.New().String()
		cmd := domain.CreateRoom{
			RoomId:           &roomId,
			Name:             rm.Name,
			Capacity:         rm.Capacity,
			Location:         rm.Location,
			Equipment:        rm.Equipment,
			RequiresApproval: rm.RequiresApproval,
		}
		_, err := s.runtime.FinalizeCommand(cmd)
		if err != nil {
			failed++
		} else {
			created++
		}
	}
	return map[string]any{"created": created, "failed": failed}
}

func generateReservations(s *appState, count int) map[string]any {
	// First register a user for organizing
	userId := uuid.New().String()
	userCmd := domain.RegisterUser{
		UserId:                  &userId,
		DisplayName:             "Test Organizer",
		Email:                   "test@example.com",
		MonthlyReservationLimit: count + 10,
	}
	s.runtime.FinalizeCommand(userCmd)

	// Get room list
	roomsJSON, err := s.runtime.ExecuteListQuery("GetRoomListQuery", "{}", nil)
	if err != nil || roomsJSON == "[]" {
		return map[string]any{"created": 0, "failed": 0, "error": "no rooms found"}
	}

	var roomItems []struct {
		RoomId string `json:"roomId"`
		Name   string `json:"name"`
	}
	json.Unmarshal([]byte(roomsJSON), &roomItems)
	if len(roomItems) == 0 {
		return map[string]any{"created": 0, "failed": 0, "error": "no rooms found"}
	}

	rng := rand.New(rand.NewSource(time.Now().UnixNano()))
	purposes := []string{
		"Team standup", "Sprint planning", "Design review",
		"1:1 meeting", "Client call", "Workshop",
		"Training session", "Brainstorming", "All-hands",
	}

	created := 0
	failed := 0
	baseTime := time.Now().UTC().Truncate(time.Hour).Add(time.Hour)

	for i := 0; i < count; i++ {
		room := roomItems[rng.Intn(len(roomItems))]
		resId := uuid.New().String()
		startTime := baseTime.Add(time.Duration(i*30) * time.Minute)
		endTime := startTime.Add(30 * time.Minute)
		purpose := purposes[rng.Intn(len(purposes))]

		draftCmd := domain.CreateReservationDraft{
			ReservationId:     &resId,
			RoomId:            room.RoomId,
			OrganizerId:       userId,
			OrganizerName:     "Test Organizer",
			StartTime:         startTime.Format(time.RFC3339),
			EndTime:           endTime.Format(time.RFC3339),
			Purpose:           purpose,
			SelectedEquipment: []string{},
		}
		_, err := s.runtime.FinalizeCommand(draftCmd)
		if err != nil {
			failed++
			continue
		}

		holdCmd := domain.CommitReservationHold{
			ReservationId:    resId,
			RoomId:           room.RoomId,
			RequiresApproval: false,
		}
		_, err = s.runtime.FinalizeCommand(holdCmd)
		if err != nil {
			failed++
			continue
		}

		confirmCmd := domain.ConfirmReservation{
			ReservationId: resId,
			RoomId:        room.RoomId,
		}
		_, err = s.runtime.FinalizeCommand(confirmCmd)
		if err != nil {
			failed++
			continue
		}

		created++
	}
	return map[string]any{"created": created, "failed": failed}
}
