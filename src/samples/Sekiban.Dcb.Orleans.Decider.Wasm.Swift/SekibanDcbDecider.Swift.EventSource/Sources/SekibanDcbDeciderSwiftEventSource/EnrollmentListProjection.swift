import Foundation
import SekibanWasm

/// In-memory multi-projection for classroom enrollments. Produces the same shape the
/// template's `/api/enrollments` endpoint returns:
///   `{"studentId": "...", "classRoomId": "...", "enrolledAt": "..."}`
/// Keyed by `studentId|classRoomId` so `StudentEnrolledInClassRoom` creates / overwrites
/// and `StudentDroppedFromClassRoom` removes. Supports filter-by-studentId and
/// filter-by-classRoomId via `executeListQuery` params.
public final class EnrollmentListProjection: MultiProjection {
    public static var projectorName: String { "EnrollmentListProjection" }

    private var enrollments: [String: EnrollmentListItem] = [:]

    public init() {}

    private static func key(studentId: String, classRoomId: String) -> String {
        "\(studentId)|\(classRoomId)"
    }

    public func applyEvent(eventType: String, payload: String, tags: [String]) {
        _ = tags
        guard let data = payload.data(using: .utf8) else { return }
        switch eventType {
        case "StudentEnrolledInClassRoom":
            if let enrolled = try? JSONDecoder().decode(StudentEnrolledInClassRoom.self, from: data) {
                let studentKey = enrolled.studentId.uuidString.lowercased()
                let classKey = enrolled.classRoomId.uuidString.lowercased()
                let key = Self.key(studentId: studentKey, classRoomId: classKey)
                enrollments[key] = EnrollmentListItem(
                    studentId: studentKey,
                    classRoomId: classKey,
                    enrolledAt: ISO8601DateFormatter().string(from: Date()))
            }
        case "StudentDroppedFromClassRoom":
            if let dropped = try? JSONDecoder().decode(StudentDroppedFromClassRoom.self, from: data) {
                let key = Self.key(
                    studentId: dropped.studentId.uuidString.lowercased(),
                    classRoomId: dropped.classRoomId.uuidString.lowercased())
                enrollments.removeValue(forKey: key)
            }
        default:
            break
        }
    }

    public func serializeState() -> String {
        let snapshot = PersistedState(enrollments: enrollments)
        guard let data = try? JSONEncoder().encode(snapshot),
              let json = String(data: data, encoding: .utf8)
        else { return "{}" }
        return json
    }

    public func restoreState(_ json: String) {
        let trimmed = json.trimmingCharacters(in: .whitespacesAndNewlines)
        if trimmed.isEmpty || trimmed == "{}" {
            enrollments = [:]
            return
        }
        guard let data = trimmed.data(using: .utf8),
              let snapshot = try? JSONDecoder().decode(PersistedState.self, from: data)
        else {
            enrollments = [:]
            return
        }
        enrollments = snapshot.enrollments
    }

    public func executeQuery(type: String, params: String) -> String {
        _ = params
        switch type {
        case "GetEnrollmentCountQuery":
            return encodeOrDefault(CountResult(count: enrollments.count),
                                   fallback: "{\"count\":\(enrollments.count)}")
        default:
            return "{}"
        }
    }

    public func executeListQuery(type: String, params: String) -> String {
        guard type == "GetEnrollmentListQuery" else { return "[]" }

        let (studentFilter, classRoomFilter) = parseFilters(from: params)
        var items = Array(enrollments.values)
        if let studentFilter {
            items.removeAll { $0.studentId != studentFilter }
        }
        if let classRoomFilter {
            items.removeAll { $0.classRoomId != classRoomFilter }
        }
        items.sort { $0.enrolledAt > $1.enrolledAt }
        return encodeArrayOrDefault(items)
    }

    private func parseFilters(from params: String) -> (student: String?, classRoom: String?) {
        guard let data = params.data(using: .utf8),
              let object = try? JSONSerialization.jsonObject(with: data) as? [String: Any]
        else { return (nil, nil) }
        let student = (object["studentId"] as? String
            ?? object["student_id"] as? String)?.lowercased()
        let classRoom = (object["classRoomId"] as? String
            ?? object["class_room_id"] as? String)?.lowercased()
        return (student, classRoom)
    }

    private struct PersistedState: Codable {
        var enrollments: [String: EnrollmentListItem]
    }
}

public struct EnrollmentListItem: Codable, Sendable {
    public var studentId: String
    public var classRoomId: String
    public var enrolledAt: String

    public init(studentId: String, classRoomId: String, enrolledAt: String) {
        self.studentId = studentId
        self.classRoomId = classRoomId
        self.enrolledAt = enrolledAt
    }
}
