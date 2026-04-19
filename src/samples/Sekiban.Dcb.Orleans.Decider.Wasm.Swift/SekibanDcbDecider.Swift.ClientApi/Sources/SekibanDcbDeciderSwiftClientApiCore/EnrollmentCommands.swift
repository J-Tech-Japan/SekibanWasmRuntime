import Foundation

// ClassRoom / Student / Enrollment / Weather update+delete command builders. Each mirrors
// the Rust handler's state-read pattern via `TagLatestSortableReader`, so the Swift
// ClientApi's writes pay the same round-trip cost as the Rust sample instead of the
// fresh-ID shortcut the benchmark originally shipped.

// ---------------------------------------------------------------------------
// Tag helpers (domain extensions)
// ---------------------------------------------------------------------------

public extension SekibanTag {
    static func classRoom(_ classRoomId: UUID) -> String {
        "ClassRoom:\(classRoomId.lowercasedUUID)"
    }

    static func student(_ studentId: UUID) -> String {
        "Student:\(studentId.lowercasedUUID)"
    }
}

// ---------------------------------------------------------------------------
// CreateClassRoom
// ---------------------------------------------------------------------------

public struct CreateClassRoomRequest: Codable, Sendable {
    public var classRoomId: UUID?
    public var name: String
    public var maxStudents: Int32

    public init(classRoomId: UUID? = nil, name: String, maxStudents: Int32) {
        self.classRoomId = classRoomId
        self.name = name
        self.maxStudents = maxStudents
    }
}

public struct ClassRoomCreated: Codable, Sendable, Equatable {
    public var classRoomId: UUID
    public var name: String
    public var maxStudents: Int32
}

public struct CreateClassRoomResult: Codable, Sendable {
    public let request: SerializableCommitRequest
    public let classRoomId: UUID
}

public func buildCreateClassRoomCommit(
    request: CreateClassRoomRequest,
    reader: TagLatestSortableReader
) async throws -> CreateClassRoomResult {
    let classRoomId = request.classRoomId ?? UUID()
    let tag = SekibanTag.classRoom(classRoomId)
    // Mirror Rust's `tag_exists` check: fetch the tag's current sortable id; for a fresh
    // ID this returns exists=false / empty. The round-trip is kept so write-side numbers
    // remain apples-to-apples with the other language samples.
    _ = try await reader.read(tag: tag)
    let event = ClassRoomCreated(
        classRoomId: classRoomId,
        name: request.name,
        maxStudents: request.maxStudents)
    let commit = try buildSingleEventCommit(
        eventPayload: event,
        eventTypeName: "ClassRoomCreated",
        tag: tag)
    return CreateClassRoomResult(request: commit, classRoomId: classRoomId)
}

// Synchronous no-reader variant for tests.
public func buildCreateClassRoomCommit(
    request: CreateClassRoomRequest
) throws -> CreateClassRoomResult {
    let classRoomId = request.classRoomId ?? UUID()
    let event = ClassRoomCreated(
        classRoomId: classRoomId,
        name: request.name,
        maxStudents: request.maxStudents)
    let commit = try buildSingleEventCommit(
        eventPayload: event,
        eventTypeName: "ClassRoomCreated",
        tag: SekibanTag.classRoom(classRoomId))
    return CreateClassRoomResult(request: commit, classRoomId: classRoomId)
}

// ---------------------------------------------------------------------------
// CreateStudent
// ---------------------------------------------------------------------------

public struct CreateStudentRequest: Codable, Sendable {
    public var studentId: UUID?
    public var name: String
    public var maxClassCount: Int32

    public init(studentId: UUID? = nil, name: String, maxClassCount: Int32) {
        self.studentId = studentId
        self.name = name
        self.maxClassCount = maxClassCount
    }
}

public struct StudentCreated: Codable, Sendable, Equatable {
    public var studentId: UUID
    public var name: String
    public var maxClassCount: Int32
}

public struct CreateStudentResult: Codable, Sendable {
    public let request: SerializableCommitRequest
    public let studentId: UUID
}

public func buildCreateStudentCommit(
    request: CreateStudentRequest,
    reader: TagLatestSortableReader
) async throws -> CreateStudentResult {
    let studentId = request.studentId ?? UUID()
    let tag = SekibanTag.student(studentId)
    _ = try await reader.read(tag: tag)
    let event = StudentCreated(
        studentId: studentId,
        name: request.name,
        maxClassCount: request.maxClassCount)
    let commit = try buildSingleEventCommit(
        eventPayload: event,
        eventTypeName: "StudentCreated",
        tag: tag)
    return CreateStudentResult(request: commit, studentId: studentId)
}

public func buildCreateStudentCommit(
    request: CreateStudentRequest
) throws -> CreateStudentResult {
    let studentId = request.studentId ?? UUID()
    let event = StudentCreated(
        studentId: studentId,
        name: request.name,
        maxClassCount: request.maxClassCount)
    let commit = try buildSingleEventCommit(
        eventPayload: event,
        eventTypeName: "StudentCreated",
        tag: SekibanTag.student(studentId))
    return CreateStudentResult(request: commit, studentId: studentId)
}

// ---------------------------------------------------------------------------
// EnrollStudent / DropStudent — both emit on Student + ClassRoom tags
// ---------------------------------------------------------------------------

public struct EnrollmentCommandRequest: Codable, Sendable {
    public var studentId: UUID
    public var classRoomId: UUID

    public init(studentId: UUID, classRoomId: UUID) {
        self.studentId = studentId
        self.classRoomId = classRoomId
    }
}

public struct StudentEnrolledInClassRoom: Codable, Sendable, Equatable {
    public var studentId: UUID
    public var classRoomId: UUID
}

