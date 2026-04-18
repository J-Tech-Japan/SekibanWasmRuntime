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

// ---------------------------------------------------------------------------
// Meeting room domain — mirrors the Rust sample's event shapes so the Swift
// runtime host can deserialize events produced by either ClientApi. Fields are
// camelCase (JSONSerialization / Codable default) to match Sekiban's
// JsonSerializerDefaults.Web on the C# side.
// ---------------------------------------------------------------------------

public struct RoomCreated: Codable, Sendable {
    public var roomId: UUID
    public var name: String
    public var capacity: Int32
    public var location: String
    public var equipment: [String]
    public var requiresApproval: Bool
    public var createdAt: String
}

public struct WeatherForecastCreated: Codable, Sendable {
    public var forecastId: UUID
    public var location: String
    public var date: String
    public var temperatureC: Int32
    public var summary: String
    public var createdAt: String
}

public struct ReservationDraftCreated: Codable, Sendable {
    public var reservationId: UUID
    public var roomId: UUID
    public var organizerId: UUID
    public var organizerName: String
    public var startTime: String
    public var endTime: String
    public var attendeeCount: Int32
    public var purpose: String
    public var createdAt: String
}

public struct ReservationHeld: Codable, Sendable {
    public var reservationId: UUID
    public var roomId: UUID
    public var heldAt: String
}

public struct ReservationConfirmed: Codable, Sendable {
    public var reservationId: UUID
    public var roomId: UUID
    public var confirmedAt: String
}
