import Foundation
import HTTPTypes
import Hummingbird
import Logging
import PostgresNIO

// /api/mv/* read endpoints. Physical table names are resolved against `sekiban_mv_registry`
// per request, scoped by service_id (env `SEKIBAN_SERVICE_ID`, defaulting to `"default"`) so
// multi-tenant deployments don't cross-contaminate reads. Direction matches the Rust sample.

let viewName = "ClassRoomEnrollment"
let viewVersion: Int32 = 1
let classRoomsLogical = "classrooms"
let studentsLogical = "students"
let enrollmentsLogical = "enrollments"

func currentServiceId() -> String {
    if let s = ProcessInfo.processInfo.environment["SEKIBAN_SERVICE_ID"],
       !s.trimmingCharacters(in: .whitespaces).isEmpty {
        return s
    }
    return "default"
}

struct MvAppContext: Sendable {
    let postgres: PostgresClient?
    let wasmServerUrl: String
    let logger: Logger
}

func registerMvRoutes(_ router: Router<BasicRequestContext>, context: MvAppContext) {
    router.get("/api/mv/status") { [context] _, _ in
        guard let client = context.postgres else { return mvDisabled() }
        return try await fetchStatus(client: client, logger: context.logger)
    }
    router.get("/api/mv/classrooms") { [context] request, _ in
        guard let client = context.postgres else { return mvDisabled() }
        let (limit, offset) = paging(from: request)
        return try await fetchClassrooms(client: client, limit: limit, offset: offset,
                                         logger: context.logger)
    }
    router.get("/api/mv/students") { [context] request, _ in
        guard let client = context.postgres else { return mvDisabled() }
        let (limit, offset) = paging(from: request)
        return try await fetchStudents(client: client, limit: limit, offset: offset,
                                       logger: context.logger)
    }
    router.get("/api/mv/enrollments") { [context] request, _ in
        guard let client = context.postgres else { return mvDisabled() }
        let (limit, offset) = paging(from: request)
        let studentIdParam = request.uri.queryParameters["student_id"] ?? request.uri.queryParameters["studentId"]
        let classRoomIdParam = request.uri.queryParameters["class_room_id"] ?? request.uri.queryParameters["classRoomId"]
        let studentId = studentIdParam.flatMap { UUID(uuidString: String($0)) }
        let classRoomId = classRoomIdParam.flatMap { UUID(uuidString: String($0)) }
        return try await fetchEnrollments(client: client, limit: limit, offset: offset,
                                          studentId: studentId, classRoomId: classRoomId,
                                          logger: context.logger)
    }
}

// ---------------------------------------------------------------------------
// Paging helpers
// ---------------------------------------------------------------------------

private func paging(from request: Request) -> (Int, Int) {
    let sizeRaw = request.uri.queryParameters["page_size"] ?? request.uri.queryParameters["pageSize"]
    let pageRaw = request.uri.queryParameters["page_number"] ?? request.uri.queryParameters["pageNumber"]
    let size = sizeRaw.flatMap { Int($0) }.map { $0 > 0 ? $0 : 20 } ?? 20
    let page = pageRaw.flatMap { Int($0) }.map { $0 > 0 ? $0 : 1 } ?? 1
    return (size, (page - 1) * size)
}

private func mvDisabled() -> Response {
    var buffer = ByteBuffer()
    buffer.writeString("""
        {"error":"Materialized view Postgres is not configured. Set DCBMATERIALIZEDVIEWPOSTGRES_URI or ConnectionStrings__DcbMaterializedViewPostgres."}
        """)
    return Response(
        status: .serviceUnavailable,
        headers: [.contentType: "application/json"],
        body: ResponseBody(byteBuffer: buffer))
}

// ---------------------------------------------------------------------------
// Registry + queries
// ---------------------------------------------------------------------------

private func physicalTable(
    client: PostgresClient,
    logical: String,
    logger: Logger
) async throws -> String {
    let serviceId = currentServiceId()
    let sql: PostgresQuery = """
        SELECT physical_table
        FROM sekiban_mv_registry
        WHERE service_id = \(serviceId) AND view_name = \(viewName)
          AND view_version = \(viewVersion) AND logical_table = \(logical)
        LIMIT 1
        """
    let rows = try await client.query(sql, logger: logger)
    for try await row in rows {
        let (physical): (String) = try row.decode((String).self)
        return physical
    }
    throw MvQueryError.notRegistered(logical: logical, serviceId: serviceId)
}