public struct StudentDroppedFromClassRoom: Codable, Sendable, Equatable {
    public var studentId: UUID
    public var classRoomId: UUID
}

public struct EnrollmentResult: Codable, Sendable {
    public let request: SerializableCommitRequest
}

/// `EnrollStudentInClassRoom` writes a single event tagged on both Student and ClassRoom,
/// matching Rust's `multi_tag_output(event, vec![student_tag, classroom_tag], ...)`.
public func buildEnrollStudentCommit(
    request: EnrollmentCommandRequest,
    reader: TagLatestSortableReader
) async throws -> EnrollmentResult {
    try await buildTwoTagEnrollmentCommit(
        request: request,
        eventTypeName: "StudentEnrolledInClassRoom",
        reader: reader)
}

public func buildDropStudentCommit(
    request: EnrollmentCommandRequest,
    reader: TagLatestSortableReader
) async throws -> EnrollmentResult {
    try await buildTwoTagEnrollmentCommit(
        request: request,
        eventTypeName: "StudentDroppedFromClassRoom",
        reader: reader)
}

private func buildTwoTagEnrollmentCommit(
    request: EnrollmentCommandRequest,
    eventTypeName: String,
    reader: TagLatestSortableReader
) async throws -> EnrollmentResult {
    let studentTag = SekibanTag.student(request.studentId)
    let classRoomTag = SekibanTag.classRoom(request.classRoomId)
    async let studentState = reader.read(tag: studentTag)
    async let classRoomState = reader.read(tag: classRoomTag)
    let (student, classroom) = try await (studentState, classRoomState)

    let payload: Encodable = eventTypeName == "StudentEnrolledInClassRoom"
        ? StudentEnrolledInClassRoom(
            studentId: request.studentId, classRoomId: request.classRoomId)
        : StudentDroppedFromClassRoom(
            studentId: request.studentId, classRoomId: request.classRoomId)

    let encoder = makeDefaultCommitJSONEncoder()
    let body = try payload.encodeToData(with: encoder)
    let candidate = SerializableCommitEventCandidate(
        payload: body.base64EncodedString(),
        eventPayloadName: eventTypeName,
        tags: [studentTag, classRoomTag])
    let commit = SerializableCommitRequest(
        eventCandidates: [candidate],
        consistencyTags: [
            SerializableConsistencyTag(tag: studentTag, lastSortableUniqueId: student.lastSortableUniqueId),
            SerializableConsistencyTag(tag: classRoomTag, lastSortableUniqueId: classroom.lastSortableUniqueId),
        ])
    return EnrollmentResult(request: commit)
}

// ---------------------------------------------------------------------------
// Weather update / delete
// ---------------------------------------------------------------------------

public struct UpdateWeatherForecastLocationRequest: Codable, Sendable {
    public var forecastId: UUID
    public var newLocation: String

    public init(forecastId: UUID, newLocation: String) {
        self.forecastId = forecastId
        self.newLocation = newLocation
    }
}

public struct WeatherForecastLocationUpdated: Codable, Sendable, Equatable {
    public var forecastId: UUID
    public var newLocation: String
    public var updatedAt: String
}

public struct DeleteWeatherForecastRequest: Codable, Sendable {
    public var forecastId: UUID

    public init(forecastId: UUID) { self.forecastId = forecastId }
}

public struct WeatherForecastDeleted: Codable, Sendable, Equatable {
    public var forecastId: UUID
    public var deletedAt: String
}

public func buildUpdateWeatherForecastLocationCommit(
    request: UpdateWeatherForecastLocationRequest,
    reader: TagLatestSortableReader,
    now: () -> Date = Date.init
) async throws -> SerializableCommitRequest {
    let tag = SekibanTag.weatherForecast(request.forecastId)
    let state = try await reader.read(tag: tag)
    let event = WeatherForecastLocationUpdated(
        forecastId: request.forecastId,
        newLocation: request.newLocation,
        updatedAt: SekibanTime.iso8601(from: now()))
    let encoder = makeDefaultCommitJSONEncoder()
    let body = try encoder.encode(event)
    return SerializableCommitRequest(
        eventCandidates: [
            SerializableCommitEventCandidate(
                payload: body.base64EncodedString(),
                eventPayloadName: "WeatherForecastLocationUpdated",
                tags: [tag]),
        ],
        consistencyTags: [
            SerializableConsistencyTag(tag: tag, lastSortableUniqueId: state.lastSortableUniqueId),
        ])
}

public func buildDeleteWeatherForecastCommit(
    request: DeleteWeatherForecastRequest,
    reader: TagLatestSortableReader,
    now: () -> Date = Date.init
) async throws -> SerializableCommitRequest {
    let tag = SekibanTag.weatherForecast(request.forecastId)
    let state = try await reader.read(tag: tag)
    let event = WeatherForecastDeleted(
        forecastId: request.forecastId,
        deletedAt: SekibanTime.iso8601(from: now()))
    let encoder = makeDefaultCommitJSONEncoder()
    let body = try encoder.encode(event)
    return SerializableCommitRequest(
        eventCandidates: [
            SerializableCommitEventCandidate(
                payload: body.base64EncodedString(),
                eventPayloadName: "WeatherForecastDeleted",
                tags: [tag]),
        ],
        consistencyTags: [
            SerializableConsistencyTag(tag: tag, lastSortableUniqueId: state.lastSortableUniqueId),
        ])
}

// ---------------------------------------------------------------------------
// Encodable helper (erases generic for heterogeneous dispatch above)
// ---------------------------------------------------------------------------

extension Encodable {
    fileprivate func encodeToData(with encoder: JSONEncoder) throws -> Data {
        try encoder.encode(self)
    }
}
