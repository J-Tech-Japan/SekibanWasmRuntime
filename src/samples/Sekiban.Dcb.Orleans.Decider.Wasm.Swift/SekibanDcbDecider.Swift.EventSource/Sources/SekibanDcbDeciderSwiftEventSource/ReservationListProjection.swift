import Foundation
import SekibanWasm

/// In-memory multi-projection for room reservations. Captures the full draft → held →
/// confirmed lifecycle on a single item per reservation id. Supports both the flat
/// `GetReservationListQuery` and the filtered `GetReservationsByRoomQuery`.
public final class ReservationListProjection: MultiProjection {
    public static var projectorName: String { "ReservationListProjection" }

    private var reservations: [String: ReservationListItem] = [:]

    public init() {}

    public func applyEvent(eventType: String, payload: String, tags: [String]) {
        // Rust fans out each reservation event on BOTH the Reservation:<id> tag and
        // the RoomReservation:<roomId> tag. wasmserver calls `apply_event` for both
        // tags, so we see the same event twice per commit. Ignoring the second hit is
        // trivial because we key by reservationId and the payload is identical.
        _ = tags
        guard let data = payload.data(using: .utf8) else { return }
        switch eventType {
        case "ReservationDraftCreated":
            if let evt = try? JSONDecoder().decode(ReservationDraftCreated.self, from: data) {
                let key = evt.reservationId.uuidString.lowercased()
                reservations[key] = ReservationListItem(
                    reservationId: key,
                    roomId: evt.roomId.uuidString.lowercased(),
                    organizerId: evt.organizerId.uuidString.lowercased(),
                    organizerName: evt.organizerName,
                    startTime: evt.startTime,
                    endTime: evt.endTime,
                    attendeeCount: 1,
                    purpose: evt.purpose,
                    status: "Draft",
                    createdAt: evt.startTime,
                    heldAt: nil,
                    confirmedAt: nil)
            }
        case "ReservationHoldCommitted":
            if let evt = try? JSONDecoder().decode(ReservationHoldCommitted.self, from: data) {
                let key = evt.reservationId.uuidString.lowercased()
                if var item = reservations[key] {
                    item.status = "Held"
                    item.heldAt = evt.startTime
                    reservations[key] = item
                }
            }
        case "ReservationConfirmed":
            if let evt = try? JSONDecoder().decode(ReservationConfirmed.self, from: data) {
                let key = evt.reservationId.uuidString.lowercased()
                if var item = reservations[key] {
                    item.status = "Confirmed"
                    item.confirmedAt = evt.confirmedAt
                    reservations[key] = item
                }
            }
        case "ReservationCancelled":
            if let evt = try? JSONDecoder().decode(ReservationCancelled.self, from: data) {
                let key = evt.reservationId.uuidString.lowercased()
                if var item = reservations[key] {
                    item.status = "Cancelled"
                    reservations[key] = item
                }
            }
        case "ReservationRejected":
            if let evt = try? JSONDecoder().decode(ReservationRejected.self, from: data) {
                let key = evt.reservationId.uuidString.lowercased()
                if var item = reservations[key] {
                    item.status = "Rejected"
                    reservations[key] = item
                }
            }
        default:
            break
        }
    }

    public func serializeState() -> String {
        let snapshot = PersistedState(reservations: reservations)
        guard let data = try? JSONEncoder().encode(snapshot),
              let json = String(data: data, encoding: .utf8)
        else { return "{}" }
        return json
    }

    public func restoreState(_ json: String) {
        let trimmed = json.trimmingCharacters(in: .whitespacesAndNewlines)
        if trimmed.isEmpty || trimmed == "{}" {
            reservations = [:]
            return
        }
        guard let data = trimmed.data(using: .utf8),
              let snapshot = try? JSONDecoder().decode(PersistedState.self, from: data)
        else {
            reservations = [:]
            return
        }
        reservations = snapshot.reservations
    }

    public func executeQuery(type: String, params: String) -> String {
        _ = params
        switch type {
        case "GetReservationCountQuery":
            return encodeOrDefault(CountResult(count: reservations.count),
                                   fallback: "{\"count\":\(reservations.count)}")
        default:
            return "{}"
        }
    }

    public func executeListQuery(type: String, params: String) -> String {
        switch type {
        case "GetReservationListQuery":
            let items = reservations.values.sorted { $0.reservationId < $1.reservationId }
            return encodeArrayOrDefault(Array(items))
        case "GetReservationsByRoomQuery":
            let roomId = parseRoomId(from: params)?.lowercased()
            let items = reservations.values
                .filter { roomId == nil || $0.roomId == roomId }
                .sorted { $0.reservationId < $1.reservationId }
            return encodeArrayOrDefault(Array(items))
        default:
            return "[]"
        }
    }

    private func parseRoomId(from params: String) -> String? {
        // Params JSON: `{"roomId":"<uuid>","pageSize":N,...}`. Decode leniently; if the
        // payload shape differs, return nil and fall through to "all reservations".
        guard let data = params.data(using: .utf8),
              let object = try? JSONSerialization.jsonObject(with: data) as? [String: Any]
        else { return nil }
        if let roomId = object["roomId"] as? String { return roomId }
        if let roomId = object["RoomId"] as? String { return roomId }
        return nil
    }

    private struct PersistedState: Codable {
        var reservations: [String: ReservationListItem]
    }
}

public struct ReservationListItem: Codable, Sendable {
    public var reservationId: String
    public var roomId: String
    public var organizerId: String
    public var organizerName: String
    public var startTime: String
    public var endTime: String
    public var attendeeCount: Int32
    public var purpose: String
    public var status: String
    public var createdAt: String
    public var heldAt: String?
    public var confirmedAt: String?

    public init(
        reservationId: String,
        roomId: String,
        organizerId: String,
        organizerName: String,
        startTime: String,
        endTime: String,
        attendeeCount: Int32,
        purpose: String,
        status: String,
        createdAt: String,
        heldAt: String?,
        confirmedAt: String?
    ) {
        self.reservationId = reservationId
        self.roomId = roomId
        self.organizerId = organizerId
        self.organizerName = organizerName
        self.startTime = startTime
        self.endTime = endTime
        self.attendeeCount = attendeeCount
        self.purpose = purpose
        self.status = status
        self.createdAt = createdAt
        self.heldAt = heldAt
        self.confirmedAt = confirmedAt
    }
}
