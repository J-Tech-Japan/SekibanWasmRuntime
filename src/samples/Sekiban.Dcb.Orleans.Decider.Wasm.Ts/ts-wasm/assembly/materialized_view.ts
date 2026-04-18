import { JSON } from "json-as/assembly";

const VIEW_NAME = "ClassRoomEnrollment";
const VIEW_VERSION: i32 = 1;
const CLASSROOMS_LOGICAL = "classrooms";
const STUDENTS_LOGICAL = "students";
const ENROLLMENTS_LOGICAL = "enrollments";

const PARAM_KIND_STRING: i32 = 1;
const PARAM_KIND_INT32: i32 = 2;
const PARAM_KIND_GUID: i32 = 5;

const _mvPinned: usize[] = [];

function readStr(ptr: u32, len: u32): string {
  return String.UTF8.decodeUnsafe(ptr as usize, len as i32);
}

function writeStr(value: string): u64 {
  const buf = String.UTF8.encode(value);
  const p = changetype<usize>(buf);
  __pin(p);
  _mvPinned.push(p);
  return (u64(p) << 32) | u64(buf.byteLength);
}

@json
class WasmMvParam {
  name: string = "";
  kind: i32 = 0;
  valueJson: string | null = null;
}

@json
class WasmMvSqlStatementDto {
  sql: string = "";
  parameters: WasmMvParam[] = [];
}

@json
class WasmMvTableBindingEntry {
  logical: string = "";
  physical: string = "";
}

@json
class WasmMvTableBindingsDto {
  bindings: WasmMvTableBindingEntry[] = [];
}

@json
class WasmMvMetadataDto {
  viewName: string = "";
  viewVersion: i32 = 0;
  logicalTables: string[] = [];
}

@json
class WasmMvSerializableEventDto {
  eventType: string = "";
  payloadJson: string = "";
  sortableUniqueId: string = "";
  tags: string[] = [];
}

@json
class WasmMvStatementBatchDto {
  statements: WasmMvSqlStatementDto[] = [];
}

@json
class ClassRoomCreatedEv {
  classRoomId: string = "";
  name: string = "";
  maxStudents: i32 = 0;
}

@json
class StudentCreatedEv {
  studentId: string = "";
  name: string = "";
  maxClassCount: i32 = 0;
}

@json
class StudentEnrolledEv {
  studentId: string = "";
  classRoomId: string = "";
}

@json
class StudentDroppedEv {
  studentId: string = "";
  classRoomId: string = "";
}

function errorPayload(message: string): string {
  return '{"error":"' + message + '"}';
}

function statementBatchPayload(statements: WasmMvSqlStatementDto[]): string {
  const batch = new WasmMvStatementBatchDto();
  batch.statements = statements;
  return JSON.stringify<WasmMvStatementBatchDto>(batch);
}

function tableName(bindings: WasmMvTableBindingsDto, logical: string): string {
  for (let i = 0; i < bindings.bindings.length; i++) {
    if (bindings.bindings[i].logical == logical) {
      return bindings.bindings[i].physical;
    }
  }
  throw new Error("Missing physical table binding for logical table: " + logical);
  return logical;
}

function sqlParam(name: string, kind: i32, valueJson: string): WasmMvParam {
  const param = new WasmMvParam();
  param.name = name;
  param.kind = kind;
  param.valueJson = valueJson;
  return param;
}

function guidParam(name: string, value: string): WasmMvParam {
  return sqlParam(name, PARAM_KIND_GUID, '"' + value + '"');
}

function stringParam(name: string, value: string): WasmMvParam {
  return sqlParam(name, PARAM_KIND_STRING, '"' + value + '"');
}

function int32Param(name: string, value: i32): WasmMvParam {
  return sqlParam(name, PARAM_KIND_INT32, value.toString());
}

function statement(sql: string, parameters: WasmMvParam[] = []): WasmMvSqlStatementDto {
  const dto = new WasmMvSqlStatementDto();
  dto.sql = sql;
  dto.parameters = parameters;
  return dto;
}

function buildIndexName(physicalTable: string, suffix: string): string {
  const maxIdentifierLength = 63;
  const prefix = "idx_";
  const tail = "_" + suffix;
  const available = maxIdentifierLength - prefix.length - tail.length;
  if (physicalTable.length <= available) {
    return prefix + physicalTable + tail;
  }
  return prefix + physicalTable.substring(0, available) + tail;
}

