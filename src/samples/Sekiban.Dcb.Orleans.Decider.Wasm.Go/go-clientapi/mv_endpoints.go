package main

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"log"
	"net/http"
	"net/url"
	"os"
	"strconv"
	"strings"
	"time"

	"github.com/jackc/pgx/v5"
	"github.com/jackc/pgx/v5/pgxpool"
)

// Read-only /api/mv/* endpoints for the Go sample. Connects directly to
// DcbMaterializedViewPostgres via pgx — the generic WasmRuntime.Host stays ignorant of
// application schema. Mirrors the Rust sample's split between write-through and read-only.

const (
	mvViewName    = "ClassRoomEnrollment"
	mvViewVersion = int32(1)

	mvLogicalClassrooms  = "classrooms"
	mvLogicalStudents    = "students"
	mvLogicalEnrollments = "enrollments"
)

func currentServiceID() string {
	if s := strings.TrimSpace(os.Getenv("SEKIBAN_SERVICE_ID")); s != "" {
		return s
	}
	return "default"
}

// connectMvPoolFromEnv opens a pgx pool when the Aspire-provided connection string is
// present. Accepts the `DCBMATERIALIZEDVIEWPOSTGRES_URI` form first (already a
// `postgresql://...` URL) then falls back to the Npgsql-style
// `ConnectionStrings__DcbMaterializedViewPostgres` key/value form.
//
// Returns nil without error when no MV DB is configured so the /api/mv/* routes can respond
// 503 cleanly instead of crashing the clientapi process.
func connectMvPoolFromEnv(ctx context.Context) (*pgxpool.Pool, error) {
	connString := resolveMvConnectionURL()
	if connString == "" {
		log.Printf("MV: DCBMATERIALIZEDVIEWPOSTGRES_URI / ConnectionStrings__DcbMaterializedViewPostgres not set — /api/mv/* disabled")
		return nil, nil
	}

	cfg, err := pgxpool.ParseConfig(connString)
	if err != nil {
		return nil, fmt.Errorf("parse MV connection string: %w", err)
	}
	cfg.MaxConns = 4
	cfg.MaxConnLifetime = 30 * time.Minute
	pool, err := pgxpool.NewWithConfig(ctx, cfg)
	if err != nil {
		return nil, fmt.Errorf("connect MV pool: %w", err)
	}
	return pool, nil
}

func resolveMvConnectionURL() string {
	if url := strings.TrimSpace(os.Getenv("DCBMATERIALIZEDVIEWPOSTGRES_URI")); url != "" {
		return url
	}
	raw := strings.TrimSpace(os.Getenv("ConnectionStrings__DcbMaterializedViewPostgres"))
	if raw == "" {
		return ""
	}
	return npgsqlToURL(raw)
}

// npgsqlToURL converts `Host=...;Port=...;Username=...;Password=...;Database=...` into a
// postgresql:// URL pgx understands. Mirrors the Rust and Swift helpers.
func npgsqlToURL(raw string) string {
	var host, port, username, password, database string
	for _, pair := range strings.Split(raw, ";") {
		pair = strings.TrimSpace(pair)
		if pair == "" {
			continue
		}
		eq := strings.Index(pair, "=")
		if eq < 0 {
			continue
		}
		key := strings.ToLower(strings.TrimSpace(pair[:eq]))
		value := pair[eq+1:]
		switch key {
		case "host", "server":
			host = value
		case "port":
			port = value
		case "username", "user id", "uid":
			username = value
		case "password", "pwd":
			password = value
		case "database", "db":
			database = value
		}
	}
	if host == "" || database == "" {
		return ""
	}
	if port == "" {
		port = "5432"
	}
	if username == "" {
		username = "postgres"
	}
	return fmt.Sprintf("postgresql://%s:%s@%s:%s/%s",
		url.QueryEscape(username), url.QueryEscape(password),
		host, port, database)
}

// mvPhysicalTable resolves a logical table name against sekiban_mv_registry, scoped by
// service_id. Matches the Rust sample's query so Postgres data layout stays comparable
// across languages.
func mvPhysicalTable(ctx context.Context, pool *pgxpool.Pool, logical string) (string, error) {
	var physical string
	serviceID := currentServiceID()
	err := pool.QueryRow(ctx,
		`SELECT physical_table
		 FROM sekiban_mv_registry
		 WHERE service_id = $1 AND view_name = $2 AND view_version = $3 AND logical_table = $4
		 LIMIT 1`,
		serviceID, mvViewName, mvViewVersion, logical,
	).Scan(&physical)
	if errors.Is(err, pgx.ErrNoRows) {
		return "", fmt.Errorf("materialized view '%s' not registered for %s/%s/%d",
			logical, serviceID, mvViewName, mvViewVersion)
	}
	if err != nil {
		return "", fmt.Errorf("lookup physical_table: %w", err)
	}
	return physical, nil
}

