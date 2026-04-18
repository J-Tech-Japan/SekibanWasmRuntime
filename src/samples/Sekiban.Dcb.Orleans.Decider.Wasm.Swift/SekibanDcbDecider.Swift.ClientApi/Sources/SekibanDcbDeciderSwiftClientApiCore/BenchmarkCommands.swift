import Foundation

// Minimum benchmark-driver write surface: CreateRoom, CreateWeatherForecast, and
// CreateQuickReservation. Event payloads + tag strings match the Rust sample's wire format
// (see src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Rs/SekibanDcbDecider.Rust.EventSource)
// so the WasmRuntime.Host accepts them without knowing which language produced them.
//
// Swift implements these command handlers directly (no WASM call) because the benchmark
// pattern always mints a fresh entity ID — the "AlreadyExists" guard in the Rust handler is
// a no-op for brand-new tags, so state reconstruction can be skipped.

// ---------------------------------------------------------------------------
// Tag helpers
// ---------------------------------------------------------------------------

public enum SekibanTag {
    public static func weatherForecast(_ forecastId: UUID) -> String {
        "weather:\(forecastId.lowercasedUUID)"
    }

    public static func room(_ roomId: UUID) -> String {
        "Room:\(roomId.lowercasedUUID)"
    }

    public static func roomReservation(_ reservationId: UUID) -> String {
        "RoomReservation:\(reservationId.lowercasedUUID)"
    }

    public static func user(_ userId: UUID) -> String {
        "User:\(userId.lowercasedUUID)"
    }
}

public extension UUID {
    /// Sekiban's C# side stores UUIDs lowercased (`Guid.ToString("D")` in invariant culture);
    /// the Rust derive macro emits `.to_string()` which is already lowercase. Force lowercase
    /// here so tag strings are byte-stable across languages — matters for the tag-state grain
    /// key derivation on the C# side.
    var lowercasedUUID: String { uuidString.lowercased() }
}

// ---------------------------------------------------------------------------
// CreateRoom
// ---------------------------------------------------------------------------

public struct CreateRoomRequest: Codable, Sendable {
    public var roomId: UUID?
    public var name: String
    public var capacity: Int32
    public var location: String
    public var equipment: [String]
    public var requiresApproval: Bool

    public init(
        roomId: UUID? = nil,
        name: String,
        capacity: Int32,
        location: String,
        equipment: [String],
        requiresApproval: Bool
    ) {
        self.roomId = roomId
        self.name = name
        self.capacity = capacity
        self.location = location
        self.equipment = equipment
        self.requiresApproval = requiresApproval
    }
}

public struct RoomCreated: Codable, Sendable, Equatable {
    public var roomId: UUID
    public var name: String
    public var capacity: Int32
    public var location: String
    public var equipment: [String]
    public var requiresApproval: Bool
    public var createdAt: String
}

public struct CreateRoomResult: Codable, Sendable {
    public let request: SerializableCommitRequest
    public let roomId: UUID
    public let eventTypeName: String
}

public func buildCreateRoomCommit(
    request: CreateRoomRequest,
    now: () -> Date = Date.init
) throws -> CreateRoomResult {
    let roomId = request.roomId ?? UUID()
    let event = RoomCreated(
        roomId: roomId,
        name: request.name,
        capacity: request.capacity,
        location: request.location,
        equipment: request.equipment,
        requiresApproval: request.requiresApproval,
        createdAt: SekibanTime.iso8601(from: now()))
    let commit = try buildSingleEventCommit(
        eventPayload: event,
        eventTypeName: "RoomCreated",
        tag: SekibanTag.room(roomId))
    return CreateRoomResult(request: commit, roomId: roomId, eventTypeName: "RoomCreated")
}

// ---------------------------------------------------------------------------
// CreateWeatherForecast
// ---------------------------------------------------------------------------

public struct CreateWeatherForecastRequest: Codable, Sendable {
    public var forecastId: UUID?
    public var location: String
    public var date: String
    public var temperatureC: Int32
    public var summary: String

    public init(
        forecastId: UUID? = nil,
        location: String,
        date: String,
        temperatureC: Int32,
        summary: String
    ) {
        self.forecastId = forecastId
        self.location = location
        self.date = date
        self.temperatureC = temperatureC
        self.summary = summary
    }
}

public struct WeatherForecastCreated: Codable, Sendable, Equatable {
    public var forecastId: UUID
    public var location: String
    public var date: String
    public var temperatureC: Int32
    public var summary: String
    public var createdAt: String
}

public struct CreateWeatherForecastResult: Codable, Sendable {
    public let request: SerializableCommitRequest
    public let forecastId: UUID
}

