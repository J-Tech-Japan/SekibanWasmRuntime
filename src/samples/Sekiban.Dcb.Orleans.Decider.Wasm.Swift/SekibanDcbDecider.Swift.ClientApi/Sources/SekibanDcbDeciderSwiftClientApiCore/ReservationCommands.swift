import Foundation

// Reservation / Room lifecycle command builders beyond CreateQuickReservation. Each
// mirrors the Rust handler's state-read pattern so writes are apples-to-apples across
// runtimes.

// ---------------------------------------------------------------------------
// Reservation state transitions — draft-only + confirm/cancel/reject
// ---------------------------------------------------------------------------

public struct CreateReservationDraftRequest: Codable, Sendable {
    public var reservationId: UUID?
    public var roomId: UUID
    public var organizerId: UUID
    public var organizerName: String
    public var startTime: String
    public var endTime: String
    public var purpose: String
    public var selectedEquipment: [String]?

    public init(
        reservationId: UUID? = nil,
        roomId: UUID,
        organizerId: UUID,
        organizerName: String,
        startTime: String,
        endTime: String,
        purpose: String,
        selectedEquipment: [String]? = nil
    ) {
        self.reservationId = reservationId
        self.roomId = roomId
        self.organizerId = organizerId
        self.organizerName = organizerName
        self.startTime = startTime
        self.endTime = endTime
        self.purpose = purpose
        self.selectedEquipment = selectedEquipment
    }
}

public struct CreateReservationDraftResult: Codable, Sendable {
    public let request: SerializableCommitRequest
    public let reservationId: UUID
}

/// Draft-only flow — emits ReservationDraftCreated on Reservation + RoomReservation tags.
public func buildCreateReservationDraftCommit(
    request: CreateReservationDraftRequest,
    reader: TagLatestSortableReader
) async throws -> CreateReservationDraftResult {
    let reservationId = request.reservationId ?? UUID()
    let reservationTag = SekibanTag.reservation(reservationId)
    let roomReservationTag = SekibanTag.roomReservation(request.roomId)
    async let reservationState = reader.read(tag: reservationTag)
    async let roomReservationState = reader.read(tag: roomReservationTag)
    let (res, roomRes) = try await (reservationState, roomReservationState)

    let draft = ReservationDraftCreated(
        reservationId: reservationId,
        roomId: request.roomId,
        organizerId: request.organizerId,
        organizerName: request.organizerName,
        startTime: request.startTime,
        endTime: request.endTime,
        purpose: request.purpose,
        selectedEquipment: request.selectedEquipment ?? [])
    let encoder = makeDefaultCommitJSONEncoder()
    let body = try encoder.encode(draft)
    let fanOut = [reservationTag, roomReservationTag]
    let commit = SerializableCommitRequest(
        eventCandidates: [
            SerializableCommitEventCandidate(
                payload: body.base64EncodedString(),
                eventPayloadName: "ReservationDraftCreated",
                tags: fanOut),
        ],
        consistencyTags: [
            // Carry the reservation tag's actual last-sortable-id — for a fresh id this
            // is empty, for a caller-supplied id it's the real prior value, and in
            // either case wasmserver gets to enforce optimistic concurrency.
            SerializableConsistencyTag(
                tag: reservationTag,
                lastSortableUniqueId: res.lastSortableUniqueId),
            SerializableConsistencyTag(
                tag: roomReservationTag,
                lastSortableUniqueId: roomRes.lastSortableUniqueId),
        ])
    return CreateReservationDraftResult(request: commit, reservationId: reservationId)
}

public struct ReservationLifecycleRequest: Codable, Sendable {
    public var reservationId: UUID
    public var roomId: UUID
    public var reason: String?

    public init(reservationId: UUID, roomId: UUID, reason: String? = nil) {
        self.reservationId = reservationId
        self.roomId = roomId
        self.reason = reason
    }
}

