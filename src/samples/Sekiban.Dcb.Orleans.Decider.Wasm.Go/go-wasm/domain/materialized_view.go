package domain

import (
	"crypto/sha1"
	"encoding/hex"
	"encoding/json"
	"fmt"

	"github.com/J-Tech-Japan/SekibanWasmRuntime/src/lib/sekiban-go/mv"
)

// Logical table names for the ClassRoomEnrollment view. Kept as exported constants so the
// Go ClientApi can cross-reference them against `sekiban_mv_registry` without stringly-typing
// the same values in two places.
const (
	ClassRoomsLogicalTable   = "classrooms"
	StudentsLogicalTable     = "students"
	EnrollmentsLogicalTable  = "enrollments"
	ClassRoomEnrollmentView  = "ClassRoomEnrollment"
	ClassRoomEnrollmentViewV = int32(1)
)

// ClassRoomEnrollmentMvV1 is the Go port of the C#/Rust/Swift projector of the same name.
// Emits identical SQL so the three samples can share a MV Postgres database and produce
// registry rows that line up exactly.
type ClassRoomEnrollmentMvV1 struct{}

func (ClassRoomEnrollmentMvV1) ViewName() string    { return ClassRoomEnrollmentView }
func (ClassRoomEnrollmentMvV1) ViewVersion() int32  { return ClassRoomEnrollmentViewV }
func (ClassRoomEnrollmentMvV1) LogicalTables() []string {
	return []string{ClassRoomsLogicalTable, StudentsLogicalTable, EnrollmentsLogicalTable}
}

func (ClassRoomEnrollmentMvV1) Initialize(tables mv.MvTableBindingsDto) []mv.MvSqlStatementDto {
	classrooms := tables.PhysicalName(ClassRoomsLogicalTable)
	students := tables.PhysicalName(StudentsLogicalTable)
	enrollments := tables.PhysicalName(EnrollmentsLogicalTable)
	return []mv.MvSqlStatementDto{
		stmt(fmt.Sprintf(`CREATE TABLE IF NOT EXISTS %s (
class_room_id UUID PRIMARY KEY,
name TEXT NOT NULL,
max_students INT NOT NULL,
enrolled_count INT NOT NULL DEFAULT 0,
_last_sortable_unique_id TEXT NOT NULL,
_last_applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);`, classrooms)),
		stmt(fmt.Sprintf(`CREATE TABLE IF NOT EXISTS %s (
student_id UUID PRIMARY KEY,
name TEXT NOT NULL,
max_class_count INT NOT NULL,
enrolled_count INT NOT NULL DEFAULT 0,
_last_sortable_unique_id TEXT NOT NULL,
_last_applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);`, students)),
		stmt(fmt.Sprintf(`CREATE TABLE IF NOT EXISTS %s (
student_id UUID NOT NULL,
class_room_id UUID NOT NULL,
enrolled_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
_last_sortable_unique_id TEXT NOT NULL,
PRIMARY KEY (student_id, class_room_id)
);`, enrollments)),
		stmt(fmt.Sprintf(`CREATE INDEX IF NOT EXISTS %s ON %s (class_room_id);`,
			buildIndexName(enrollments, "class_room"), enrollments)),
	}
}

func (p ClassRoomEnrollmentMvV1) ApplyEvent(
	tables mv.MvTableBindingsDto,
	event mv.MvSerializableEventDto,
	_ mv.QueryPort,
) []mv.MvSqlStatementDto {
	switch event.EventType {
	case "ClassRoomCreated":
		var created ClassRoomCreated
		if err := json.Unmarshal([]byte(event.PayloadJSON), &created); err != nil {
			return nil
		}
		return []mv.MvSqlStatementDto{p.insertClassRoom(tables, created, event.SortableUniqueId)}
	case "StudentCreated":
		var created StudentCreated
		if err := json.Unmarshal([]byte(event.PayloadJSON), &created); err != nil {
			return nil
		}
		return []mv.MvSqlStatementDto{p.insertStudent(tables, created, event.SortableUniqueId)}
	case "StudentEnrolledInClassRoom":
		var enrolled StudentEnrolledInClassRoom
		if err := json.Unmarshal([]byte(event.PayloadJSON), &enrolled); err != nil {
			return nil
		}
		return p.insertEnrollment(tables, enrolled, event.SortableUniqueId)
	case "StudentDroppedFromClassRoom":
		var dropped StudentDroppedFromClassRoom
		if err := json.Unmarshal([]byte(event.PayloadJSON), &dropped); err != nil {
			return nil
		}
		return p.deleteEnrollment(tables, dropped, event.SortableUniqueId)
	}
	return nil
}

