import Foundation

// Serializable DTOs for `/api/sekiban/serialized/commit` — the generic WasmRuntime.Host
// transport endpoint. The wire format is camelCase and matches the Rust executor's
// `CommitRequest` shape in sekiban-executor. `payload` is base64-encoded JSON bytes of the
// event payload (NOT the raw object, because Sekiban's C# side treats it as an opaque blob).
// `eventPayloadName` is the CLR-style type name the WASM apply_event dispatch key expects
// (e.g. "RoomCreated", "WeatherForecastCreated") and must match the projector's switch.

public struct SerializableCommitEventCandidate: Codable, Sendable, Equatable {
    public var payload: String
    public var eventPayloadName: String
    public var tags: [String]

    public init(payload: String, eventPayloadName: String, tags: [String]) {
        self.payload = payload
        self.eventPayloadName = eventPayloadName
        self.tags = tags
    }
}

public struct SerializableConsistencyTag: Codable, Sendable, Equatable {
    public var tag: String
    public var lastSortableUniqueId: String

    public init(tag: String, lastSortableUniqueId: String) {
        self.tag = tag
        self.lastSortableUniqueId = lastSortableUniqueId
    }
}

public struct SerializableCommitRequest: Codable, Sendable, Equatable {
    public var eventCandidates: [SerializableCommitEventCandidate]
    public var consistencyTags: [SerializableConsistencyTag]

    public init(
        eventCandidates: [SerializableCommitEventCandidate],
        consistencyTags: [SerializableConsistencyTag]
    ) {
        self.eventCandidates = eventCandidates
        self.consistencyTags = consistencyTags
    }
}

/// Canonical encoder matching the other language samples' JSON output: camelCase keys (we
/// rely on the property names already being camelCase here), no pretty-print, and sorted
/// keys so tests can compare payloads byte-for-byte.
public func makeDefaultCommitJSONEncoder() -> JSONEncoder {
    let encoder = JSONEncoder()
    encoder.outputFormatting = [.sortedKeys]
    return encoder
}

/// Build a `SerializableCommitRequest` for one event on one tag, using an empty
/// `lastSortableUniqueId` so "brand new tag" commits succeed without a prior tag-latest
/// round trip. The benchmark driver only creates fresh entity IDs so this assumption holds.
///
/// - Parameters:
///   - eventPayload: encodable struct whose JSON becomes the event payload. Must have the
///     exact field names the WASM projector's `apply_event` dispatcher expects.
///   - eventTypeName: CLR-style event type name (e.g. "RoomCreated").
///   - tag: "group:content" formatted tag string (e.g. "Room:<uuid>").
public func buildSingleEventCommit<T: Encodable>(
    eventPayload: T,
    eventTypeName: String,
    tag: String,
    encoder: JSONEncoder = makeDefaultCommitJSONEncoder()
) throws -> SerializableCommitRequest {
    let payloadData = try encoder.encode(eventPayload)
    return SerializableCommitRequest(
        eventCandidates: [
            SerializableCommitEventCandidate(
                payload: payloadData.base64EncodedString(),
                eventPayloadName: eventTypeName,
                tags: [tag])
        ],
        consistencyTags: [
            SerializableConsistencyTag(tag: tag, lastSortableUniqueId: "")
        ])
}

/// Build a `SerializableCommitRequest` for multiple events that share the same tag list,
/// used by `CreateQuickReservation` which emits draft → hold → confirm in one commit.
public func buildMultiEventCommit<T: Encodable>(
    events: [(payload: T, typeName: String)],
    tags: [String],
    encoder: JSONEncoder = makeDefaultCommitJSONEncoder()
) throws -> SerializableCommitRequest {
    let candidates: [SerializableCommitEventCandidate] = try events.map { event in
        let data = try encoder.encode(event.payload)
        return SerializableCommitEventCandidate(
            payload: data.base64EncodedString(),
            eventPayloadName: event.typeName,
            tags: tags)
    }
    let consistencyTags = tags.map {
        SerializableConsistencyTag(tag: $0, lastSortableUniqueId: "")
    }
    return SerializableCommitRequest(
        eventCandidates: candidates,
        consistencyTags: consistencyTags)
}

/// Build a `SerializableCommitRequest` where each event candidate can carry a distinct tag
/// subset (used by command flows that emit events across multiple tag groups in one commit).
public func buildHeterogeneousCommit(
    events: [SerializableCommitEventCandidate],
    consistencyTags: [String]
) -> SerializableCommitRequest {
    SerializableCommitRequest(
        eventCandidates: events,
        consistencyTags: consistencyTags.map {
            SerializableConsistencyTag(tag: $0, lastSortableUniqueId: "")
        })
}