func pagingFromRequest(r *http.Request) (int, int) {
	sizeRaw := firstQuery(r, "page_size", "pageSize")
	pageRaw := firstQuery(r, "page_number", "pageNumber")

	size, _ := strconv.Atoi(sizeRaw)
	if size <= 0 {
		size = 20
	}
	if size > 100 {
		size = 100
	}
	page, _ := strconv.Atoi(pageRaw)
	if page < 1 {
		page = 1
	}
	return size, (page - 1) * size
}

func firstQuery(r *http.Request, keys ...string) string {
	for _, k := range keys {
		if v := strings.TrimSpace(r.URL.Query().Get(k)); v != "" {
			return v
		}
	}
	return ""
}

// ------------------------------------------------------------------
// Handlers
// ------------------------------------------------------------------

func (s *appState) handleMvStatus(w http.ResponseWriter, r *http.Request) {
	if s.mvPool == nil {
		writeMvDisabled(w)
		return
	}
	serviceID := currentServiceID()
	rows, err := s.mvPool.Query(r.Context(),
		`SELECT service_id, view_name, view_version, logical_table, physical_table, status,
		        applied_event_version, current_position, last_catch_up_sortable_unique_id,
		        last_updated
		 FROM sekiban_mv_registry
		 WHERE service_id = $1 AND view_name = $2 AND view_version = $3
		 ORDER BY logical_table`,
		serviceID, mvViewName, mvViewVersion)
	if err != nil {
		writeJSON(w, http.StatusInternalServerError, map[string]string{"error": err.Error()})
		return
	}
	defer rows.Close()

	type entry struct {
		ServiceID                    string     `json:"service_id"`
		ViewName                     string     `json:"view_name"`
		ViewVersion                  int32      `json:"view_version"`
		LogicalTable                 string     `json:"logical_table"`
		PhysicalTable                string     `json:"physical_table"`
		Status                       int32      `json:"status"`
		AppliedEventVersion          int64      `json:"applied_event_version"`
		CurrentPosition              *string    `json:"current_position"`
		LastCatchUpSortableUniqueID  *string    `json:"last_catch_up_sortable_unique_id"`
		LastUpdated                  *time.Time `json:"last_updated"`
	}
	entries := make([]entry, 0)
	for rows.Next() {
		var e entry
		if err := rows.Scan(&e.ServiceID, &e.ViewName, &e.ViewVersion, &e.LogicalTable,
			&e.PhysicalTable, &e.Status, &e.AppliedEventVersion, &e.CurrentPosition,
			&e.LastCatchUpSortableUniqueID, &e.LastUpdated); err != nil {
			writeJSON(w, http.StatusInternalServerError, map[string]string{"error": err.Error()})
			return
		}
		entries = append(entries, e)
	}

	writeJSON(w, http.StatusOK, map[string]any{
		"service_id":   serviceID,
		"view_name":    mvViewName,
		"view_version": mvViewVersion,
		"entries":      entries,
	})
}

func (s *appState) handleMvClassrooms(w http.ResponseWriter, r *http.Request) {
	if s.mvPool == nil {
		writeMvDisabled(w)
		return
	}
	ctx := r.Context()
	table, err := mvPhysicalTable(ctx, s.mvPool, mvLogicalClassrooms)
	if err != nil {
		writeJSON(w, http.StatusInternalServerError, map[string]string{"error": err.Error()})
		return
	}
	limit, offset := pagingFromRequest(r)
	// Physical table names come from sekiban_mv_registry (Sekiban-controlled), so direct SQL
	// interpolation is safe — pgx does not accept dynamic identifier parameters otherwise.
	//nolint:gosec // table name is sourced from internal registry, not user input.
	sql := fmt.Sprintf(`SELECT class_room_id, name, max_students, enrolled_count,
	                          _last_sortable_unique_id, _last_applied_at
	                   FROM %q
	                   ORDER BY name
	                   LIMIT $1 OFFSET $2`, table)
	rows, err := s.mvPool.Query(ctx, sql, limit, offset)
	if err != nil {
		writeJSON(w, http.StatusInternalServerError, map[string]string{"error": err.Error()})
		return
	}
	defer rows.Close()

	type row struct {
		ClassRoomID          string    `json:"class_room_id"`
		Name                 string    `json:"name"`
		MaxStudents          int32     `json:"max_students"`
		EnrolledCount        int32     `json:"enrolled_count"`
		LastSortableUniqueID string    `json:"last_sortable_unique_id"`
		LastAppliedAt        time.Time `json:"last_applied_at"`
	}
	out := make([]row, 0)
	for rows.Next() {
		var rec row
		if err := rows.Scan(&rec.ClassRoomID, &rec.Name, &rec.MaxStudents, &rec.EnrolledCount,
			&rec.LastSortableUniqueID, &rec.LastAppliedAt); err != nil {
			writeJSON(w, http.StatusInternalServerError, map[string]string{"error": err.Error()})
			return
		}
		out = append(out, rec)
	}
	writeJSON(w, http.StatusOK, out)
}

