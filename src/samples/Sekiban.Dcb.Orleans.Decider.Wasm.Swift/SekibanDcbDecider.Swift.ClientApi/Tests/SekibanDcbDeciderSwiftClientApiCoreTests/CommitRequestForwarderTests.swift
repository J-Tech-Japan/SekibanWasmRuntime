import XCTest
@testable import SekibanDcbDeciderSwiftClientApiCore

/// Captures the last POST so the test can assert the path, body, and content-type the
/// forwarder sent — the important invariants for interop with the generic WasmRuntime.Host.
/// Uses an actor to stay async-safe under Swift 6 strict concurrency; tests invoke `record`
/// via `await` and read the captured state the same way.
actor CommitHTTPSessionRecorder {
    struct Recorded {
        let url: URL
        let body: Data
        let contentType: String
    }

    private(set) var recorded: Recorded?
    private let response: (Data, Int)

    init(response: (Data, Int) = (Data("{\"success\":true}".utf8), 200)) {
        self.response = response
    }

    func record(url: URL, body: Data, contentType: String) -> (Data, Int) {
        recorded = Recorded(url: url, body: body, contentType: contentType)
        return response
    }
}

struct RecordingCommitHTTPSession: CommitHTTPSession {
    let recorder: CommitHTTPSessionRecorder

    init(response: (Data, Int) = (Data("{\"success\":true}".utf8), 200)) {
        self.recorder = CommitHTTPSessionRecorder(response: response)
    }

    func post(url: URL, body: Data, contentType: String) async throws -> (Data, Int) {
        await recorder.record(url: url, body: body, contentType: contentType)
    }
}

final class CommitRequestForwarderTests: XCTestCase {
    func testForwardPostsJsonToCommitPath() async throws {
        let session = RecordingCommitHTTPSession()
        let forwarder = CommitRequestForwarder(
            wasmServerUrl: "http://127.0.0.1:6299",
            session: session)

        let request = SerializableCommitRequest(
            eventCandidates: [
                SerializableCommitEventCandidate(
                    payload: "eyJmb28iOiAiYmFyIn0=",
                    eventPayloadName: "TestEvent",
                    tags: ["test:42"]),
            ],
            consistencyTags: [
                SerializableConsistencyTag(tag: "test:42", lastSortableUniqueId: ""),
            ])

        let result = try await forwarder.forward(request)
        XCTAssertTrue(result.isSuccess)
        XCTAssertEqual(result.statusCode, 200)

        let captured = await session.recorder.recorded
        let recorded = try XCTUnwrap(captured)
        XCTAssertEqual(recorded.url.absoluteString,
                       "http://127.0.0.1:6299/api/sekiban/serialized/commit")
        XCTAssertEqual(recorded.contentType, "application/json")

        // Decode the body back into a SerializableCommitRequest to verify the wire shape.
        let decoded = try JSONDecoder().decode(SerializableCommitRequest.self, from: recorded.body)
        XCTAssertEqual(decoded, request)
    }

    func testForwardHandlesTrailingSlashInBaseUrl() async throws {
        let session = RecordingCommitHTTPSession()
        let forwarder = CommitRequestForwarder(
            wasmServerUrl: "http://127.0.0.1:6299/",
            session: session)
        _ = try await forwarder.forward(SerializableCommitRequest(
            eventCandidates: [],
            consistencyTags: []))

        let captured = await session.recorder.recorded
        let recorded = try XCTUnwrap(captured)
        XCTAssertEqual(recorded.url.absoluteString,
                       "http://127.0.0.1:6299/api/sekiban/serialized/commit",
                       "trailing slash must not produce a double-slash in the commit path")
    }

    func testForwardSurfacesSortableUniqueIdFromWrittenEvents() async throws {
        let body = Data("""
            {"success":true,"writtenEvents":[{"eventId":"e1","sortableUniqueId":"su-123"}]}
            """.utf8)
        let session = RecordingCommitHTTPSession(response: (body, 200))
        let forwarder = CommitRequestForwarder(
            wasmServerUrl: "http://127.0.0.1:6299",
            session: session)
        let result = try await forwarder.forward(SerializableCommitRequest(
            eventCandidates: [],
            consistencyTags: []))
        XCTAssertEqual(result.sortableUniqueId, "su-123")
    }

    func testForwardRejectsInvalidUrl() async {
        // Control-character + space combo forces `URL(string:)` to return nil on Swift 6.x.
        let session = RecordingCommitHTTPSession()
        let forwarder = CommitRequestForwarder(wasmServerUrl: "http://\u{0001}bad host", session: session)
        do {
            _ = try await forwarder.forward(SerializableCommitRequest(
                eventCandidates: [],
                consistencyTags: []))
            XCTFail("expected invalidWasmServerUrl error")
        } catch CommitForwardError.invalidWasmServerUrl {
            // ok
        } catch {
            XCTFail("unexpected error: \(error)")
        }
    }
}
