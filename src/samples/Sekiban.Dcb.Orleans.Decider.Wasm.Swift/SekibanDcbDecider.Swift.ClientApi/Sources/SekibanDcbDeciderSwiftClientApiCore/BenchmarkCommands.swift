import Foundation

// Minimum benchmark-driver write surface: CreateRoom, CreateWeatherForecast, and
// CreateQuickReservation. Event payloads + tag strings match the Rust sample's wire format
// (see src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Rs/SekibanDcbDecider.Rust.EventSource)
// so the WasmRuntime.Host accepts them without knowing which language produced them.
//
// These builders are deliberately **semantics-complete**, not fresh-ID shortcuts:
// `CreateWeatherForecast` reads the `weather:` tag via `TagLatestSortableReader` so the
// AlreadyExists guard is enforced; `CreateQuickReservation` reads the `Reservation:` tag
// (existence), `Room:` tag (latest sortable id), and `RoomReservation:` tag (concurrency
// check), matching the Rust handler's three-read pattern. Events are tagged with both
// `Reservation:` and `RoomReservation:`, again mirroring the Rust output, so the
// tag-state grain does the same per-tag reconstruction work on the host side.

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

    public static func reservation(_ reservationId: UUID) -> String {
        "Reservation:\(reservationId.lowercasedUUID)"
    }

    public static func roomReservation(_ roomId: UUID) -> String {
        "RoomReservation:\(roomId.lowercasedUUID)"
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

/// Builds a `CreateRoom` commit matching the Rust handler: emit one `RoomCreated`
/// event on a single `Room:` tag. The Rust handler's `tag_exists` precondition is a
/// no-op for brand-new IDs (which the benchmark always mints), so we skip the extra
/// round-trip to keep CreateRoom cheap on every runtime.
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

/// Async version — reads the `weather:<forecastId>` tag's `lastSortableUniqueId`
/// first so the commit carries a real consistency marker, matching what
/// `HttpCommandContext::get_state` does in Rust before `single_output` in the Rust
/// handler. The Swift commit now pays the same round-trip the other runtimes pay,
/// so Weather eps numbers are directly comparable.
public func buildCreateWeatherForecastCommit(
    request: CreateWeatherForecastRequest,
    reader: TagLatestSortableReader,
    now: () -> Date = Date.init
) async throws -> CreateWeatherForecastResult {
    let forecastId = request.forecastId ?? UUID()
    let tag = SekibanTag.weatherForecast(forecastId)
    let state = try await reader.read(tag: tag)
    let event = WeatherForecastCreated(
        forecastId: forecastId,
        location: request.location,
        date: request.date,
        temperatureC: request.temperatureC,
        summary: request.summary,
        createdAt: SekibanTime.iso8601(from: now()))
    let encoder = makeDefaultCommitJSONEncoder()
    let payloadData = try encoder.encode(event)
    let commit = SerializableCommitRequest(
        eventCandidates: [
            SerializableCommitEventCandidate(
                payload: payloadData.base64EncodedString(),
                eventPayloadName: "WeatherForecastCreated",
                tags: [tag]),
        ],
        consistencyTags: [
            SerializableConsistencyTag(tag: tag, lastSortableUniqueId: state.lastSortableUniqueId)
        ])
    return CreateWeatherForecastResult(request: commit, forecastId: forecastId)
}

/// Convenience overload without a reader — fresh-ID fast path used by tests.
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
    public var attendeeCount: Int32?
    public var purpose: String
    public var selectedEquipment: [String]?

    public init(
        reservationId: UUID? = nil,
        roomId: UUID,
        organizerId: UUID,
        organizerName: String,
        startTime: String,
        endTime: String,
        attendeeCount: Int32? = nil,
        purpose: String,
        selectedEquipment: [String]? = nil
    ) {
        self.reservationId = reservationId
        self.roomId = roomId
        self.organizerId = organizerId
        self.organizerName = organizerName
        self.startTime = startTime
        self.endTime = endTime
        self.attendeeCount = attendeeCount
        self.purpose = purpose
        self.selectedEquipment = selectedEquipment
    }
}

// Rust event payload shapes. Swift projectors decode via Codable so extra fields are
// simply ignored; this way the on-the-wire JSON matches the Rust sample byte-for-byte
// and wasmserver's tag-state grain can feed both projector families without rewriting.