function initializeStatements(bindings: WasmMvTableBindingsDto): WasmMvSqlStatementDto[] {
  const classrooms = tableName(bindings, CLASSROOMS_LOGICAL);
  const students = tableName(bindings, STUDENTS_LOGICAL);
  const enrollments = tableName(bindings, ENROLLMENTS_LOGICAL);
  const idx = buildIndexName(enrollments, "class_room");
  return [
    statement(
      "CREATE TABLE IF NOT EXISTS " + classrooms +
      " (class_room_id UUID PRIMARY KEY, name TEXT NOT NULL, max_students INT NOT NULL, enrolled_count INT NOT NULL DEFAULT 0, _last_sortable_unique_id TEXT NOT NULL, _last_applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW());"
    ),
    statement(
      "CREATE TABLE IF NOT EXISTS " + students +
      " (student_id UUID PRIMARY KEY, name TEXT NOT NULL, max_class_count INT NOT NULL, enrolled_count INT NOT NULL DEFAULT 0, _last_sortable_unique_id TEXT NOT NULL, _last_applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW());"
    ),
    statement(
      "CREATE TABLE IF NOT EXISTS " + enrollments +
      " (student_id UUID NOT NULL, class_room_id UUID NOT NULL, enrolled_at TIMESTAMPTZ NOT NULL DEFAULT NOW(), _last_sortable_unique_id TEXT NOT NULL, PRIMARY KEY (student_id, class_room_id));"
    ),
    statement("CREATE INDEX IF NOT EXISTS " + idx + " ON " + enrollments + " (class_room_id);"),
  ];
}

function recountClassroom(bindings: WasmMvTableBindingsDto, classRoomId: string, sortableUniqueId: string): WasmMvSqlStatementDto {
  const classrooms = tableName(bindings, CLASSROOMS_LOGICAL);
  const enrollments = tableName(bindings, ENROLLMENTS_LOGICAL);
  return statement(
    "UPDATE " + classrooms +
    " SET enrolled_count = (SELECT COUNT(*) FROM " + enrollments + " WHERE class_room_id = @ClassRoomId), _last_sortable_unique_id = @SortableUniqueId, _last_applied_at = NOW() WHERE class_room_id = @ClassRoomId AND _last_sortable_unique_id < @SortableUniqueId;",
    [guidParam("ClassRoomId", classRoomId), stringParam("SortableUniqueId", sortableUniqueId)],
  );
}

function recountStudent(bindings: WasmMvTableBindingsDto, studentId: string, sortableUniqueId: string): WasmMvSqlStatementDto {
  const students = tableName(bindings, STUDENTS_LOGICAL);
  const enrollments = tableName(bindings, ENROLLMENTS_LOGICAL);
  return statement(
    "UPDATE " + students +
    " SET enrolled_count = (SELECT COUNT(*) FROM " + enrollments + " WHERE student_id = @StudentId), _last_sortable_unique_id = @SortableUniqueId, _last_applied_at = NOW() WHERE student_id = @StudentId AND _last_sortable_unique_id < @SortableUniqueId;",
    [guidParam("StudentId", studentId), stringParam("SortableUniqueId", sortableUniqueId)],
  );
}

function insertClassroom(bindings: WasmMvTableBindingsDto, payload: ClassRoomCreatedEv, sortableUniqueId: string): WasmMvSqlStatementDto[] {
  const table = tableName(bindings, CLASSROOMS_LOGICAL);
  return [
    statement(
      "INSERT INTO " + table +
      " (class_room_id, name, max_students, enrolled_count, _last_sortable_unique_id, _last_applied_at) VALUES (@ClassRoomId, @Name, @MaxStudents, 0, @SortableUniqueId, NOW()) ON CONFLICT (class_room_id) DO UPDATE SET name = EXCLUDED.name, max_students = EXCLUDED.max_students, _last_sortable_unique_id = EXCLUDED._last_sortable_unique_id, _last_applied_at = EXCLUDED._last_applied_at WHERE " + table + "._last_sortable_unique_id < EXCLUDED._last_sortable_unique_id;",
      [
        guidParam("ClassRoomId", payload.classRoomId),
        stringParam("Name", payload.name),
        int32Param("MaxStudents", payload.maxStudents),
        stringParam("SortableUniqueId", sortableUniqueId),
      ],
    ),
  ];
}

function insertStudent(bindings: WasmMvTableBindingsDto, payload: StudentCreatedEv, sortableUniqueId: string): WasmMvSqlStatementDto[] {
  const table = tableName(bindings, STUDENTS_LOGICAL);
  return [
    statement(
      "INSERT INTO " + table +
      " (student_id, name, max_class_count, enrolled_count, _last_sortable_unique_id, _last_applied_at) VALUES (@StudentId, @Name, @MaxClassCount, 0, @SortableUniqueId, NOW()) ON CONFLICT (student_id) DO UPDATE SET name = EXCLUDED.name, max_class_count = EXCLUDED.max_class_count, _last_sortable_unique_id = EXCLUDED._last_sortable_unique_id, _last_applied_at = EXCLUDED._last_applied_at WHERE " + table + "._last_sortable_unique_id < EXCLUDED._last_sortable_unique_id;",
      [
        guidParam("StudentId", payload.studentId),
        stringParam("Name", payload.name),
        int32Param("MaxClassCount", payload.maxClassCount),
        stringParam("SortableUniqueId", sortableUniqueId),
      ],
    ),
  ];
}

