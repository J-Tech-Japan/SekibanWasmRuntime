//! Rust port of the C# `ClassRoomEnrollmentMvV1` projector.
//!
//! Runs inside the Rust `.wasm` module and emits the same SQL as the C# projector so the two
//! samples share the Postgres-side table shape, bump strategy, and recount logic. Kept here
//! (inside the Rust event-source crate) because it depends on the event payload types defined
//! alongside the events themselves.

use sekiban_mv::{
    dto::{MvSerializableEventDto, MvSqlStatementDto, MvTableBindingsDto},
    param_builder::MvParamBuilder,
    projector::WasmMvProjector,
    query_port::MvQueryPort,
};
use sha1::Digest;

use crate::events::{
    ClassRoomCreated, StudentCreated, StudentDroppedFromClassRoom, StudentEnrolledInClassRoom,
};

pub const CLASSROOMS_LOGICAL: &str = "classrooms";
pub const STUDENTS_LOGICAL: &str = "students";
pub const ENROLLMENTS_LOGICAL: &str = "enrollments";

pub struct ClassRoomEnrollmentMvV1;

impl WasmMvProjector for ClassRoomEnrollmentMvV1 {
    fn view_name(&self) -> &'static str {
        "ClassRoomEnrollment"
    }

    fn view_version(&self) -> i32 {
        1
    }

    fn logical_tables(&self) -> &'static [&'static str] {
        &[CLASSROOMS_LOGICAL, STUDENTS_LOGICAL, ENROLLMENTS_LOGICAL]
    }

    fn initialize(&self, tables: &MvTableBindingsDto) -> Vec<MvSqlStatementDto> {
        let classrooms = tables.get_physical_name(CLASSROOMS_LOGICAL);
        let students = tables.get_physical_name(STUDENTS_LOGICAL);
        let enrollments = tables.get_physical_name(ENROLLMENTS_LOGICAL);

        vec![
            stmt(format!(
                "CREATE TABLE IF NOT EXISTS {classrooms} (\n\
                 class_room_id UUID PRIMARY KEY,\n\
                 name TEXT NOT NULL,\n\
                 max_students INT NOT NULL,\n\
                 enrolled_count INT NOT NULL DEFAULT 0,\n\
                 _last_sortable_unique_id TEXT NOT NULL,\n\
                 _last_applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()\n\
                 );"
            )),
            stmt(format!(
                "CREATE TABLE IF NOT EXISTS {students} (\n\
                 student_id UUID PRIMARY KEY,\n\
                 name TEXT NOT NULL,\n\
                 max_class_count INT NOT NULL,\n\
                 enrolled_count INT NOT NULL DEFAULT 0,\n\
                 _last_sortable_unique_id TEXT NOT NULL,\n\
                 _last_applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()\n\
                 );"
            )),
            stmt(format!(
                "CREATE TABLE IF NOT EXISTS {enrollments} (\n\
                 student_id UUID NOT NULL,\n\
                 class_room_id UUID NOT NULL,\n\
                 enrolled_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),\n\
                 _last_sortable_unique_id TEXT NOT NULL,\n\
                 PRIMARY KEY (student_id, class_room_id)\n\
                 );"
            )),
            stmt(format!(
                "CREATE INDEX IF NOT EXISTS {idx} ON {enrollments} (class_room_id);",
                idx = build_index_name(&enrollments, "class_room"),
            )),
        ]
    }

    fn apply_event(
        &self,
        tables: &MvTableBindingsDto,
        event: &MvSerializableEventDto,
        _query_port: &dyn MvQueryPort,
    ) -> Vec<MvSqlStatementDto> {
        match event.event_type.as_str() {
            "ClassRoomCreated" => {
                let Ok(created) = serde_json::from_str::<ClassRoomCreated>(&event.payload_json) else {
                    return vec![];
                };
                vec![insert_classroom(tables, &created, &event.sortable_unique_id)]
            }
            "StudentCreated" => {
                let Ok(created) = serde_json::from_str::<StudentCreated>(&event.payload_json) else {
                    return vec![];
                };
                vec![insert_student(tables, &created, &event.sortable_unique_id)]
            }
            "StudentEnrolledInClassRoom" => {
                let Ok(enrolled) =
                    serde_json::from_str::<StudentEnrolledInClassRoom>(&event.payload_json)
                else {
                    return vec![];
                };
                insert_enrollment(tables, &enrolled, &event.sortable_unique_id)
            }
            "StudentDroppedFromClassRoom" => {
                let Ok(dropped) =
                    serde_json::from_str::<StudentDroppedFromClassRoom>(&event.payload_json)
                else {
                    return vec![];
                };
                delete_enrollment(tables, &dropped, &event.sortable_unique_id)
            }
            _ => vec![],
        }
    }
}

