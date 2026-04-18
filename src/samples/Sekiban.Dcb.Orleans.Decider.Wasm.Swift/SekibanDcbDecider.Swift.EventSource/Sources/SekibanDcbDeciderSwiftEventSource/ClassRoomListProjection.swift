import Foundation
import SekibanWasm

/// In-memory multi-projection tracking every classroom plus its enrolled student count.
/// Plays the same role as the Rust `ClassRoomListProjection` — gives the Swift sample a
/// MultiProjection read path so the benchmark harness can compare Swift's memory mode with
/// every other language.
///
/// Aligned with the `ClassRoomEnrollmentMvV1` SQL so the two sources produce the same
/// shape: every classroom row exposes `classRoomId`, `name`, `maxStudents`, and a live
/// `enrolledCount` driven by the enroll/drop events.
///
/// Stored as a class (reference semantics) so the instance the `PrimitiveInstanceManager`
/// holds is the same instance `applyEvent` mutates — a value-type projector would be copied
/// on every dispatch and lose state between events.
public final class ClassRoomListProjection: MultiProjection {
    public static var projectorName: String { "ClassRoomListProjection" }

    /// Ordered by classroom id so `executeListQuery` output is deterministic.
    private var classrooms: [String: ClassRoomListItem] = [:]

    public init() {}

    public func applyEvent(eventType: String, payload: String, tags: [String]) {
        _ = tags
        guard let data = payload.data(using: .utf8) else { return }
        switch eventType {
        case "ClassRoomCreated":
            if let created = try? JSONDecoder().decode(ClassRoomCreated.self, from: data) {
                let key = created.classRoomId.uuidString.lowercased()
                classrooms[key] = ClassRoomListItem(
                    classRoomId: key,
                    name: created.name,
                    maxStudents: created.maxStudents,
                    enrolledCount: 0)
            }
        case "StudentEnrolledInClassRoom":
            if let enrolled = try? JSONDecoder().decode(StudentEnrolledInClassRoom.self, from: data) {
                let key = enrolled.classRoomId.uuidString.lowercased()
                if var item = classrooms[key] {
                    item.enrolledCount += 1
                    classrooms[key] = item
                }
            }
        case "StudentDroppedFromClassRoom":
            if let dropped = try? JSONDecoder().decode(StudentDroppedFromClassRoom.self, from: data) {
                let key = dropped.classRoomId.uuidString.lowercased()
                if var item = classrooms[key] {
                    item.enrolledCount = max(0, item.enrolledCount - 1)
                    classrooms[key] = item
                }
            }
        default:
            break
        }
    }

    public func serializeState() -> String {
        let snapshot = PersistedState(classrooms: classrooms)
        guard let data = try? JSONEncoder().encode(snapshot),
              let json = String(data: data, encoding: .utf8)
        else { return "{}" }
        return json
    }

    public func restoreState(_ json: String) {
        let trimmed = json.trimmingCharacters(in: .whitespacesAndNewlines)
        if trimmed.isEmpty || trimmed == "{}" {
            classrooms = [:]
            return
        }
        guard let data = trimmed.data(using: .utf8),
              let snapshot = try? JSONDecoder().decode(PersistedState.self, from: data)
        else {
            classrooms = [:]
            return
        }
        classrooms = snapshot.classrooms
    }

    public func executeQuery(type: String, params: String) -> String {
        _ = params
        switch type {
        case "GetClassRoomCountQuery":
            let countPayload = CountResult(count: classrooms.count)
            if let data = try? JSONEncoder().encode(countPayload),
               let json = String(data: data, encoding: .utf8) {
                return json
            }
            return "{\"count\":\(classrooms.count)}"
        default:
            return "{}"
        }
    }

    public func executeListQuery(type: String, params: String) -> String {
        // The host's MultiProjectionGrain wraps our return value into a
        // `SerializedListQueryResponse.ItemsJson`; Rust's reference projector returns just a
        // JSON array of items, so the Swift projector mirrors that rather than emitting its
        // own `{items:…}` envelope. (Wrapping twice would produce `{items:[{items:[…]}]}`.)
        _ = params // future: parse JSON for filter/paging controls
        guard type == "GetClassRoomListQuery" else { return "[]" }
        let items = classrooms.values
            .sorted { lhs, rhs in lhs.classRoomId < rhs.classRoomId }
        if let data = try? JSONEncoder().encode(Array(items)),
           let json = String(data: data, encoding: .utf8) {
            return json
        }
        return "[]"
    }

    // ------------------------------------------------------------------
    // Serialization helpers
    // ------------------------------------------------------------------

    private struct PersistedState: Codable {
        var classrooms: [String: ClassRoomListItem]
    }
}

public struct ClassRoomListItem: Codable, Sendable {
    public var classRoomId: String
    public var name: String
    public var maxStudents: Int32
    public var enrolledCount: Int32

    public init(classRoomId: String, name: String, maxStudents: Int32, enrolledCount: Int32) {
        self.classRoomId = classRoomId
        self.name = name
        self.maxStudents = maxStudents
        self.enrolledCount = enrolledCount
    }
}

public struct CountResult: Codable, Sendable {
    public var count: Int
    public init(count: Int) { self.count = count }
}
