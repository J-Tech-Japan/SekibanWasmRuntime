let mv_view_name = "ClassRoomEnrollment"
let mv_view_version = 1
let mv_classrooms_logical = "classrooms"
let mv_students_logical = "students"
let mv_enrollments_logical = "enrollments"

let mv_kind_string = 1
let mv_kind_int32 = 2
let mv_kind_guid = 5

struct WasmMvParam {
  name : String
  kind : Int
  valueJson : String?
} derive(ToJson, FromJson, Show, Eq)

struct WasmMvSqlStatementDto {
  sql : String
  parameters : Array[WasmMvParam]
} derive(ToJson, FromJson, Show, Eq)

struct WasmMvTableBindingEntry {
  logical : String
  physical : String
} derive(ToJson, FromJson, Show, Eq)

struct WasmMvTableBindingsDto {
  bindings : Array[WasmMvTableBindingEntry]
} derive(ToJson, FromJson, Show, Eq)

struct WasmMvMetadataDto {
  viewName : String
  viewVersion : Int
  logicalTables : Array[String]
} derive(ToJson, FromJson, Show, Eq)

struct WasmMvSerializableEventDto {
  eventType : String
  payloadJson : String
  sortableUniqueId : String
  tags : Array[String]
} derive(ToJson, FromJson, Show, Eq)

struct WasmMvStatementBatchDto {
  statements : Array[WasmMvSqlStatementDto]
} derive(ToJson, FromJson, Show, Eq)

fn error_payload(message : String) -> String {
  let payload : Json = { "error": message.to_json() }
  @core.json_stringify(payload)
}

fn statement_batch_payload(statements : Array[WasmMvSqlStatementDto]) -> String {
  @core.json_stringify(WasmMvStatementBatchDto::{ statements }.to_json())
}

fn table_name(bindings : WasmMvTableBindingsDto, logical : String) -> String {
  for binding in bindings.bindings {
    if binding.logical == logical {
      return binding.physical
    }
  }
  logical
}

fn sql_param(name : String, kind : Int, value_json : String) -> WasmMvParam {
  { name, kind, valueJson: Some(value_json) }
}

fn guid_param(name : String, value : String) -> WasmMvParam {
  sql_param(name, mv_kind_guid, @core.json_stringify(value.to_json()))
}

fn string_param(name : String, value : String) -> WasmMvParam {
  sql_param(name, mv_kind_string, @core.json_stringify(value.to_json()))
}

fn int32_param(name : String, value : Int) -> WasmMvParam {
  sql_param(name, mv_kind_int32, @core.json_stringify(value.to_json()))
}

fn statement(sql : String, parameters : Array[WasmMvParam]) -> WasmMvSqlStatementDto {
  { sql, parameters }
}

fn initialize_statements(bindings : WasmMvTableBindingsDto) -> Array[WasmMvSqlStatementDto] {
  let classrooms = table_name(bindings, mv_classrooms_logical)
  let students = table_name(bindings, mv_students_logical)
  let enrollments = table_name(bindings, mv_enrollments_logical)
  let statements : Array[WasmMvSqlStatementDto] = []
  statements.push(statement("CREATE TABLE IF NOT EXISTS " + classrooms + " (class_room_id UUID PRIMARY KEY, name TEXT NOT NULL, max_students INT NOT NULL, enrolled_count INT NOT NULL DEFAULT 0, _last_sortable_unique_id TEXT NOT NULL, _last_applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW());", []))
  statements.push(statement("CREATE TABLE IF NOT EXISTS " + students + " (student_id UUID PRIMARY KEY, name TEXT NOT NULL, max_class_count INT NOT NULL, enrolled_count INT NOT NULL DEFAULT 0, _last_sortable_unique_id TEXT NOT NULL, _last_applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW());", []))
  statements.push(statement("CREATE TABLE IF NOT EXISTS " + enrollments + " (student_id UUID NOT NULL, class_room_id UUID NOT NULL, enrolled_at TIMESTAMPTZ NOT NULL DEFAULT NOW(), _last_sortable_unique_id TEXT NOT NULL, PRIMARY KEY (student_id, class_room_id));", []))
  statements.push(statement("CREATE INDEX IF NOT EXISTS mv_class_room_enrollment_class_room_idx ON " + enrollments + " (class_room_id);", []))
  statements
}