fn stmt(sql: String) -> MvSqlStatementDto {
    MvSqlStatementDto { sql, parameters: vec![] }
}

fn insert_classroom(
    tables: &MvTableBindingsDto,
    created: &ClassRoomCreated,
    sortable_unique_id: &str,
) -> MvSqlStatementDto {
    let table = tables.get_physical_name(CLASSROOMS_LOGICAL);
    MvSqlStatementDto {
        sql: format!(
            "INSERT INTO {table}\n\
             (class_room_id, name, max_students, enrolled_count, _last_sortable_unique_id, _last_applied_at)\n\
             VALUES (@ClassRoomId, @Name, @MaxStudents, 0, @SortableUniqueId, NOW())\n\
             ON CONFLICT (class_room_id) DO UPDATE SET\n\
             name = EXCLUDED.name,\n\
             max_students = EXCLUDED.max_students,\n\
             _last_sortable_unique_id = EXCLUDED._last_sortable_unique_id,\n\
             _last_applied_at = EXCLUDED._last_applied_at\n\
             WHERE {table}._last_sortable_unique_id < EXCLUDED._last_sortable_unique_id;"
        ),
        parameters: MvParamBuilder::new()
            .guid("ClassRoomId", created.class_room_id)
            .string("Name", created.name.clone())
            .int32("MaxStudents", created.max_students)
            .string("SortableUniqueId", sortable_unique_id)
            .build(),
    }
}

fn insert_student(
    tables: &MvTableBindingsDto,
    created: &StudentCreated,
    sortable_unique_id: &str,
) -> MvSqlStatementDto {
    let table = tables.get_physical_name(STUDENTS_LOGICAL);
    MvSqlStatementDto {
        sql: format!(
            "INSERT INTO {table}\n\
             (student_id, name, max_class_count, enrolled_count, _last_sortable_unique_id, _last_applied_at)\n\
             VALUES (@StudentId, @Name, @MaxClassCount, 0, @SortableUniqueId, NOW())\n\
             ON CONFLICT (student_id) DO UPDATE SET\n\
             name = EXCLUDED.name,\n\
             max_class_count = EXCLUDED.max_class_count,\n\
             _last_sortable_unique_id = EXCLUDED._last_sortable_unique_id,\n\
             _last_applied_at = EXCLUDED._last_applied_at\n\
             WHERE {table}._last_sortable_unique_id < EXCLUDED._last_sortable_unique_id;"
        ),
        parameters: MvParamBuilder::new()
            .guid("StudentId", created.student_id)
            .string("Name", created.name.clone())
            .int32("MaxClassCount", created.max_class_count)
            .string("SortableUniqueId", sortable_unique_id)
            .build(),
    }
}

fn insert_enrollment(
    tables: &MvTableBindingsDto,
    enrolled: &StudentEnrolledInClassRoom,
    sortable_unique_id: &str,
) -> Vec<MvSqlStatementDto> {
    let enrollments = tables.get_physical_name(ENROLLMENTS_LOGICAL);
    vec![
        MvSqlStatementDto {
            sql: format!(
                "INSERT INTO {enrollments}\n\
                 (student_id, class_room_id, enrolled_at, _last_sortable_unique_id)\n\
                 VALUES (@StudentId, @ClassRoomId, NOW(), @SortableUniqueId)\n\
                 ON CONFLICT (student_id, class_room_id) DO UPDATE SET\n\
                 _last_sortable_unique_id = EXCLUDED._last_sortable_unique_id\n\
                 WHERE {enrollments}._last_sortable_unique_id < EXCLUDED._last_sortable_unique_id;"
            ),
            parameters: MvParamBuilder::new()
                .guid("StudentId", enrolled.student_id)
                .guid("ClassRoomId", enrolled.class_room_id)
                .string("SortableUniqueId", sortable_unique_id)
                .build(),
        },
        recount_classroom(tables, enrolled.class_room_id, sortable_unique_id),
        recount_student(tables, enrolled.student_id, sortable_unique_id),
    ]
}