func (s *appState) handleMvStudents(w http.ResponseWriter, r *http.Request) {
	if s.mvPool == nil {
		writeMvDisabled(w)
		return
	}
	ctx := r.Context()
	table, err := mvPhysicalTable(ctx, s.mvPool, mvLogicalStudents)
	if err != nil {
		writeJSON(w, http.StatusInternalServerError, map[string]string{"error": err.Error()})
		return
	}
	limit, offset := pagingFromRequest(r)
	sql := fmt.Sprintf(`SELECT student_id, name, max_class_count, enrolled_count,
	                          _last_sortable_unique_id, _last_applied_at
	                   FROM %q
	                   ORDER BY name
	                   LIMIT $1 OFFSET $2`, table)
	rows, err := s.mvPool.Query(ctx, sql, limit, offset)
	if err != nil {
		writeJSON(w, http.StatusInternalServerError, map[string]string{"error": err.Error()})
		return
	}
	defer rows.Close()

	type row struct {
		StudentID            string    `json:"student_id"`
		Name                 string    `json:"name"`
		MaxClassCount        int32     `json:"max_class_count"`
		EnrolledCount        int32     `json:"enrolled_count"`
		LastSortableUniqueID string    `json:"last_sortable_unique_id"`
		LastAppliedAt        time.Time `json:"last_applied_at"`
	}
	out := make([]row, 0)
	for rows.Next() {
		var rec row
		if err := rows.Scan(&rec.StudentID, &rec.Name, &rec.MaxClassCount, &rec.EnrolledCount,
			&rec.LastSortableUniqueID, &rec.LastAppliedAt); err != nil {
			writeJSON(w, http.StatusInternalServerError, map[string]string{"error": err.Error()})
			return
		}
		out = append(out, rec)
	}
	writeJSON(w, http.StatusOK, out)
}

func (s *appState) handleMvEnrollments(w http.ResponseWriter, r *http.Request) {
	if s.mvPool == nil {
		writeMvDisabled(w)
		return
	}
	ctx := r.Context()
	table, err := mvPhysicalTable(ctx, s.mvPool, mvLogicalEnrollments)
	if err != nil {
		writeJSON(w, http.StatusInternalServerError, map[string]string{"error": err.Error()})
		return
	}
	limit, offset := pagingFromRequest(r)
	studentID := firstQuery(r, "student_id", "studentId")
	classRoomID := firstQuery(r, "class_room_id", "classRoomId")

	var args []any
	where := ""
	args = append(args, limit, offset)
	placeholderIdx := 3
	if studentID != "" {
		where += fmt.Sprintf(" AND student_id = $%d::uuid", placeholderIdx)
		args = append(args, studentID)
		placeholderIdx++
	}
	if classRoomID != "" {
		where += fmt.Sprintf(" AND class_room_id = $%d::uuid", placeholderIdx)
		args = append(args, classRoomID)
		placeholderIdx++
	}
	sql := fmt.Sprintf(`SELECT student_id, class_room_id, enrolled_at, _last_sortable_unique_id
	                   FROM %q
	                   WHERE 1=1%s
	                   ORDER BY enrolled_at DESC
	                   LIMIT $1 OFFSET $2`, table, where)
	rows, err := s.mvPool.Query(ctx, sql, args...)
	if err != nil {
		writeJSON(w, http.StatusInternalServerError, map[string]string{"error": err.Error()})
		return
	}
	defer rows.Close()

	type row struct {
		StudentID            string    `json:"student_id"`
		ClassRoomID          string    `json:"class_room_id"`
		EnrolledAt           time.Time `json:"enrolled_at"`
		LastSortableUniqueID string    `json:"last_sortable_unique_id"`
	}
	out := make([]row, 0)
	for rows.Next() {
		var rec row
		if err := rows.Scan(&rec.StudentID, &rec.ClassRoomID, &rec.EnrolledAt,
			&rec.LastSortableUniqueID); err != nil {
			writeJSON(w, http.StatusInternalServerError, map[string]string{"error": err.Error()})
			return
		}
		out = append(out, rec)
	}
	writeJSON(w, http.StatusOK, out)
}

func writeMvDisabled(w http.ResponseWriter) {
	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(http.StatusServiceUnavailable)
	_ = json.NewEncoder(w).Encode(map[string]string{
		"error": "Materialized view Postgres is not configured. Set DCBMATERIALIZEDVIEWPOSTGRES_URI or ConnectionStrings__DcbMaterializedViewPostgres.",
	})
}