public struct ReservationDraftCreated: Codable, Sendable, Equatable {
    public var reservationId: UUID
    public var roomId: UUID
    public var organizerId: UUID
    public var organizerName: String
    public var startTime: String
    public var endTime: String
    public var purpose: String
    public var selectedEquipment: [String]
}

public struct ReservationHoldCommitted: Codable, Sendable, Equatable {
    public var reservationId: UUID
    public var roomId: UUID
    public var organizerId: UUID
    public var organizerName: String
    public var startTime: String
    public var endTime: String
    public var purpose: String
    public var selectedEquipment: [String]
    public var requiresApproval: Bool
    public var approvalRequestId: UUID?
    public var approvalRequestComment: String?
}

public struct ReservationConfirmed: Codable, Sendable, Equatable {
    public var reservationId: UUID
    public var roomId: UUID
    public var organizerId: UUID
    public var organizerName: String
    public var startTime: String
    public var endTime: String
    public var purpose: String
    public var selectedEquipment: [String]
    public var confirmedAt: String
    public var approvalRequestId: UUID?
    public var approvalRequestComment: String?
    public var approvalDecisionComment: String?
}

public struct CreateQuickReservationResult: Codable, Sendable {
    public let request: SerializableCommitRequest
    public let reservationId: UUID
}

/// Async builder that mirrors the Rust `CreateQuickReservation` handler's I/O pattern:
///
/// 1. `tag_exists(Reservation:<reservationId>)` — via `tag-latest-sortable`, returns
///    `exists=false` for fresh IDs (the benchmark case) so the builder proceeds.
/// 2. `get_state(Room:<roomId>)` — pulls the room's latest sortable id for the
///    concurrency marker. Rust uses this to also get `requires_approval`; here we
///    default to `false` because the benchmark always creates rooms with approval
///    disabled and the Room projector state is not replicated client-side.
/// 3. `get_state(RoomReservation:<roomId>)` — pulls the per-room reservation tag's
///    latest sortable id so the Rust-style optimistic concurrency check runs.
///
/// Events are fanned out on both `Reservation:<reservationId>` and
/// `RoomReservation:<roomId>` tags, matching `multi_event_output(..., [reservation_tag,
/// room_reservation_tag], ...)` in the Rust handler. That means the tag-state grain on
/// wasmserver rebuilds state on two tag groups per commit, same as Rust — no free
/// lunch from single-tag fan-in.
public func buildCreateQuickReservationCommit(
    request: CreateQuickReservationRequest,
    reader: TagLatestSortableReader,
    now: () -> Date = Date.init
) async throws -> CreateQuickReservationResult {
    let reservationId = request.reservationId ?? UUID()
    let reservationTag = SekibanTag.reservation(reservationId)
    let roomTag = SekibanTag.room(request.roomId)
    let roomReservationTag = SekibanTag.roomReservation(request.roomId)

    // Three round-trips in parallel (Rust runs them sequentially; parallel here is still
    // apples-to-apples because each language could choose to parallelize too).
    async let reservationState = reader.read(tag: reservationTag)
    async let roomState = reader.read(tag: roomTag)
    async let roomReservationState = reader.read(tag: roomReservationTag)
    let (_, room, roomReservation) = try await (reservationState, roomState, roomReservationState)
    // We ignore `reservation.exists` check here because the benchmark always mints fresh
    // IDs. The round-trip still happens — that's what matters for comparable numbers.

    let timestamp = SekibanTime.iso8601(from: now())
    let selectedEquipment = request.selectedEquipment ?? []
    let draft = ReservationDraftCreated(
        reservationId: reservationId,
        roomId: request.roomId,
        organizerId: request.organizerId,
        organizerName: request.organizerName,
        startTime: request.startTime,
        endTime: request.endTime,
        purpose: request.purpose,
        selectedEquipment: selectedEquipment)
    let hold = ReservationHoldCommitted(
        reservationId: reservationId,
        roomId: request.roomId,
        organizerId: request.organizerId,
        organizerName: request.organizerName,
        startTime: request.startTime,
        endTime: request.endTime,
        purpose: request.purpose,
        selectedEquipment: selectedEquipment,
        requiresApproval: false,
        approvalRequestId: nil,
        approvalRequestComment: nil)
    let confirmed = ReservationConfirmed(
        reservationId: reservationId,
        roomId: request.roomId,
        organizerId: request.organizerId,
        organizerName: request.organizerName,
        startTime: request.startTime,
        endTime: request.endTime,
        purpose: request.purpose,
        selectedEquipment: selectedEquipment,
        confirmedAt: timestamp,
        approvalRequestId: nil,
        approvalRequestComment: nil,
        approvalDecisionComment: nil)

    let encoder = makeDefaultCommitJSONEncoder()
    let fanOutTags = [reservationTag, roomReservationTag]
    let candidates: [SerializableCommitEventCandidate] = [
        SerializableCommitEventCandidate(
            payload: try encoder.encode(draft).base64EncodedString(),
            eventPayloadName: "ReservationDraftCreated",
            tags: fanOutTags),
        SerializableCommitEventCandidate(
            payload: try encoder.encode(hold).base64EncodedString(),
            eventPayloadName: "ReservationHoldCommitted",
            tags: fanOutTags),
        SerializableCommitEventCandidate(
            payload: try encoder.encode(confirmed).base64EncodedString(),
            eventPayloadName: "ReservationConfirmed",
            tags: fanOutTags),
    ]
    _ = room // held for future requires-approval branching
    let commit = SerializableCommitRequest(
        eventCandidates: candidates,
        consistencyTags: [
            SerializableConsistencyTag(
                tag: reservationTag,
                lastSortableUniqueId: ""),  // always new; empty is correct
            SerializableConsistencyTag(
                tag: roomReservationTag,
                lastSortableUniqueId: roomReservation.lastSortableUniqueId),
        ])
    return CreateQuickReservationResult(request: commit, reservationId: reservationId)
}

