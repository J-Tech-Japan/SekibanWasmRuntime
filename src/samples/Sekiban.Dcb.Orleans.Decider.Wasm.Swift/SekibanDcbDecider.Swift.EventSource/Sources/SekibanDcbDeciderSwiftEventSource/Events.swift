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

public struct RoomUpdated: Codable, Sendable {
    public var roomId: UUID
    public var name: String
    public var capacity: Int32
    public var location: String
    public var equipment: [String]
    public var requiresApproval: Bool
    public var updatedAt: String
}

public struct RoomDeactivated: Codable, Sendable {
    public var roomId: UUID
    public var reason: String?
    public var deactivatedAt: String
}

public struct RoomReactivated: Codable, Sendable {
    public var roomId: UUID
    public var reactivatedAt: String
}

public struct WeatherForecastCreated: Codable, Sendable {
    public var forecastId: UUID
    public var location: String
    public var date: String
    public var temperatureC: Int32
    public var summary: String
    public var createdAt: String
}

public struct WeatherForecastLocationUpdated: Codable, Sendable {
    public var forecastId: UUID
    public var newLocation: String
    public var updatedAt: String
}

public struct WeatherForecastDeleted: Codable, Sendable {
    public var forecastId: UUID
    public var deletedAt: String
}

// Reservation events mirror the Rust sample's payload shape so the same JSON flows
// through the generic WasmRuntime.Host regardless of which language produced it. Fields
// absent from the benchmark payload (`approvalRequestId`, `selectedEquipment`, etc.) are
// optional/defaulted on decode — the benchmark's Swift ClientApi doesn't touch them.

public struct ReservationDraftCreated: Codable, Sendable {
    public var reservationId: UUID
    public var roomId: UUID
    public var organizerId: UUID
    public var organizerName: String
    public var startTime: String
    public var endTime: String
    public var purpose: String
    public var selectedEquipment: [String]?
}

public struct ReservationHoldCommitted: Codable, Sendable {
    public var reservationId: UUID
    public var roomId: UUID
    public var organizerId: UUID
    public var organizerName: String
    public var startTime: String
    public var endTime: String
    public var purpose: String
    public var selectedEquipment: [String]?
    public var requiresApproval: Bool?
    public var approvalRequestId: UUID?
    public var approvalRequestComment: String?
}

public struct ReservationConfirmed: Codable, Sendable {
    // All contextual fields are optional so both shapes decode: the rich quick-
    // reservation commit carries the full payload, the lifecycle transition from the
    // confirm/cancel/reject endpoints carries only { reservationId, roomId,
    // confirmedAt }. Projectors that already have the earlier Draft/Hold state only
    // need the ids + confirmedAt to transition — everything else is context.
    public var reservationId: UUID
    public var roomId: UUID
    public var organizerId: UUID?
    public var organizerName: String?
    public var startTime: String?
    public var endTime: String?
    public var purpose: String?
    public var selectedEquipment: [String]?
    public var confirmedAt: String
    public var approvalRequestId: UUID?
    public var approvalRequestComment: String?
    public var approvalDecisionComment: String?
}

public struct ReservationCancelled: Codable, Sendable {
    public var reservationId: UUID
    public var roomId: UUID
    public var reason: String?
    public var cancelledAt: String
}

public struct ReservationRejected: Codable, Sendable {
    public var reservationId: UUID
    public var roomId: UUID
    public var reason: String?
    public var rejectedAt: String
}