function insertEnrollment(bindings: WasmMvTableBindingsDto, payload: StudentEnrolledEv, sortableUniqueId: string): WasmMvSqlStatementDto[] {
  const enrollments = tableName(bindings, ENROLLMENTS_LOGICAL);
  return [
    statement(
      "INSERT INTO " + enrollments +
      " (student_id, class_room_id, enrolled_at, _last_sortable_unique_id) VALUES (@StudentId, @ClassRoomId, NOW(), @SortableUniqueId) ON CONFLICT (student_id, class_room_id) DO UPDATE SET _last_sortable_unique_id = EXCLUDED._last_sortable_unique_id WHERE " + enrollments + "._last_sortable_unique_id < EXCLUDED._last_sortable_unique_id;",
      [
        guidParam("StudentId", payload.studentId),
        guidParam("ClassRoomId", payload.classRoomId),
        stringParam("SortableUniqueId", sortableUniqueId),
      ],
    ),
    recountClassroom(bindings, payload.classRoomId, sortableUniqueId),
    recountStudent(bindings, payload.studentId, sortableUniqueId),
  ];
}

function deleteEnrollment(bindings: WasmMvTableBindingsDto, payload: StudentDroppedEv, sortableUniqueId: string): WasmMvSqlStatementDto[] {
  const enrollments = tableName(bindings, ENROLLMENTS_LOGICAL);
  return [
    statement(
      "DELETE FROM " + enrollments +
      " WHERE student_id = @StudentId AND class_room_id = @ClassRoomId AND _last_sortable_unique_id < @SortableUniqueId;",
      [
        guidParam("StudentId", payload.studentId),
        guidParam("ClassRoomId", payload.classRoomId),
        stringParam("SortableUniqueId", sortableUniqueId),
      ],
    ),
    recountClassroom(bindings, payload.classRoomId, sortableUniqueId),
    recountStudent(bindings, payload.studentId, sortableUniqueId),
  ];
}

function applyEventStatements(bindings: WasmMvTableBindingsDto, serializableEvent: WasmMvSerializableEventDto): WasmMvSqlStatementDto[] {
  const eventType = serializableEvent.eventType;
  const sortableUniqueId = serializableEvent.sortableUniqueId;
  if (eventType == "ClassRoomCreated") {
    return insertClassroom(bindings, JSON.parse<ClassRoomCreatedEv>(serializableEvent.payloadJson), sortableUniqueId);
  }
  if (eventType == "StudentCreated") {
    return insertStudent(bindings, JSON.parse<StudentCreatedEv>(serializableEvent.payloadJson), sortableUniqueId);
  }
  if (eventType == "StudentEnrolledInClassRoom") {
    return insertEnrollment(bindings, JSON.parse<StudentEnrolledEv>(serializableEvent.payloadJson), sortableUniqueId);
  }
  if (eventType == "StudentDroppedFromClassRoom") {
    return deleteEnrollment(bindings, JSON.parse<StudentDroppedEv>(serializableEvent.payloadJson), sortableUniqueId);
  }
  return [];
}

export function mv_metadata(): u64 {
  const metadata = new Array<WasmMvMetadataDto>();
  const dto = new WasmMvMetadataDto();
  dto.viewName = VIEW_NAME;
  dto.viewVersion = VIEW_VERSION;
  dto.logicalTables = [CLASSROOMS_LOGICAL, STUDENTS_LOGICAL, ENROLLMENTS_LOGICAL];
  metadata.push(dto);
  return writeStr(JSON.stringify<WasmMvMetadataDto[]>(metadata));
}

export function mv_initialize(
  viewNamePtr: u32,
  viewNameLen: u32,
  viewVersion: i32,
  bindingsPtr: u32,
  bindingsLen: u32,
): u64 {
  const viewName = readStr(viewNamePtr, viewNameLen);
  if (viewName != VIEW_NAME || viewVersion != VIEW_VERSION) {
    return writeStr(errorPayload("unknown materialized view: " + viewName));
  }

  const bindings = JSON.parse<WasmMvTableBindingsDto>(readStr(bindingsPtr, bindingsLen));
  return writeStr(statementBatchPayload(initializeStatements(bindings)));
}

export function mv_apply_event(
  viewNamePtr: u32,
  viewNameLen: u32,
  viewVersion: i32,
  bindingsPtr: u32,
  bindingsLen: u32,
  serializableEventPtr: u32,
  serializableEventLen: u32,
): u64 {
  const viewName = readStr(viewNamePtr, viewNameLen);
  if (viewName != VIEW_NAME || viewVersion != VIEW_VERSION) {
    return writeStr(errorPayload("unknown materialized view: " + viewName));
  }

  const bindings = JSON.parse<WasmMvTableBindingsDto>(readStr(bindingsPtr, bindingsLen));
  const serializableEvent = JSON.parse<WasmMvSerializableEventDto>(readStr(serializableEventPtr, serializableEventLen));
  return writeStr(statementBatchPayload(applyEventStatements(bindings, serializableEvent)));
}
