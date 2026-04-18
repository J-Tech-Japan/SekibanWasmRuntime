import XCTest
@testable import SekibanDcbDeciderSwiftClientApiCore

/// Captures each POST the reader sends. Two modes:
///   * `byTagPrefix` returns scripted responses keyed by the tag string's prefix, which
///     is robust to parallel `async let` races.
///   * `sequential` pops from an ordered list (used by the single-request tests).
actor TagLatestSortableSessionRecorder {
    struct Recorded {
        let url: URL
        let body: Data
        let contentType: String
    }

    private(set) var recorded: [Recorded] = []
    private var sequential: [(Data, Int)]
    private let byTagPrefix: [(String, (Data, Int))]

    init(sequential: [(Data, Int)] = [], byTagPrefix: [(String, (Data, Int))] = []) {
        self.sequential = sequential
        self.byTagPrefix = byTagPrefix
    }

    func record(url: URL, body: Data, contentType: String) -> (Data, Int) {
        recorded.append(Recorded(url: url, body: body, contentType: contentType))
        if !byTagPrefix.isEmpty,
           let decoded = try? JSONDecoder().decode([String: String].self, from: body),
           let tag = decoded["tag"],
           let match = byTagPrefix.first(where: { tag.hasPrefix($0.0) })
        {
            return match.1
        }
        if !sequential.isEmpty { return sequential.removeFirst() }
        return (Data("{\"exists\":false,\"lastSortableUniqueId\":\"\"}".utf8), 200)
    }
}

struct ScriptedTagLatestSortableSession: TagLatestSortableSession {
    let recorder: TagLatestSortableSessionRecorder

    init(sequential: [(Data, Int)] = [], byTagPrefix: [(String, (Data, Int))] = []) {
        self.recorder = TagLatestSortableSessionRecorder(
            sequential: sequential,
            byTagPrefix: byTagPrefix)
    }

    func post(url: URL, body: Data, contentType: String) async throws -> (Data, Int) {
        await recorder.record(url: url, body: body, contentType: contentType)
    }
}

final class TagLatestSortableReaderTests: XCTestCase {
    func testReadParsesExistsAndSortableId() async throws {
        let session = ScriptedTagLatestSortableSession(sequential: [
            (Data("{\"exists\":true,\"lastSortableUniqueId\":\"su-42\"}".utf8), 200)
        ])
        let reader = TagLatestSortableReader(
            wasmServerUrl: "http://127.0.0.1:6299",
            session: session)

        let result = try await reader.read(tag: "Room:abc")
        XCTAssertTrue(result.exists)
        XCTAssertEqual(result.lastSortableUniqueId, "su-42")

        let captured = await session.recorder.recorded
        XCTAssertEqual(captured.count, 1)
        XCTAssertEqual(
            captured[0].url.absoluteString,
            "http://127.0.0.1:6299/api/sekiban/serialized/tag-latest-sortable")
        let body = try JSONDecoder().decode([String: String].self, from: captured[0].body)
        XCTAssertEqual(body["tag"], "Room:abc")
    }

    func testReadFallsBackToEmptyOnUnexpectedBody() async throws {
        let session = ScriptedTagLatestSortableSession(sequential: [
            (Data("<not json>".utf8), 200)
        ])
        let reader = TagLatestSortableReader(
            wasmServerUrl: "http://127.0.0.1:6299",
            session: session)
        let result = try await reader.read(tag: "Room:xyz")
        XCTAssertFalse(result.exists)
        XCTAssertEqual(result.lastSortableUniqueId, "")
    }

    func testReadSurfacesHttpErrors() async {
        let session = ScriptedTagLatestSortableSession(sequential: [
            (Data("{\"error\":\"nope\"}".utf8), 500)
        ])
        let reader = TagLatestSortableReader(
            wasmServerUrl: "http://127.0.0.1:6299",
            session: session)
        do {
            _ = try await reader.read(tag: "Room:abc")
            XCTFail("expected httpStatus error")
        } catch TagLatestSortableError.httpStatus(let code, _) {
            XCTAssertEqual(code, 500)
        } catch {
            XCTFail("unexpected error: \(error)")
        }
    }

    func testAsyncQuickReservationBuilderPerformsThreeReads() async throws {
        // Match responses by tag prefix so the test doesn't rely on parallel-async-let
        // request ordering (which is non-deterministic across runs).
        let session = ScriptedTagLatestSortableSession(byTagPrefix: [
            ("Reservation:", (Data("{\"exists\":false,\"lastSortableUniqueId\":\"\"}".utf8), 200)),
            ("Room:", (Data("{\"exists\":true,\"lastSortableUniqueId\":\"room-su\"}".utf8), 200)),
            ("RoomReservation:", (Data("{\"exists\":true,\"lastSortableUniqueId\":\"rr-su\"}".utf8), 200)),
        ])
        let reader = TagLatestSortableReader(
            wasmServerUrl: "http://127.0.0.1:6299",
            session: session)

        let request = CreateQuickReservationRequest(
            reservationId: UUID(uuidString: "00000000-0000-4000-8000-000000000001")!,
            roomId: UUID(uuidString: "00000000-0000-4000-8000-000000000002")!,
            organizerId: UUID(uuidString: "00000000-0000-4000-8000-000000000003")!,
            organizerName: "Bench User",
            startTime: "2026-05-03T09:00:00Z",
            endTime: "2026-05-03T10:00:00Z",
            attendeeCount: nil,
            purpose: "Benchmark",
            selectedEquipment: [])

        let result = try await buildCreateQuickReservationCommit(request: request, reader: reader)
        XCTAssertEqual(result.request.eventCandidates.count, 3)
        XCTAssertEqual(result.request.consistencyTags.count, 2)

        // Two tags in consistencyTags: Reservation:<id> has empty sortable id (always new);
        // RoomReservation:<roomId> carries the `rr-su` we scripted.
        let consistencyByTag = Dictionary(
            uniqueKeysWithValues: result.request.consistencyTags.map { ($0.tag, $0.lastSortableUniqueId) })
        let reservationTag = "Reservation:\(result.reservationId.lowercasedUUID)"
        let roomReservationTag = "RoomReservation:\(request.roomId.lowercasedUUID)"
        XCTAssertEqual(consistencyByTag[reservationTag], "")
        XCTAssertEqual(consistencyByTag[roomReservationTag], "rr-su")

        let captured = await session.recorder.recorded
        XCTAssertEqual(captured.count, 3, "expected exactly 3 tag-latest-sortable reads")
        let tags = try captured
            .map { try JSONDecoder().decode([String: String].self, from: $0.body) }
            .map { $0["tag"] ?? "" }
        // Order isn't important — they're launched concurrently — but all three tags
        // must be present.
        XCTAssertEqual(Set(tags), Set([
            reservationTag,
            "Room:\(request.roomId.lowercasedUUID)",
            roomReservationTag,
        ]))
    }
}
