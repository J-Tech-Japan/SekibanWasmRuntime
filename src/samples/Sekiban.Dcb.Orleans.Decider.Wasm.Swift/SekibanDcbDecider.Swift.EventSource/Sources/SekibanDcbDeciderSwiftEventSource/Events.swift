import Foundation

// Event payloads accepted by the ClassRoomEnrollment materialized view. Field names match the
// C# sample's JSON serialization (camelCase via JsonSerializerDefaults.Web), which is how the
// host writes them into `SerializableEvent.Payload`. UUIDs round-trip as lowercase 8-4-4-4-12
// strings — JSONDecoder accepts both cases but we emit lowercase in MvParamBuilder.guid to
// keep the Postgres representation canonical.

public struct ClassRoomCreated: Codable, Sendable {
    public var classRoomId: UUID
    public var name: String
    public var maxStudents: Int32
}

public struct StudentCreated: Codable, Sendable {
    public var studentId: UUID
    public var name: String
    public var maxClassCount: Int32
}

public struct StudentEnrolledInClassRoom: Codable, Sendable {
    public var studentId: UUID
    public var classRoomId: UUID
}

public struct StudentDroppedFromClassRoom: Codable, Sendable {
    public var studentId: UUID
    public var classRoomId: UUID
}
