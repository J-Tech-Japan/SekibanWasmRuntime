import { Pool } from "pg";

const VIEW_NAME = "ClassRoomEnrollment";
const VIEW_VERSION = 1;
const CLASSROOMS_LOGICAL = "classrooms";
const STUDENTS_LOGICAL = "students";
const ENROLLMENTS_LOGICAL = "enrollments";

export interface MaterializedViewState {
  pool: Pool | null;
}

export interface PagingOptions {
  pageNumber?: number | null;
  pageSize?: number | null;
}

export interface EnrollmentFilters extends PagingOptions {
  studentId?: string | null;
  classRoomId?: string | null;
}

interface RegistryRow {
  service_id: string;
  view_name: string;
  view_version: number;
  logical_table: string;
  physical_table: string;
  status: string;
  applied_event_version: string | null;
  current_position: string | null;
  last_catch_up_sortable_unique_id: string | null;
  last_updated: string | Date | null;
}

interface StudentRow {
  student_id: string;
  name: string;
  max_class_count: number;
  enrolled_class_room_ids: string[] | null;
}

interface ClassroomRow {
  class_room_id: string;
  name: string;
  max_students: number;
  enrolled_count: number;
}

interface EnrollmentRow {
  student_id: string;
  student_name: string;
  grade: number;
  class_room_id: string;
  class_name: string;
  enrollment_date: string | Date;
}

export async function createMaterializedViewState(logger = console): Promise<MaterializedViewState> {
  const connectionString = resolveConnectionUrl();
  if (!connectionString) {
    logger.warn?.(
      "materialized view: no DCBMATERIALIZEDVIEWPOSTGRES_URI / ConnectionStrings__DcbMaterializedViewPostgres env var; /api/mv/* will return 503",
    );
    return { pool: null };
  }

  const pool = new Pool({
    connectionString,
    max: 4,
    idleTimeoutMillis: 10_000,
    connectionTimeoutMillis: 5_000,
  });

  try {
    await pool.query("select 1");
    return { pool };
  } catch (error) {
    try {
      await pool.end();
    } catch {
      // Preserve original connection failure.
    }
    throw error;
  }
}

export async function getStatus(state: MaterializedViewState) {
  const pool = requirePool(state);
  const serviceId = currentServiceId();
  const { rows } = await pool.query<RegistryRow>(
    `select service_id, view_name, view_version, logical_table, physical_table, status,
            applied_event_version, current_position, last_catch_up_sortable_unique_id, last_updated
       from sekiban_mv_registry
      where service_id = $1 and view_name = $2 and view_version = $3
      order by logical_table`,
    [serviceId, VIEW_NAME, VIEW_VERSION],
  );

  return {
    serviceId,
    viewName: VIEW_NAME,
    viewVersion: VIEW_VERSION,
    entries: rows.map((row) => ({
      serviceId: row.service_id,
      viewName: row.view_name,
      viewVersion: row.view_version,
      logicalTable: row.logical_table,
      physicalTable: row.physical_table,
      status: row.status,
      appliedEventVersion: String(row.applied_event_version ?? "0"),
      currentPosition: row.current_position,
      lastCatchUpSortableUniqueId: row.last_catch_up_sortable_unique_id,
      lastUpdated: row.last_updated,
    })),
  };
}

export async function listStudents(state: MaterializedViewState, options: PagingOptions = {}) {
  const pool = requirePool(state);
  const studentsTable = await physicalTable(pool, STUDENTS_LOGICAL);
  const enrollmentsTable = await physicalTable(pool, ENROLLMENTS_LOGICAL);
  const { limit, offset } = resolvePaging(options);
  const pagination = buildPaginationClause(limit, offset, 1);
  const sql = `
    select
      s.student_id,
      s.name,
      s.max_class_count,
      coalesce(
        array_agg(e.class_room_id order by e.class_room_id)
          filter (where e.class_room_id is not null),
        array[]::uuid[]
      ) as enrolled_class_room_ids
    from ${quoteIdentifier(studentsTable)} s
    left join ${quoteIdentifier(enrollmentsTable)} e on e.student_id = s.student_id
    group by s.student_id, s.name, s.max_class_count
    order by s.name
    ${pagination.clause}`;
  const { rows } = await pool.query<StudentRow>(sql, pagination.parameters);
  return rows.map((row) => ({
    studentId: row.student_id,
    name: row.name,
    maxClassCount: row.max_class_count,
    enrolledClassRoomIds: row.enrolled_class_room_ids ?? [],
  }));
}

export async function listClassrooms(state: MaterializedViewState, options: PagingOptions = {}) {
  const pool = requirePool(state);
  const classroomsTable = await physicalTable(pool, CLASSROOMS_LOGICAL);
  const { limit, offset } = resolvePaging(options);
  const pagination = buildPaginationClause(limit, offset, 1);
  const sql = `
    select class_room_id, name, max_students, enrolled_count
      from ${quoteIdentifier(classroomsTable)}
     order by name
     ${pagination.clause}`;
  const { rows } = await pool.query<ClassroomRow>(sql, pagination.parameters);
  return rows.map((row) => ({
    classRoomId: row.class_room_id,
    name: row.name,
    maxStudents: row.max_students,
    enrolledCount: row.enrolled_count,
    remainingCapacity: Math.max(0, row.max_students - row.enrolled_count),
    isFull: row.max_students > 0 && row.enrolled_count >= row.max_students,
  }));
}