fn recount_classroom(bindings : WasmMvTableBindingsDto, class_room_id : String, sortable_unique_id : String) -> WasmMvSqlStatementDto {
  let classrooms = table_name(bindings, mv_classrooms_logical)
  let enrollments = table_name(bindings, mv_enrollments_logical)
  statement(
    "UPDATE " + classrooms + " SET enrolled_count = (SELECT COUNT(*) FROM " + enrollments + " WHERE class_room_id = @ClassRoomId), _last_sortable_unique_id = @SortableUniqueId, _last_applied_at = NOW() WHERE class_room_id = @ClassRoomId AND _last_sortable_unique_id < @SortableUniqueId;",
    [guid_param("ClassRoomId", class_room_id), string_param("SortableUniqueId", sortable_unique_id)],
  )
}

fn recount_student(bindings : WasmMvTableBindingsDto, student_id : String, sortable_unique_id : String) -> WasmMvSqlStatementDto {
  let students = table_name(bindings, mv_students_logical)
  let enrollments = table_name(bindings, mv_enrollments_logical)
  statement(
    "UPDATE " + students + " SET enrolled_count = (SELECT COUNT(*) FROM " + enrollments + " WHERE student_id = @StudentId), _last_sortable_unique_id = @SortableUniqueId, _last_applied_at = NOW() WHERE student_id = @StudentId AND _last_sortable_unique_id < @SortableUniqueId;",
    [guid_param("StudentId", student_id), string_param("SortableUniqueId", sortable_unique_id)],
  )
}

fn insert_classroom(bindings : WasmMvTableBindingsDto, payload : ClassRoomCreated, sortable_unique_id : String) -> Array[WasmMvSqlStatementDto] {
  let table = table_name(bindings, mv_classrooms_logical)
  [
    statement(
      "INSERT INTO " + table + " (class_room_id, name, max_students, enrolled_count, _last_sortable_unique_id, _last_applied_at) VALUES (@ClassRoomId, @Name, @MaxStudents, 0, @SortableUniqueId, NOW()) ON CONFLICT (class_room_id) DO UPDATE SET name = EXCLUDED.name, max_students = EXCLUDED.max_students, _last_sortable_unique_id = EXCLUDED._last_sortable_unique_id, _last_applied_at = EXCLUDED._last_applied_at WHERE " + table + "._last_sortable_unique_id < EXCLUDED._last_sortable_unique_id;",
      [
        guid_param("ClassRoomId", payload.classRoomId),
        string_param("Name", payload.name),
        int32_param("MaxStudents", payload.maxStudents),
        string_param("SortableUniqueId", sortable_unique_id),
      ],
    ),
  ]
}

fn insert_student(bindings : WasmMvTableBindingsDto, payload : StudentCreated, sortable_unique_id : String) -> Array[WasmMvSqlStatementDto] {
  let table = table_name(bindings, mv_students_logical)
  [
    statement(
      "INSERT INTO " + table + " (student_id, name, max_class_count, enrolled_count, _last_sortable_unique_id, _last_applied_at) VALUES (@StudentId, @Name, @MaxClassCount, 0, @SortableUniqueId, NOW()) ON CONFLICT (student_id) DO UPDATE SET name = EXCLUDED.name, max_class_count = EXCLUDED.max_class_count, _last_sortable_unique_id = EXCLUDED._last_sortable_unique_id, _last_applied_at = EXCLUDED._last_applied_at WHERE " + table + "._last_sortable_unique_id < EXCLUDED._last_sortable_unique_id;",
      [
        guid_param("StudentId", payload.studentId),
        string_param("Name", payload.name),
        int32_param("MaxClassCount", payload.maxClassCount),
        string_param("SortableUniqueId", sortable_unique_id),
      ],
    ),
  ]
}

fn insert_enrollment(bindings : WasmMvTableBindingsDto, payload : StudentEnrolledInClassRoom, sortable_unique_id : String) -> Array[WasmMvSqlStatementDto] {
  let enrollments = table_name(bindings, mv_enrollments_logical)
  [
    statement(
      "INSERT INTO " + enrollments + " (student_id, class_room_id, enrolled_at, _last_sortable_unique_id) VALUES (@StudentId, @ClassRoomId, NOW(), @SortableUniqueId) ON CONFLICT (student_id, class_room_id) DO UPDATE SET _last_sortable_unique_id = EXCLUDED._last_sortable_unique_id WHERE " + enrollments + "._last_sortable_unique_id < EXCLUDED._last_sortable_unique_id;",
      [
        guid_param("StudentId", payload.studentId),
        guid_param("ClassRoomId", payload.classRoomId),
        string_param("SortableUniqueId", sortable_unique_id),
      ],
    ),
    recount_classroom(bindings, payload.classRoomId, sortable_unique_id),
    recount_student(bindings, payload.studentId, sortable_unique_id),
  ]
}