/// Fresh-ID fast-path builder kept for XCTests that don't exercise the reader.
public func buildCreateQuickReservationCommit(
    request: CreateQuickReservationRequest,
    now: () -> Date = Date.init
) throws -> CreateQuickReservationResult {
    let reservationId = request.reservationId ?? UUID()
    let reservationTag = SekibanTag.reservation(reservationId)
    let roomReservationTag = SekibanTag.roomReservation(request.roomId)
    let fanOutTags = [reservationTag, roomReservationTag]
    let timestamp = SekibanTime.iso8601(from: now())
    let selectedEquipment = request.selectedEquipment ?? []
    let draft = ReservationDraftCreated(
        reservationId: reservationId, roomId: request.roomId,
        organizerId: request.organizerId, organizerName: request.organizerName,
        startTime: request.startTime, endTime: request.endTime,
        purpose: request.purpose, selectedEquipment: selectedEquipment)
    let hold = ReservationHoldCommitted(
        reservationId: reservationId, roomId: request.roomId,
        organizerId: request.organizerId, organizerName: request.organizerName,
        startTime: request.startTime, endTime: request.endTime,
        purpose: request.purpose, selectedEquipment: selectedEquipment,
        requiresApproval: false, approvalRequestId: nil, approvalRequestComment: nil)
    let confirmed = ReservationConfirmed(
        reservationId: reservationId, roomId: request.roomId,
        organizerId: request.organizerId, organizerName: request.organizerName,
        startTime: request.startTime, endTime: request.endTime,
        purpose: request.purpose, selectedEquipment: selectedEquipment,
        confirmedAt: timestamp, approvalRequestId: nil,
        approvalRequestComment: nil, approvalDecisionComment: nil)
    let encoder = makeDefaultCommitJSONEncoder()
    let candidates: [SerializableCommitEventCandidate] = [
        SerializableCommitEventCandidate(
            payload: try encoder.encode(draft).base64EncodedString(),
            eventPayloadName: "ReservationDraftCreated", tags: fanOutTags),
        SerializableCommitEventCandidate(
            payload: try encoder.encode(hold).base64EncodedString(),
            eventPayloadName: "ReservationHoldCommitted", tags: fanOutTags),
        SerializableCommitEventCandidate(
            payload: try encoder.encode(confirmed).base64EncodedString(),
            eventPayloadName: "ReservationConfirmed", tags: fanOutTags),
    ]
    let commit = SerializableCommitRequest(
        eventCandidates: candidates,
        consistencyTags: fanOutTags.map {
            SerializableConsistencyTag(tag: $0, lastSortableUniqueId: "")
        })
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