export async function listEnrollments(state: MaterializedViewState, options: EnrollmentFilters = {}) {
  const pool = requirePool(state);
  const studentsTable = await physicalTable(pool, STUDENTS_LOGICAL);
  const classroomsTable = await physicalTable(pool, CLASSROOMS_LOGICAL);
  const enrollmentsTable = await physicalTable(pool, ENROLLMENTS_LOGICAL);
  const { limit, offset } = resolvePaging(options);
  const predicates: string[] = [];
  const parameters: Array<string | number> = [];

  if (options.studentId) {
    parameters.push(options.studentId);
    predicates.push(`e.student_id = $${parameters.length}`);
  }
  if (options.classRoomId) {
    parameters.push(options.classRoomId);
    predicates.push(`e.class_room_id = $${parameters.length}`);
  }

  const pagination = buildPaginationClause(limit, offset, parameters.length + 1);
  const whereClause = predicates.length > 0 ? `where ${predicates.join(" and ")}` : "";
  const sql = `
    select
      e.student_id,
      s.name as student_name,
      s.max_class_count as grade,
      e.class_room_id,
      c.name as class_name,
      e.enrolled_at as enrollment_date
    from ${quoteIdentifier(enrollmentsTable)} e
    inner join ${quoteIdentifier(studentsTable)} s on s.student_id = e.student_id
    inner join ${quoteIdentifier(classroomsTable)} c on c.class_room_id = e.class_room_id
    ${whereClause}
    order by s.name, c.name
    ${pagination.clause}`;
  const { rows } = await pool.query<EnrollmentRow>(sql, [...parameters, ...pagination.parameters]);
  return rows.map((row) => ({
    studentId: row.student_id,
    studentName: row.student_name,
    grade: row.grade,
    classRoomId: row.class_room_id,
    className: row.class_name,
    enrollmentDate: row.enrollment_date,
  }));
}

async function physicalTable(pool: Pool, logicalTable: string): Promise<string> {
  const serviceId = currentServiceId();
  const { rows } = await pool.query<{ physical_table: string }>(
    `select physical_table
       from sekiban_mv_registry
      where service_id = $1 and view_name = $2 and view_version = $3 and logical_table = $4
      limit 1`,
    [serviceId, VIEW_NAME, VIEW_VERSION, logicalTable],
  );
  if (rows.length === 0) {
    throw materializedViewError(
      404,
      "MaterializedViewNotReady",
      `materialized view '${logicalTable}' is not registered for ${serviceId}/${VIEW_NAME}/${VIEW_VERSION}`,
    );
  }
  return rows[0].physical_table as string;
}

function resolveConnectionUrl() {
  const direct = process.env.DCBMATERIALIZEDVIEWPOSTGRES_URI;
  if (direct && direct.trim() !== "") {
    return direct;
  }

  const raw = process.env.ConnectionStrings__DcbMaterializedViewPostgres;
  if (!raw || raw.trim() === "") {
    return null;
  }

  const parts = new Map<string, string>();
  for (const pair of raw.split(";")) {
    if (!pair || !pair.includes("=")) {
      continue;
    }
    const [key, value] = pair.split("=");
    parts.set(key.trim().toLowerCase(), value.trim());
  }

  const host = parts.get("host") ?? parts.get("server");
  const database = parts.get("database") ?? parts.get("db");
  if (!host || !database) {
    return null;
  }

  const port = parts.get("port") ?? "5432";
  const username = parts.get("username") ?? parts.get("user id") ?? parts.get("uid") ?? "postgres";
  const password = parts.get("password") ?? parts.get("pwd") ?? "";
  return `postgresql://${encodeURIComponent(username)}:${encodeURIComponent(password)}@${host}:${port}/${database}`;
}

function resolvePaging(options: PagingOptions) {
  const hasPageSize = Number.isInteger(options.pageSize) && Number(options.pageSize) > 0;
  const hasPageNumber = Number.isInteger(options.pageNumber) && Number(options.pageNumber) > 0;
  if (!hasPageSize && !hasPageNumber) {
    return { limit: null, offset: 0 };
  }

  const limit = hasPageSize ? Number(options.pageSize) : 20;
  const page = hasPageNumber ? Number(options.pageNumber) : 1;
  return { limit, offset: (page - 1) * limit };
}

function buildPaginationClause(limit: number | null, offset: number, startIndex: number) {
  if (limit === null) {
    return { clause: "", parameters: [] as number[] };
  }

  return {
    clause: `limit $${startIndex} offset $${startIndex + 1}`,
    parameters: [limit, offset],
  };
}

function requirePool(state: MaterializedViewState) {
  if (state.pool) {
    return state.pool;
  }

  throw materializedViewError(
    503,
    "MaterializedViewDisabled",
    "DcbMaterializedViewPostgres is not configured for the TypeScript ClientApi",
  );
}

function currentServiceId() {
  const value = process.env.SEKIBAN_SERVICE_ID;
  return value && value.trim() !== "" ? value : "default";
}

function quoteIdentifier(identifier: string) {
  if (!/^[A-Za-z_][A-Za-z0-9_]*$/.test(identifier)) {
    throw materializedViewError(
      500,
      "MaterializedViewIdentifierInvalid",
      `registry returned an unsafe physical table name: ${identifier}`,
    );
  }
  return `"${identifier}"`;
}

function materializedViewError(status: number, code: string, message: string) {
  const error = new Error(message) as Error & { status: number; code: string };
  error.status = status;
  error.code = code;
  return error;
}