enum MvQueryError: Error, CustomStringConvertible {
    case notRegistered(logical: String, serviceId: String)
    var description: String {
        switch self {
        case let .notRegistered(logical, serviceId):
            return "materialized view '\(logical)' not registered for \(serviceId)/\(viewName)/\(viewVersion)"
        }
    }
}

// --- status ------------------------------------------------------------

struct StatusEntry: Codable {
    let serviceId: String
    let viewName: String
    let viewVersion: Int32
    let logicalTable: String
    let physicalTable: String
    let status: Int32
    let appliedEventVersion: Int64
    let currentPosition: String?
    let lastCatchUpSortableUniqueId: String?
    let lastUpdated: String?

    enum CodingKeys: String, CodingKey {
        case serviceId = "service_id"
        case viewName = "view_name"
        case viewVersion = "view_version"
        case logicalTable = "logical_table"
        case physicalTable = "physical_table"
        case status
        case appliedEventVersion = "applied_event_version"
        case currentPosition = "current_position"
        case lastCatchUpSortableUniqueId = "last_catch_up_sortable_unique_id"
        case lastUpdated = "last_updated"
    }
}

struct StatusResponse: Codable {
    let serviceId: String
    let viewName: String
    let viewVersion: Int32
    let entries: [StatusEntry]

    enum CodingKeys: String, CodingKey {
        case serviceId = "service_id"
        case viewName = "view_name"
        case viewVersion = "view_version"
        case entries
    }
}

private func fetchStatus(client: PostgresClient, logger: Logger) async throws -> Response {
    let serviceId = currentServiceId()
    let sql: PostgresQuery = """
        SELECT service_id, view_name, view_version, logical_table, physical_table, status,
               applied_event_version, current_position, last_catch_up_sortable_unique_id,
               last_updated
        FROM sekiban_mv_registry
        WHERE service_id = \(serviceId) AND view_name = \(viewName)
          AND view_version = \(viewVersion)
        ORDER BY logical_table
        """
    var entries: [StatusEntry] = []
    let rows = try await client.query(sql, logger: logger)
    for try await row in rows {
        let decoded = try row.decode(
            (String, String, Int32, String, String, Int32, Int64, String?, String?, Date?).self)
        let dateFmt = ISO8601DateFormatter()
        entries.append(StatusEntry(
            serviceId: decoded.0,
            viewName: decoded.1,
            viewVersion: decoded.2,
            logicalTable: decoded.3,
            physicalTable: decoded.4,
            status: decoded.5,
            appliedEventVersion: decoded.6,
            currentPosition: decoded.7,
            lastCatchUpSortableUniqueId: decoded.8,
            lastUpdated: decoded.9.map { dateFmt.string(from: $0) }))
    }
    let response = StatusResponse(
        serviceId: serviceId,
        viewName: viewName,
        viewVersion: viewVersion,
        entries: entries)
    return try jsonResponse(response)
}

// --- classrooms --------------------------------------------------------

struct ClassRoomMvRow: Codable {
    let classRoomId: UUID
    let name: String
    let maxStudents: Int32
    let enrolledCount: Int32
    let lastSortableUniqueId: String
    let lastAppliedAt: String

    enum CodingKeys: String, CodingKey {
        case classRoomId = "class_room_id"
        case name
        case maxStudents = "max_students"
        case enrolledCount = "enrolled_count"
        case lastSortableUniqueId = "last_sortable_unique_id"
        case lastAppliedAt = "last_applied_at"
    }
}

private func fetchClassrooms(
    client: PostgresClient,
    limit: Int, offset: Int,
    logger: Logger
) async throws -> Response {
    let table = try await physicalTable(client: client, logical: classRoomsLogical,
                                        logger: logger)
    // Physical name is controlled by Sekiban itself (written into sekiban_mv_registry); safe to
    // interpolate into the SQL string. Dynamic column/name parameters in PostgresNIO are
    // verbose otherwise.
    let sqlText = """
        SELECT class_room_id, name, max_students, enrolled_count,
               _last_sortable_unique_id, _last_applied_at
        FROM "\(table)"
        ORDER BY name
        LIMIT \(limit) OFFSET \(offset)
        """
    let rows = try await client.query(PostgresQuery(unsafeSQL: sqlText), logger: logger)
    var result: [ClassRoomMvRow] = []
    let fmt = ISO8601DateFormatter()
    for try await row in rows {
        let tuple = try row.decode((UUID, String, Int32, Int32, String, Date).self)
        result.append(ClassRoomMvRow(
            classRoomId: tuple.0,
            name: tuple.1,
            maxStudents: tuple.2,
            enrolledCount: tuple.3,
            lastSortableUniqueId: tuple.4,
            lastAppliedAt: fmt.string(from: tuple.5)))
    }
    return try jsonResponse(result)
}

