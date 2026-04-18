import Foundation

/// Dispatch helpers the sample wasm module wraps in `@_cdecl` exports. They turn the FFI
/// parameters into Swift strings, look up the factory/instance, and return the packed
/// `(ptr, len)` `Int64` the host consumes. Mirrors the Rust `instance.rs` module's role.
///
/// The primitive ABI exports live in the sample (so it owns the projector-factory map)
/// rather than in this library — duplicate `@_cdecl` symbols across a library and an exe
/// would be a link error, and the sample knows which projectors to construct.
public enum PrimitiveHelpers {
    public typealias Factory = () -> MultiProjection

    /// Look up the projector by name in `factories`, instantiate it, register with the
    /// shared instance manager, and return the new id. Returns `-1` on unknown name so the
    /// host treats it as a failed activation.
    public static func createInstance(
        factories: [String: Factory],
        namePtr: Int32, nameLen: Int32
    ) -> Int32 {
        let name = readString(ptr: namePtr, len: nameLen)
        guard let factory = factories[name] else { return -1 }
        let instance = factory()
        return PrimitiveInstanceManager.shared.register(instance)
    }

    public static func applyEvent(
        instanceId: Int32,
        eventTypePtr: Int32, eventTypeLen: Int32,
        payloadPtr: Int32, payloadLen: Int32
    ) {
        let eventType = readString(ptr: eventTypePtr, len: eventTypeLen)
        let payload = readString(ptr: payloadPtr, len: payloadLen)
        PrimitiveInstanceManager.shared.get(instanceId)?
            .applyEvent(eventType: eventType, payload: payload, tags: [])
    }

    public static func applyEventWithMetadata(
        instanceId: Int32,
        eventTypePtr: Int32, eventTypeLen: Int32,
        payloadPtr: Int32, payloadLen: Int32,
        metaPtr: Int32, metaLen: Int32
    ) {
        let eventType = readString(ptr: eventTypePtr, len: eventTypeLen)
        let payload = readString(ptr: payloadPtr, len: payloadLen)
        let metaJson = readString(ptr: metaPtr, len: metaLen)
        // Host serializes metadata as `{"tags": ["..."], "sortableUniqueId": "..."}`. Any
        // projector that cares about tags needs them forwarded; decoding failures degrade
        // to the no-tag path so a malformed metadata blob never drops the event itself.
        let tags = decodeMetadataTags(metaJson)
        PrimitiveInstanceManager.shared.get(instanceId)?
            .applyEvent(eventType: eventType, payload: payload, tags: tags)
    }

    /// Parses the common metadata envelope the host attaches via `apply_event_with_metadata`.
    /// Returns an empty array when the metadata blob is missing or unparseable.
    private static func decodeMetadataTags(_ metaJson: String) -> [String] {
        guard let data = metaJson.data(using: .utf8),
              let object = try? JSONSerialization.jsonObject(with: data) as? [String: Any]
        else { return [] }
        return (object["tags"] as? [String]) ?? []
    }

    /// Apply a JSON array of `{"eventType":"...","payloadJson":"..."}` items in order. The
    /// host calls this (via the `apply_events_batch` export) whenever a batch of size > 1
    /// is available during catch-up; without a working implementation the host otherwise
    /// degrades to per-event apply through exception handling, tanking benchmark numbers.
    /// Returns the number of events successfully dispatched.
    public static func applyEventsBatch(
        instanceId: Int32,
        jsonPtr: Int32, jsonLen: Int32
    ) -> Int32 {
        let jsonText = readString(ptr: jsonPtr, len: jsonLen)
        let trimmed = jsonText.trimmingCharacters(in: .whitespacesAndNewlines)
        if trimmed.isEmpty { return 0 }
        guard let data = trimmed.data(using: .utf8),
              let array = try? JSONSerialization.jsonObject(with: data) as? [[String: Any]],
              let instance = PrimitiveInstanceManager.shared.get(instanceId)
        else { return -1 }

        var applied: Int32 = 0
        for item in array {
            guard let eventType = item["eventType"] as? String, !eventType.isEmpty,
                  let payload = item["payloadJson"] as? String else { break }
            let tags = (item["tags"] as? [String]) ?? []
            instance.applyEvent(eventType: eventType, payload: payload, tags: tags)
            applied &+= 1
        }
        return applied
    }

    public static func serializeState(instanceId: Int32) -> Int64 {
        let json = PrimitiveInstanceManager.shared.get(instanceId)?.serializeState() ?? "{}"
        return writeStringToMemory(json)
    }

    public static func restoreState(
        instanceId: Int32,
        ptr: Int32, len: Int32
    ) {
        let json = readString(ptr: ptr, len: len)
        PrimitiveInstanceManager.shared.get(instanceId)?.restoreState(json)
    }

    public static func executeQuery(
        instanceId: Int32,
        typePtr: Int32, typeLen: Int32,
        paramsPtr: Int32, paramsLen: Int32
    ) -> Int64 {
        let type = readString(ptr: typePtr, len: typeLen)
        let params = readString(ptr: paramsPtr, len: paramsLen)
        let json = PrimitiveInstanceManager.shared.get(instanceId)?
            .executeQuery(type: type, params: params) ?? "{}"
        return writeStringToMemory(json)
    }

    public static func executeListQuery(
        instanceId: Int32,
        typePtr: Int32, typeLen: Int32,
        paramsPtr: Int32, paramsLen: Int32
    ) -> Int64 {
        let type = readString(ptr: typePtr, len: typeLen)
        let params = readString(ptr: paramsPtr, len: paramsLen)
        // Projectors return plain JSON arrays for list queries so the host grain can wrap
        // them into `SerializedListQueryResponse.itemsJson`. Fall back to `[]` to stay
        // consistent with that contract even on the unknown-instance branch.
        let json = PrimitiveInstanceManager.shared.get(instanceId)?
            .executeListQuery(type: type, params: params) ?? "[]"
        return writeStringToMemory(json)
    }
}
