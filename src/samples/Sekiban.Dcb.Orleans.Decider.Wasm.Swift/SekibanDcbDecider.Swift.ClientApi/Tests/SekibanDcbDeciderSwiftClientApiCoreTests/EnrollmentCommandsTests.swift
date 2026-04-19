import XCTest
@testable import SekibanDcbDeciderSwiftClientApiCore

final class EnrollmentCommandsTests: XCTestCase {
    private func makeUUID(_ seed: String) -> UUID {
        var bytes = Array(seed.utf8)
        while bytes.count < 16 { bytes.append(0) }
        bytes = Array(bytes.prefix(16))
        bytes[6] = (bytes[6] & 0x0F) | 0x40
        bytes[8] = (bytes[8] & 0x3F) | 0x80
        let u = (
            bytes[0], bytes[1], bytes[2], bytes[3], bytes[4], bytes[5], bytes[6], bytes[7],
            bytes[8], bytes[9], bytes[10], bytes[11], bytes[12], bytes[13], bytes[14], bytes[15])
        return UUID(uuid: u)
    }

    func testBuildCreateClassRoomCommit_SingleTagSingleEvent() throws {
        let classRoomId = makeUUID("classroom-001")
        let request = CreateClassRoomRequest(
            classRoomId: classRoomId, name: "Math 101", maxStudents: 20)
        let result = try buildCreateClassRoomCommit(request: request)
        XCTAssertEqual(result.classRoomId, classRoomId)
        XCTAssertEqual(result.request.eventCandidates.count, 1)
        XCTAssertEqual(result.request.eventCandidates[0].eventPayloadName, "ClassRoomCreated")
        XCTAssertEqual(
            result.request.eventCandidates[0].tags,
            ["ClassRoom:\(classRoomId.lowercasedUUID)"])
        let payload = Data(base64Encoded: result.request.eventCandidates[0].payload)!
        let event = try JSONDecoder().decode(ClassRoomCreated.self, from: payload)
        XCTAssertEqual(event.classRoomId, classRoomId)
        XCTAssertEqual(event.name, "Math 101")
        XCTAssertEqual(event.maxStudents, 20)
    }

    func testBuildCreateStudentCommit_SingleTagSingleEvent() throws {
        let studentId = makeUUID("student-001")
        let result = try buildCreateStudentCommit(
            request: CreateStudentRequest(studentId: studentId, name: "Alice", maxClassCount: 5))
        XCTAssertEqual(result.studentId, studentId)
        XCTAssertEqual(result.request.eventCandidates.count, 1)
        XCTAssertEqual(result.request.eventCandidates[0].eventPayloadName, "StudentCreated")
        XCTAssertEqual(
            result.request.eventCandidates[0].tags,
            ["Student:\(studentId.lowercasedUUID)"])
    }

    func testBuildEnrollStudentCommit_FansOutOnStudentAndClassroomTags() async throws {
        let studentId = makeUUID("student-enroll")
        let classRoomId = makeUUID("classroom-enroll")
        let session = ScriptedTagLatestSortableSession(byTagPrefix: [
            ("Student:", (Data("{\"exists\":true,\"lastSortableUniqueId\":\"s-1\"}".utf8), 200)),
            ("ClassRoom:", (Data("{\"exists\":true,\"lastSortableUniqueId\":\"c-1\"}".utf8), 200)),
        ])
        let reader = TagLatestSortableReader(
            wasmServerUrl: "http://127.0.0.1:6299",
            session: session)

        let result = try await buildEnrollStudentCommit(
            request: EnrollmentCommandRequest(studentId: studentId, classRoomId: classRoomId),
            reader: reader)

        XCTAssertEqual(result.request.eventCandidates.count, 1)
        XCTAssertEqual(result.request.eventCandidates[0].eventPayloadName, "StudentEnrolledInClassRoom")
        let expectedStudentTag = "Student:\(studentId.lowercasedUUID)"
        let expectedClassRoomTag = "ClassRoom:\(classRoomId.lowercasedUUID)"
        XCTAssertEqual(
            Set(result.request.eventCandidates[0].tags),
            Set([expectedStudentTag, expectedClassRoomTag]))

        XCTAssertEqual(result.request.consistencyTags.count, 2)
        let byTag = Dictionary(uniqueKeysWithValues: result.request.consistencyTags.map { ($0.tag, $0.lastSortableUniqueId) })
        XCTAssertEqual(byTag[expectedStudentTag], "s-1")
        XCTAssertEqual(byTag[expectedClassRoomTag], "c-1")

        let captured = await session.recorder.recorded
        XCTAssertEqual(captured.count, 2, "expected exactly 2 tag-latest-sortable reads (Student + ClassRoom)")
    }