// --- students ----------------------------------------------------------

struct StudentMvRow: Codable {
    let studentId: UUID
    let name: String
    let maxClassCount: Int32
    let enrolledCount: Int32
    let lastSortableUniqueId: String
    let lastAppliedAt: String

    enum CodingKeys: String, CodingKey {
        case studentId = "student_id"
        case name
        case maxClassCount = "max_class_count"
        case enrolledCount = "enrolled_count"
        case lastSortableUniqueId = "last_sortable_unique_id"
        case lastAppliedAt = "last_applied_at"
    }
}

private func fetchStudents(
    client: PostgresClient,
    limit: Int, offset: Int,
    logger: Logger
) async throws -> Response {
    let table = try await physicalTable(client: client, logical: studentsLogical,
                                        logger: logger)
    let sqlText = """
        SELECT student_id, name, max_class_count, enrolled_count,
               _last_sortable_unique_id, _last_applied_at
        FROM "\(table)"
        ORDER BY name
        LIMIT \(limit) OFFSET \(offset)
        """
    let rows = try await client.query(PostgresQuery(unsafeSQL: sqlText), logger: logger)
    var result: [StudentMvRow] = []
    let fmt = ISO8601DateFormatter()
    for try await row in rows {
        let tuple = try row.decode((UUID, String, Int32, Int32, String, Date).self)
        result.append(StudentMvRow(
            studentId: tuple.0,
            name: tuple.1,
            maxClassCount: tuple.2,
            enrolledCount: tuple.3,
            lastSortableUniqueId: tuple.4,
            lastAppliedAt: fmt.string(from: tuple.5)))
    }
    return try jsonResponse(result)
}

// --- enrollments -------------------------------------------------------

struct EnrollmentMvRow: Codable {
    let studentId: UUID
    let classRoomId: UUID
    let enrolledAt: String
    let lastSortableUniqueId: String

    enum CodingKeys: String, CodingKey {
        case studentId = "student_id"
        case classRoomId = "class_room_id"
        case enrolledAt = "enrolled_at"
        case lastSortableUniqueId = "last_sortable_unique_id"
    }
}

private func fetchEnrollments(
    client: PostgresClient,
    limit: Int, offset: Int,
    studentId: UUID?,
    classRoomId: UUID?,
    logger: Logger
) async throws -> Response {
    let table = try await physicalTable(client: client, logical: enrollmentsLogical,
                                        logger: logger)
    var sqlText = """
        SELECT student_id, class_room_id, enrolled_at, _last_sortable_unique_id
        FROM "\(table)"
        WHERE 1=1
        """
    if let sid = studentId {
        sqlText += " AND student_id = '\(sid.uuidString.lowercased())'::uuid"
    }
    if let cid = classRoomId {
        sqlText += " AND class_room_id = '\(cid.uuidString.lowercased())'::uuid"
    }
    sqlText += " ORDER BY enrolled_at DESC LIMIT \(limit) OFFSET \(offset)"

    let rows = try await client.query(PostgresQuery(unsafeSQL: sqlText), logger: logger)
    var result: [EnrollmentMvRow] = []
    let fmt = ISO8601DateFormatter()
    for try await row in rows {
        let tuple = try row.decode((UUID, UUID, Date, String).self)
        result.append(EnrollmentMvRow(
            studentId: tuple.0,
            classRoomId: tuple.1,
            enrolledAt: fmt.string(from: tuple.2),
            lastSortableUniqueId: tuple.3))
    }
    return try jsonResponse(result)
}

// ---------------------------------------------------------------------------
// JSON helper
// ---------------------------------------------------------------------------

func jsonResponse<T: Encodable>(_ value: T, status: HTTPResponse.Status = .ok) throws -> Response {
    let encoder = JSONEncoder()
    encoder.dateEncodingStrategy = .iso8601
    encoder.outputFormatting = []
    let data = try encoder.encode(value)
    var buffer = ByteBuffer()
    buffer.writeBytes(data)
    return Response(
        status: status,
        headers: [.contentType: "application/json"],
        body: ResponseBody(byteBuffer: buffer))
}