/// Minimal `ReservationConfirmed` payload emitted by the confirm/cancel/reject
/// lifecycle commands — reservation id + room id + confirmed-at timestamp. The richer
/// `ReservationConfirmed` struct (with organizer / time window / equipment) is used by
/// `buildCreateQuickReservationCommit` where those fields are known at commit-time;
/// the lifecycle transitions here only know the ids. The Swift
/// `ReservationListProjection` uses `Codable` with optional fields, so it decodes both
/// the rich and the minimal shape equivalently.
public struct ReservationConfirmedLifecycle: Codable, Sendable, Equatable {
    public var reservationId: UUID
    public var roomId: UUID
    public var confirmedAt: String
}

/// Emit ReservationConfirmed on Reservation + RoomReservation tags. Caller promises the
/// reservation is currently Held — the Swift projector is idempotent so repeated
/// Confirmed events for the same id don't corrupt state.
public func buildConfirmReservationCommit(
    request: ReservationLifecycleRequest,
    reader: TagLatestSortableReader,
    now: () -> Date = Date.init
) async throws -> SerializableCommitRequest {
    try await buildReservationLifecycleCommit(
        request: request,
        eventTypeName: "ReservationConfirmed",
        body: ReservationConfirmedLifecycle(
            reservationId: request.reservationId,
            roomId: request.roomId,
            confirmedAt: SekibanTime.iso8601(from: now())),
        reader: reader)
}

public func buildCancelReservationCommit(
    request: ReservationLifecycleRequest,
    reader: TagLatestSortableReader,
    now: () -> Date = Date.init
) async throws -> SerializableCommitRequest {
    try await buildReservationLifecycleCommit(
        request: request,
        eventTypeName: "ReservationCancelled",
        body: ReservationCancelled(
            reservationId: request.reservationId,
            roomId: request.roomId,
            reason: request.reason,
            cancelledAt: SekibanTime.iso8601(from: now())),
        reader: reader)
}

public func buildRejectReservationCommit(
    request: ReservationLifecycleRequest,
    reader: TagLatestSortableReader,
    now: () -> Date = Date.init
) async throws -> SerializableCommitRequest {
    try await buildReservationLifecycleCommit(
        request: request,
        eventTypeName: "ReservationRejected",
        body: ReservationRejected(
            reservationId: request.reservationId,
            roomId: request.roomId,
            reason: request.reason,
            rejectedAt: SekibanTime.iso8601(from: now())),
        reader: reader)
}

private func buildReservationLifecycleCommit<T: Encodable>(
    request: ReservationLifecycleRequest,
    eventTypeName: String,
    body: T,
    reader: TagLatestSortableReader
) async throws -> SerializableCommitRequest {
    let reservationTag = SekibanTag.reservation(request.reservationId)
    let roomReservationTag = SekibanTag.roomReservation(request.roomId)
    async let reservationState = reader.read(tag: reservationTag)
    async let roomReservationState = reader.read(tag: roomReservationTag)
    let (res, roomRes) = try await (reservationState, roomReservationState)

    let encoder = makeDefaultCommitJSONEncoder()
    let encoded = try encoder.encode(body)
    return SerializableCommitRequest(
        eventCandidates: [
            SerializableCommitEventCandidate(
                payload: encoded.base64EncodedString(),
                eventPayloadName: eventTypeName,
                tags: [reservationTag, roomReservationTag]),
        ],
        consistencyTags: [
            SerializableConsistencyTag(
                tag: reservationTag,
                lastSortableUniqueId: res.lastSortableUniqueId),
            SerializableConsistencyTag(
                tag: roomReservationTag,
                lastSortableUniqueId: roomRes.lastSortableUniqueId),
        ])
}

public struct ReservationCancelled: Codable, Sendable, Equatable {
    public var reservationId: UUID
    public var roomId: UUID
    public var reason: String?
    public var cancelledAt: String
}

public struct ReservationRejected: Codable, Sendable, Equatable {
    public var reservationId: UUID
    public var roomId: UUID
    public var reason: String?
    public var rejectedAt: String
}