// stmt wraps a DDL string with an empty parameter list. Go's encoding/json emits `null` for a
// nil slice; the C# host's `WasmMvApplyHost.ToSekibanStatements` expects an iterable so we
// serialize an empty slice instead to produce `"parameters": []`.
func stmt(sql string) mv.MvSqlStatementDto {
	return mv.MvSqlStatementDto{Sql: sql, Parameters: []mv.MvParam{}}
}

func (ClassRoomEnrollmentMvV1) insertClassRoom(
	tables mv.MvTableBindingsDto, created ClassRoomCreated, sortableUniqueId string,
) mv.MvSqlStatementDto {
	table := tables.PhysicalName(ClassRoomsLogicalTable)
	return mv.MvSqlStatementDto{
		Sql: fmt.Sprintf(`INSERT INTO %s
(class_room_id, name, max_students, enrolled_count, _last_sortable_unique_id, _last_applied_at)
VALUES (@ClassRoomId, @Name, @MaxStudents, 0, @SortableUniqueId, NOW())
ON CONFLICT (class_room_id) DO UPDATE SET
name = EXCLUDED.name,
max_students = EXCLUDED.max_students,
_last_sortable_unique_id = EXCLUDED._last_sortable_unique_id,
_last_applied_at = EXCLUDED._last_applied_at
WHERE %s._last_sortable_unique_id < EXCLUDED._last_sortable_unique_id;`, table, table),
		Parameters: mv.NewParams().
			Guid("ClassRoomId", created.ClassRoomId).
			String("Name", created.Name).
			Int32("MaxStudents", int32(created.MaxStudents)).
			String("SortableUniqueId", sortableUniqueId).
			Build(),
	}
}

func (ClassRoomEnrollmentMvV1) insertStudent(
	tables mv.MvTableBindingsDto, created StudentCreated, sortableUniqueId string,
) mv.MvSqlStatementDto {
	table := tables.PhysicalName(StudentsLogicalTable)
	return mv.MvSqlStatementDto{
		Sql: fmt.Sprintf(`INSERT INTO %s
(student_id, name, max_class_count, enrolled_count, _last_sortable_unique_id, _last_applied_at)
VALUES (@StudentId, @Name, @MaxClassCount, 0, @SortableUniqueId, NOW())
ON CONFLICT (student_id) DO UPDATE SET
name = EXCLUDED.name,
max_class_count = EXCLUDED.max_class_count,
_last_sortable_unique_id = EXCLUDED._last_sortable_unique_id,
_last_applied_at = EXCLUDED._last_applied_at
WHERE %s._last_sortable_unique_id < EXCLUDED._last_sortable_unique_id;`, table, table),
		Parameters: mv.NewParams().
			Guid("StudentId", created.StudentId).
			String("Name", created.Name).
			Int32("MaxClassCount", int32(created.MaxClassCount)).
			String("SortableUniqueId", sortableUniqueId).
			Build(),
	}
}

func (p ClassRoomEnrollmentMvV1) insertEnrollment(
	tables mv.MvTableBindingsDto, enrolled StudentEnrolledInClassRoom, sortableUniqueId string,
) []mv.MvSqlStatementDto {
	enrollments := tables.PhysicalName(EnrollmentsLogicalTable)
	return []mv.MvSqlStatementDto{
		{
			Sql: fmt.Sprintf(`INSERT INTO %s
(student_id, class_room_id, enrolled_at, _last_sortable_unique_id)
VALUES (@StudentId, @ClassRoomId, NOW(), @SortableUniqueId)
ON CONFLICT (student_id, class_room_id) DO UPDATE SET
_last_sortable_unique_id = EXCLUDED._last_sortable_unique_id
WHERE %s._last_sortable_unique_id < EXCLUDED._last_sortable_unique_id;`, enrollments, enrollments),
			Parameters: mv.NewParams().
				Guid("StudentId", enrolled.StudentId).
				Guid("ClassRoomId", enrolled.ClassRoomId).
				String("SortableUniqueId", sortableUniqueId).
				Build(),
		},
		p.recountClassRoom(tables, enrolled.ClassRoomId, sortableUniqueId),
		p.recountStudent(tables, enrolled.StudentId, sortableUniqueId),
	}
}