fn delete_enrollment(
    tables: &MvTableBindingsDto,
    dropped: &StudentDroppedFromClassRoom,
    sortable_unique_id: &str,
) -> Vec<MvSqlStatementDto> {
    let enrollments = tables.get_physical_name(ENROLLMENTS_LOGICAL);
    vec![
        MvSqlStatementDto {
            sql: format!(
                "DELETE FROM {enrollments}\n\
                 WHERE student_id = @StudentId\n\
                 AND class_room_id = @ClassRoomId\n\
                 AND _last_sortable_unique_id < @SortableUniqueId;"
            ),
            parameters: MvParamBuilder::new()
                .guid("StudentId", dropped.student_id)
                .guid("ClassRoomId", dropped.class_room_id)
                .string("SortableUniqueId", sortable_unique_id)
                .build(),
        },
        recount_classroom(tables, dropped.class_room_id, sortable_unique_id),
        recount_student(tables, dropped.student_id, sortable_unique_id),
    ]
}

fn recount_classroom(
    tables: &MvTableBindingsDto,
    class_room_id: uuid::Uuid,
    sortable_unique_id: &str,
) -> MvSqlStatementDto {
    let classrooms = tables.get_physical_name(CLASSROOMS_LOGICAL);
    let enrollments = tables.get_physical_name(ENROLLMENTS_LOGICAL);
    MvSqlStatementDto {
        sql: format!(
            "UPDATE {classrooms}\n\
             SET enrolled_count = (\n\
             SELECT COUNT(*) FROM {enrollments} WHERE class_room_id = @ClassRoomId\n\
             ),\n\
             _last_sortable_unique_id = @SortableUniqueId,\n\
             _last_applied_at = NOW()\n\
             WHERE class_room_id = @ClassRoomId\n\
             AND _last_sortable_unique_id < @SortableUniqueId;"
        ),
        parameters: MvParamBuilder::new()
            .guid("ClassRoomId", class_room_id)
            .string("SortableUniqueId", sortable_unique_id)
            .build(),
    }
}

fn recount_student(
    tables: &MvTableBindingsDto,
    student_id: uuid::Uuid,
    sortable_unique_id: &str,
) -> MvSqlStatementDto {
    let students = tables.get_physical_name(STUDENTS_LOGICAL);
    let enrollments = tables.get_physical_name(ENROLLMENTS_LOGICAL);
    MvSqlStatementDto {
        sql: format!(
            "UPDATE {students}\n\
             SET enrolled_count = (\n\
             SELECT COUNT(*) FROM {enrollments} WHERE student_id = @StudentId\n\
             ),\n\
             _last_sortable_unique_id = @SortableUniqueId,\n\
             _last_applied_at = NOW()\n\
             WHERE student_id = @StudentId\n\
             AND _last_sortable_unique_id < @SortableUniqueId;"
        ),
        parameters: MvParamBuilder::new()
            .guid("StudentId", student_id)
            .string("SortableUniqueId", sortable_unique_id)
            .build(),
    }
}

/// PostgreSQL identifier length limit is 63 bytes. Mirrors the BuildIndexName helper on the C#
/// side so the two ports produce identical DDL.
fn build_index_name(physical_table: &str, suffix: &str) -> String {
    const MAX: usize = 63;
    const PREFIX: &str = "idx_";
    let tail = format!("_{}", suffix);
    let available = MAX - PREFIX.len() - tail.len();
    if physical_table.len() <= available {
        return format!("{PREFIX}{physical_table}{tail}");
    }
    let hash_full = sha1::Sha1::digest(physical_table.as_bytes());
    let hash_hex = hex::encode(hash_full);
    let hash = &hash_hex[..8];
    let headroom = available.saturating_sub(9).max(1);
    let head = &physical_table[..physical_table.len().min(headroom)];
    format!("{PREFIX}{head}_{hash}{tail}")
}