// ---------------------------------------------------------------------------
// Room lifecycle — update / deactivate / reactivate
// ---------------------------------------------------------------------------

public struct UpdateRoomRequest: Codable, Sendable {
    public var roomId: UUID
    public var name: String
    public var capacity: Int32
    public var location: String
    public var equipment: [String]
    public var requiresApproval: Bool

    public init(
        roomId: UUID,
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

public struct RoomUpdated: Codable, Sendable, Equatable {
    public var roomId: UUID
    public var name: String
    public var capacity: Int32
    public var location: String
    public var equipment: [String]
    public var requiresApproval: Bool
    public var updatedAt: String
}

public struct RoomDeactivated: Codable, Sendable, Equatable {
    public var roomId: UUID
    public var reason: String?
    public var deactivatedAt: String
}

public struct RoomReactivated: Codable, Sendable, Equatable {
    public var roomId: UUID
    public var reactivatedAt: String
}

public struct RoomLifecycleRequest: Codable, Sendable {
    public var roomId: UUID
    public var reason: String?

    public init(roomId: UUID, reason: String? = nil) {
        self.roomId = roomId
        self.reason = reason
    }
}

public func buildUpdateRoomCommit(
    request: UpdateRoomRequest,
    reader: TagLatestSortableReader,
    now: () -> Date = Date.init
) async throws -> SerializableCommitRequest {
    let tag = SekibanTag.room(request.roomId)
    let state = try await reader.read(tag: tag)
    let event = RoomUpdated(
        roomId: request.roomId,
        name: request.name,
        capacity: request.capacity,
        location: request.location,
        equipment: request.equipment,
        requiresApproval: request.requiresApproval,
        updatedAt: SekibanTime.iso8601(from: now()))
    let encoder = makeDefaultCommitJSONEncoder()
    return SerializableCommitRequest(
        eventCandidates: [
            SerializableCommitEventCandidate(
                payload: try encoder.encode(event).base64EncodedString(),
                eventPayloadName: "RoomUpdated",
                tags: [tag]),
        ],
        consistencyTags: [
            SerializableConsistencyTag(tag: tag, lastSortableUniqueId: state.lastSortableUniqueId),
        ])
}

public func buildDeactivateRoomCommit(
    request: RoomLifecycleRequest,
    reader: TagLatestSortableReader,
    now: () -> Date = Date.init
) async throws -> SerializableCommitRequest {
    let tag = SekibanTag.room(request.roomId)
    let state = try await reader.read(tag: tag)
    let event = RoomDeactivated(
        roomId: request.roomId,
        reason: request.reason,
        deactivatedAt: SekibanTime.iso8601(from: now()))
    let encoder = makeDefaultCommitJSONEncoder()
    return SerializableCommitRequest(
        eventCandidates: [
            SerializableCommitEventCandidate(
                payload: try encoder.encode(event).base64EncodedString(),
                eventPayloadName: "RoomDeactivated",
                tags: [tag]),
        ],
        consistencyTags: [
            SerializableConsistencyTag(tag: tag, lastSortableUniqueId: state.lastSortableUniqueId),
        ])
}

public func buildReactivateRoomCommit(
    request: RoomLifecycleRequest,
    reader: TagLatestSortableReader,
    now: () -> Date = Date.init
) async throws -> SerializableCommitRequest {
    let tag = SekibanTag.room(request.roomId)
    let state = try await reader.read(tag: tag)
    let event = RoomReactivated(
        roomId: request.roomId,
        reactivatedAt: SekibanTime.iso8601(from: now()))
    let encoder = makeDefaultCommitJSONEncoder()
    return SerializableCommitRequest(
        eventCandidates: [
            SerializableCommitEventCandidate(
                payload: try encoder.encode(event).base64EncodedString(),
                eventPayloadName: "RoomReactivated",
                tags: [tag]),
        ],
        consistencyTags: [
            SerializableConsistencyTag(tag: tag, lastSortableUniqueId: state.lastSortableUniqueId),
        ])
}
