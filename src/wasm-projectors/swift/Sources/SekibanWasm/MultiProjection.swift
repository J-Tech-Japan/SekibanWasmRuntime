import Foundation

/// Protocol a Swift-side multi-projection implements so the generic `WasmRuntime.Host`
/// MultiProjectionGrain can drive it through the primitive ABI
/// (`create_instance`/`apply_event`/`serialize_state`/`restore_state`/
/// `execute_query`/`execute_list_query`). Kept deliberately simple: per-instance mutable
/// state, plus four string-in/string-out entry points.
///
/// Projectors are expected to be class types (reference semantics) so `apply` mutates the
/// same instance the manager holds; a value-type projector would be copied on each
/// dispatch call. See `ClassRoomListProjection` in the Swift sample for a reference.
public protocol MultiProjection: AnyObject {
    static var projectorName: String { get }

    func applyEvent(eventType: String, payload: String, tags: [String])
    func serializeState() -> String
    func restoreState(_ json: String)
    func executeQuery(type: String, params: String) -> String
    func executeListQuery(type: String, params: String) -> String
}

/// Minimal instance registry. The WASM module runs single-threaded, so a simple dictionary
/// is sufficient. Each `create_instance` call returns the next integer id; the host keeps
/// that id for subsequent `apply_event`/`serialize_state`/… calls.
public final class PrimitiveInstanceManager: @unchecked Sendable {
    public static let shared = PrimitiveInstanceManager()
    private var instances: [Int32: MultiProjection] = [:]
    private var nextId: Int32 = 1
    private init() {}

    public func register(_ instance: MultiProjection) -> Int32 {
        let id = nextId
        nextId &+= 1
        instances[id] = instance
        return id
    }

    public func get(_ id: Int32) -> MultiProjection? { instances[id] }

    public func remove(_ id: Int32) { instances.removeValue(forKey: id) }
}
