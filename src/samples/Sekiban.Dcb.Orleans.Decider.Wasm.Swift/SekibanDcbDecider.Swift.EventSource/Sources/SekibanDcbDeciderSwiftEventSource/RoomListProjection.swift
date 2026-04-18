import Foundation
import SekibanWasm

/// In-memory multi-projection for the meeting-room domain. Populated by `RoomCreated`
/// events; `executeListQuery` returns a JSON array the Swift ClientApi forwards to the
/// benchmark driver's `/api/rooms` GET.
public final class RoomListProjection: MultiProjection {
    public static var projectorName: String { "RoomListProjection" }

    private var rooms: [String: RoomListItem] = [:]

    public init() {}

    public func applyEvent(eventType: String, payload: String, tags: [String]) {
        _ = tags
        guard let data = payload.data(using: .utf8) else { return }
        switch eventType {
        case "RoomCreated":
            if let created = try? JSONDecoder().decode(RoomCreated.self, from: data) {
                let key = created.roomId.uuidString.lowercased()
                rooms[key] = RoomListItem(
                    roomId: key,
                    name: created.name,
                    capacity: created.capacity,
                    location: created.location,
                    equipment: created.equipment,
                    requiresApproval: created.requiresApproval,
                    createdAt: created.createdAt)
            }
        default:
            break
        }
    }

    public func serializeState() -> String {
        let snapshot = PersistedState(rooms: rooms)
        guard let data = try? JSONEncoder().encode(snapshot),
              let json = String(data: data, encoding: .utf8)
        else { return "{}" }
        return json
    }

    public func restoreState(_ json: String) {
        let trimmed = json.trimmingCharacters(in: .whitespacesAndNewlines)
        if trimmed.isEmpty || trimmed == "{}" {
            rooms = [:]
            return
        }
        guard let data = trimmed.data(using: .utf8),
              let snapshot = try? JSONDecoder().decode(PersistedState.self, from: data)
        else {
            rooms = [:]
            return
        }
        rooms = snapshot.rooms
    }

    public func executeQuery(type: String, params: String) -> String {
        _ = params
        switch type {
        case "GetRoomCountQuery":
            return encodeOrDefault(CountResult(count: rooms.count),
                                   fallback: "{\"count\":\(rooms.count)}")
        default:
            return "{}"
        }
    }

    public func executeListQuery(type: String, params: String) -> String {
        _ = params
        guard type == "GetRoomListQuery" else { return "[]" }
        let items = rooms.values.sorted { $0.roomId < $1.roomId }
        return encodeArrayOrDefault(Array(items))
    }

    private struct PersistedState: Codable {
        var rooms: [String: RoomListItem]
    }
}

public struct RoomListItem: Codable, Sendable {
    public var roomId: String
    public var name: String
    public var capacity: Int32
    public var location: String
    public var equipment: [String]
    public var requiresApproval: Bool
    public var createdAt: String

    public init(
        roomId: String,
        name: String,
        capacity: Int32,
        location: String,
        equipment: [String],
        requiresApproval: Bool,
        createdAt: String
    ) {
        self.roomId = roomId
        self.name = name
        self.capacity = capacity
        self.location = location
        self.equipment = equipment
        self.requiresApproval = requiresApproval
        self.createdAt = createdAt
    }
}

// Shared JSON helpers used by the meeting-room / weather / reservation projectors.

internal func encodeOrDefault<T: Encodable>(_ value: T, fallback: String) -> String {
    if let data = try? JSONEncoder().encode(value),
       let json = String(data: data, encoding: .utf8) {
        return json
    }
    return fallback
}

internal func encodeArrayOrDefault<T: Encodable>(_ array: [T]) -> String {
    if let data = try? JSONEncoder().encode(array),
       let json = String(data: data, encoding: .utf8) {
        return json
    }
    return "[]"
}