func (p ClassRoomEnrollmentMvV1) deleteEnrollment(
	tables mv.MvTableBindingsDto, dropped StudentDroppedFromClassRoom, sortableUniqueId string,
) []mv.MvSqlStatementDto {
	enrollments := tables.PhysicalName(EnrollmentsLogicalTable)
	return []mv.MvSqlStatementDto{
		{
			Sql: fmt.Sprintf(`DELETE FROM %s
WHERE student_id = @StudentId
AND class_room_id = @ClassRoomId
AND _last_sortable_unique_id < @SortableUniqueId;`, enrollments),
			Parameters: mv.NewParams().
				Guid("StudentId", dropped.StudentId).
				Guid("ClassRoomId", dropped.ClassRoomId).
				String("SortableUniqueId", sortableUniqueId).
				Build(),
		},
		p.recountClassRoom(tables, dropped.ClassRoomId, sortableUniqueId),
		p.recountStudent(tables, dropped.StudentId, sortableUniqueId),
	}
}

func (ClassRoomEnrollmentMvV1) recountClassRoom(
	tables mv.MvTableBindingsDto, classRoomId, sortableUniqueId string,
) mv.MvSqlStatementDto {
	classrooms := tables.PhysicalName(ClassRoomsLogicalTable)
	enrollments := tables.PhysicalName(EnrollmentsLogicalTable)
	return mv.MvSqlStatementDto{
		Sql: fmt.Sprintf(`UPDATE %s
SET enrolled_count = (
SELECT COUNT(*) FROM %s WHERE class_room_id = @ClassRoomId
),
_last_sortable_unique_id = @SortableUniqueId,
_last_applied_at = NOW()
WHERE class_room_id = @ClassRoomId
AND _last_sortable_unique_id < @SortableUniqueId;`, classrooms, enrollments),
		Parameters: mv.NewParams().
			Guid("ClassRoomId", classRoomId).
			String("SortableUniqueId", sortableUniqueId).
			Build(),
	}
}

func (ClassRoomEnrollmentMvV1) recountStudent(
	tables mv.MvTableBindingsDto, studentId, sortableUniqueId string,
) mv.MvSqlStatementDto {
	students := tables.PhysicalName(StudentsLogicalTable)
	enrollments := tables.PhysicalName(EnrollmentsLogicalTable)
	return mv.MvSqlStatementDto{
		Sql: fmt.Sprintf(`UPDATE %s
SET enrolled_count = (
SELECT COUNT(*) FROM %s WHERE student_id = @StudentId
),
_last_sortable_unique_id = @SortableUniqueId,
_last_applied_at = NOW()
WHERE student_id = @StudentId
AND _last_sortable_unique_id < @SortableUniqueId;`, students, enrollments),
		Parameters: mv.NewParams().
			Guid("StudentId", studentId).
			String("SortableUniqueId", sortableUniqueId).
			Build(),
	}
}

// buildIndexName mirrors the C#/Rust/Swift helper so the generated DDL matches across samples.
// Postgres identifier length limit is 63 bytes; truncate long physical names and append a
// stable SHA-1 prefix so the index name stays unique.
func buildIndexName(physicalTable, suffix string) string {
	const maxLength = 63
	const prefix = "idx_"
	tail := "_" + suffix
	available := maxLength - len(prefix) - len(tail)
	if len(physicalTable) <= available {
		return prefix + physicalTable + tail
	}
	sum := sha1.Sum([]byte(physicalTable))
	hash := hex.EncodeToString(sum[:])[:8]
	headroom := available - 9
	if headroom < 1 {
		headroom = 1
	}
	head := physicalTable
	if len(head) > headroom {
		head = head[:headroom]
	}
	return prefix + head + "_" + hash + tail
}