fn delete_enrollment(bindings : WasmMvTableBindingsDto, payload : StudentDroppedFromClassRoom, sortable_unique_id : String) -> Array[WasmMvSqlStatementDto] {
  let enrollments = table_name(bindings, mv_enrollments_logical)
  [
    statement(
      "DELETE FROM " + enrollments + " WHERE student_id = @StudentId AND class_room_id = @ClassRoomId AND _last_sortable_unique_id < @SortableUniqueId;",
      [
        guid_param("StudentId", payload.studentId),
        guid_param("ClassRoomId", payload.classRoomId),
        string_param("SortableUniqueId", sortable_unique_id),
      ],
    ),
    recount_classroom(bindings, payload.classRoomId, sortable_unique_id),
    recount_student(bindings, payload.studentId, sortable_unique_id),
  ]
}

fn apply_event_statements(bindings : WasmMvTableBindingsDto, serializable_event : WasmMvSerializableEventDto) -> Array[WasmMvSqlStatementDto] {
  let event_type = serializable_event.eventType
  let sortable_unique_id = serializable_event.sortableUniqueId
  if event_type == event_classroom_created {
    let payload = parse_json(serializable_event.payloadJson, ClassRoomCreated::{ classRoomId: "", name: "", maxStudents: 0 })
    return insert_classroom(bindings, payload, sortable_unique_id)
  }
  if event_type == event_student_created {
    let payload = parse_json(serializable_event.payloadJson, StudentCreated::{ studentId: "", name: "", maxClassCount: 0 })
    return insert_student(bindings, payload, sortable_unique_id)
  }
  if event_type == event_student_enrolled {
    let payload = parse_json(serializable_event.payloadJson, StudentEnrolledInClassRoom::{ studentId: "", classRoomId: "" })
    return insert_enrollment(bindings, payload, sortable_unique_id)
  }
  if event_type == event_student_dropped {
    let payload = parse_json(serializable_event.payloadJson, StudentDroppedFromClassRoom::{ studentId: "", classRoomId: "" })
    return delete_enrollment(bindings, payload, sortable_unique_id)
  }
  []
}

fn materialized_view_metadata() -> Int64 {
  let metadata : Array[WasmMvMetadataDto] = [
    WasmMvMetadataDto::{
      viewName: mv_view_name,
      viewVersion: mv_view_version,
      logicalTables: [mv_classrooms_logical, mv_students_logical, mv_enrollments_logical],
    },
  ]
  @core.write_utf8(@core.json_stringify(metadata.to_json()))
}

fn materialized_view_initialize(
  view_name_ptr : UInt,
  view_name_len : UInt,
  view_version : Int,
  bindings_ptr : UInt,
  bindings_len : UInt
) -> Int64 {
  let view_name = @core.read_utf8(view_name_ptr.reinterpret_as_int(), view_name_len.reinterpret_as_int())
  if view_name != mv_view_name || view_version != mv_view_version {
    return @core.write_utf8(error_payload("unknown materialized view: " + view_name))
  }
  let bindings_json = @core.read_utf8(bindings_ptr.reinterpret_as_int(), bindings_len.reinterpret_as_int())
  let bindings = parse_json(bindings_json, WasmMvTableBindingsDto::{ bindings: [] })
  @core.write_utf8(statement_batch_payload(initialize_statements(bindings)))
}

fn materialized_view_apply_event(
  view_name_ptr : UInt,
  view_name_len : UInt,
  view_version : Int,
  bindings_ptr : UInt,
  bindings_len : UInt,
  event_ptr : UInt,
  event_len : UInt
) -> Int64 {
  let view_name = @core.read_utf8(view_name_ptr.reinterpret_as_int(), view_name_len.reinterpret_as_int())
  if view_name != mv_view_name || view_version != mv_view_version {
    return @core.write_utf8(error_payload("unknown materialized view: " + view_name))
  }
  let bindings_json = @core.read_utf8(bindings_ptr.reinterpret_as_int(), bindings_len.reinterpret_as_int())
  let event_json = @core.read_utf8(event_ptr.reinterpret_as_int(), event_len.reinterpret_as_int())
  let bindings = parse_json(bindings_json, WasmMvTableBindingsDto::{ bindings: [] })
  let serializable_event = parse_json(
    event_json,
    WasmMvSerializableEventDto::{ eventType: "", payloadJson: "", sortableUniqueId: "", tags: [] },
  )
  @core.write_utf8(statement_batch_payload(apply_event_statements(bindings, serializable_event)))
}