    func testBuildDropStudentCommit_DifferentEventNameSameFanOut() async throws {
        let studentId = makeUUID("student-drop")
        let classRoomId = makeUUID("classroom-drop")
        let session = ScriptedTagLatestSortableSession(byTagPrefix: [
            ("Student:", (Data("{\"exists\":true,\"lastSortableUniqueId\":\"s-5\"}".utf8), 200)),
            ("ClassRoom:", (Data("{\"exists\":true,\"lastSortableUniqueId\":\"c-5\"}".utf8), 200)),
        ])
        let reader = TagLatestSortableReader(
            wasmServerUrl: "http://127.0.0.1:6299",
            session: session)

        let result = try await buildDropStudentCommit(
            request: EnrollmentCommandRequest(studentId: studentId, classRoomId: classRoomId),
            reader: reader)

        XCTAssertEqual(result.request.eventCandidates[0].eventPayloadName, "StudentDroppedFromClassRoom")
        XCTAssertEqual(result.request.consistencyTags.count, 2)
    }

    func testBuildUpdateWeatherLocationCommit_OneTagOneEvent() async throws {
        let forecastId = makeUUID("forecast-upd")
        let session = ScriptedTagLatestSortableSession(byTagPrefix: [
            ("weather:", (Data("{\"exists\":true,\"lastSortableUniqueId\":\"w-99\"}".utf8), 200)),
        ])
        let reader = TagLatestSortableReader(
            wasmServerUrl: "http://127.0.0.1:6299",
            session: session)

        let commit = try await buildUpdateWeatherForecastLocationCommit(
            request: UpdateWeatherForecastLocationRequest(
                forecastId: forecastId, newLocation: "Mountain View"),
            reader: reader)
        XCTAssertEqual(commit.eventCandidates.count, 1)
        XCTAssertEqual(commit.eventCandidates[0].eventPayloadName, "WeatherForecastLocationUpdated")
        XCTAssertEqual(
            commit.eventCandidates[0].tags,
            ["weather:\(forecastId.lowercasedUUID)"])
        XCTAssertEqual(commit.consistencyTags[0].lastSortableUniqueId, "w-99")
    }

    func testBuildDeleteWeatherCommit_OneTagOneEvent() async throws {
        let forecastId = makeUUID("forecast-del")
        let session = ScriptedTagLatestSortableSession(byTagPrefix: [
            ("weather:", (Data("{\"exists\":true,\"lastSortableUniqueId\":\"w-100\"}".utf8), 200)),
        ])
        let reader = TagLatestSortableReader(
            wasmServerUrl: "http://127.0.0.1:6299",
            session: session)
        let commit = try await buildDeleteWeatherForecastCommit(
            request: DeleteWeatherForecastRequest(forecastId: forecastId),
            reader: reader)
        XCTAssertEqual(commit.eventCandidates.count, 1)
        XCTAssertEqual(commit.eventCandidates[0].eventPayloadName, "WeatherForecastDeleted")
        XCTAssertEqual(commit.consistencyTags[0].lastSortableUniqueId, "w-100")
    }
}