public func buildCreateWeatherForecastCommit(
    request: CreateWeatherForecastRequest,
    now: () -> Date = Date.init
) throws -> CreateWeatherForecastResult {
    let forecastId = request.forecastId ?? UUID()
    let event = WeatherForecastCreated(
        forecastId: forecastId,
        location: request.location,
        date: request.date,
        temperatureC: request.temperatureC,
        summary: request.summary,
        createdAt: SekibanTime.iso8601(from: now()))
    let commit = try buildSingleEventCommit(
        eventPayload: event,
        eventTypeName: "WeatherForecastCreated",
        tag: SekibanTag.weatherForecast(forecastId))
    return CreateWeatherForecastResult(request: commit, forecastId: forecastId)
}

// ---------------------------------------------------------------------------
// CreateQuickReservation — draft + hold + confirm in a single commit
// ---------------------------------------------------------------------------

public struct CreateQuickReservationRequest: Codable, Sendable {
    public var reservationId: UUID?
    public var roomId: UUID
    public var organizerId: UUID
    public var organizerName: String
    public var startTime: String
    public var endTime: String
    public var attendeeCount: Int32
    public var purpose: String

    public init(
        reservationId: UUID? = nil,
        roomId: UUID,
        organizerId: UUID,
        organizerName: String,
        startTime: String,
        endTime: String,
        attendeeCount: Int32,
        purpose: String
    ) {
        self.reservationId = reservationId
        self.roomId = roomId
        self.organizerId = organizerId
        self.organizerName = organizerName
        self.startTime = startTime
        self.endTime = endTime
        self.attendeeCount = attendeeCount
        self.purpose = purpose
    }
}

public struct ReservationDraftCreated: Codable, Sendable, Equatable {
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

public struct ReservationHeld: Codable, Sendable, Equatable {
    public var reservationId: UUID
    public var roomId: UUID
    public var heldAt: String
}

public struct ReservationConfirmed: Codable, Sendable, Equatable {
    public var reservationId: UUID
    public var roomId: UUID
    public var confirmedAt: String
}

public struct CreateQuickReservationResult: Codable, Sendable {
    public let request: SerializableCommitRequest
    public let reservationId: UUID
}

/// Quick reservation emits three events on the same `RoomReservation` tag in one commit.
/// The Rust sample additionally touches `Room:` and `User:` tags for state reads, but for
/// the benchmark (fresh IDs every time) we only need the reservation tag — the other tags
/// would have no prior events anyway.
public func buildCreateQuickReservationCommit(
    request: CreateQuickReservationRequest,
    now: () -> Date = Date.init
) throws -> CreateQuickReservationResult {
    let reservationId = request.reservationId ?? UUID()
    let timestamp = SekibanTime.iso8601(from: now())
    let draft = ReservationDraftCreated(
        reservationId: reservationId,
        roomId: request.roomId,
        organizerId: request.organizerId,
        organizerName: request.organizerName,
        startTime: request.startTime,
        endTime: request.endTime,
        attendeeCount: request.attendeeCount,
        purpose: request.purpose,
        createdAt: timestamp)
    let held = ReservationHeld(
        reservationId: reservationId,
        roomId: request.roomId,
        heldAt: timestamp)
    let confirmed = ReservationConfirmed(
        reservationId: reservationId,
        roomId: request.roomId,
        confirmedAt: timestamp)

    let encoder = makeDefaultCommitJSONEncoder()
    let draftData = try encoder.encode(draft)
    let heldData = try encoder.encode(held)
    let confirmedData = try encoder.encode(confirmed)

    let reservationTag = SekibanTag.roomReservation(reservationId)
    let candidates: [SerializableCommitEventCandidate] = [
        SerializableCommitEventCandidate(
            payload: draftData.base64EncodedString(),
            eventPayloadName: "ReservationDraftCreated",
            tags: [reservationTag]),
        SerializableCommitEventCandidate(
            payload: heldData.base64EncodedString(),
            eventPayloadName: "ReservationHeld",
            tags: [reservationTag]),
        SerializableCommitEventCandidate(
            payload: confirmedData.base64EncodedString(),
            eventPayloadName: "ReservationConfirmed",
            tags: [reservationTag]),
    ]
    let commit = SerializableCommitRequest(
        eventCandidates: candidates,
        consistencyTags: [
            SerializableConsistencyTag(tag: reservationTag, lastSortableUniqueId: "")
        ])
    return CreateQuickReservationResult(request: commit, reservationId: reservationId)
}

// ---------------------------------------------------------------------------
// Time helper
// ---------------------------------------------------------------------------

public enum SekibanTime {
    /// RFC3339 / ISO8601 with fractional seconds to match `Utc::now().to_rfc3339()` in Rust
    /// (which always emits fractional seconds). Stable across locales.
    public static func iso8601(from date: Date) -> String {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [
            .withInternetDateTime,
            .withFractionalSeconds,
        ]
        return formatter.string(from: date)
    }
}
