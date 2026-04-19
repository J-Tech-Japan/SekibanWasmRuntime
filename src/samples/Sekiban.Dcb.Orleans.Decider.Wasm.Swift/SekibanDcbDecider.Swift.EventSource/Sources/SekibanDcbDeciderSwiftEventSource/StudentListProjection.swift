import Foundation
import SekibanWasm

/// In-memory multi-projection for standalone student records. Pairs with
/// `ClassRoomListProjection` (which already tracks classrooms + their enrolled counts) but
/// is dedicated to student identity + enrolled classroom ids so `GET /api/students` can
/// return the list without touching the classroom projection.
///
/// Mirrors the Rust `StudentListProjection` shape the template exposes.
public final class StudentListProjection: MultiProjection {
    public static var projectorName: String { "StudentListProjection" }

    private var students: [String: StudentListItem] = [:]

    public init() {}

    public func applyEvent(eventType: String, payload: String, tags: [String]) {
        _ = tags
        guard let data = payload.data(using: .utf8) else { return }
        switch eventType {
        case "StudentCreated":
            if let created = try? JSONDecoder().decode(StudentCreated.self, from: data) {
                let key = created.studentId.uuidString.lowercased()
                students[key] = StudentListItem(
                    studentId: key,
                    name: created.name,
                    maxClassCount: created.maxClassCount,
                    enrolledClassRoomIds: [])
            }
        case "StudentEnrolledInClassRoom":
            if let enrolled = try? JSONDecoder().decode(StudentEnrolledInClassRoom.self, from: data) {
                let key = enrolled.studentId.uuidString.lowercased()
                if var item = students[key] {
                    let classKey = enrolled.classRoomId.uuidString.lowercased()
                    if !item.enrolledClassRoomIds.contains(classKey) {
                        item.enrolledClassRoomIds.append(classKey)
                        students[key] = item
                    }
                }
            }
        case "StudentDroppedFromClassRoom":
            if let dropped = try? JSONDecoder().decode(StudentDroppedFromClassRoom.self, from: data) {
                let key = dropped.studentId.uuidString.lowercased()
                if var item = students[key] {
                    let classKey = dropped.classRoomId.uuidString.lowercased()
                    item.enrolledClassRoomIds.removeAll { $0 == classKey }
                    students[key] = item
                }
            }
        default:
            break
        }
    }

    public func serializeState() -> String {
        let snapshot = PersistedState(students: students)
        guard let data = try? JSONEncoder().encode(snapshot),
              let json = String(data: data, encoding: .utf8)
        else { return "{}" }
        return json
    }

    public func restoreState(_ json: String) {
        let trimmed = json.trimmingCharacters(in: .whitespacesAndNewlines)
        if trimmed.isEmpty || trimmed == "{}" {
            students = [:]
            return
        }
        guard let data = trimmed.data(using: .utf8),
              let snapshot = try? JSONDecoder().decode(PersistedState.self, from: data)
        else {
            students = [:]
            return
        }
        students = snapshot.students
    }

    public func executeQuery(type: String, params: String) -> String {
        _ = params
        switch type {
        case "GetStudentCountQuery":
            return encodeOrDefault(CountResult(count: students.count),
                                   fallback: "{\"count\":\(students.count)}")
        default:
            return "{}"
        }
    }

    public func executeListQuery(type: String, params: String) -> String {
        _ = params
        guard type == "GetStudentListQuery" else { return "[]" }
        let items = students.values.sorted { $0.studentId < $1.studentId }
        return encodeArrayOrDefault(Array(items))
    }

    private struct PersistedState: Codable {
        var students: [String: StudentListItem]
    }
}

public struct StudentListItem: Codable, Sendable {
    public var studentId: String
    public var name: String
    public var maxClassCount: Int32
    public var enrolledClassRoomIds: [String]

    public init(studentId: String, name: String, maxClassCount: Int32, enrolledClassRoomIds: [String]) {
        self.studentId = studentId
        self.name = name
        self.maxClassCount = maxClassCount
        self.enrolledClassRoomIds = enrolledClassRoomIds
    }
}
