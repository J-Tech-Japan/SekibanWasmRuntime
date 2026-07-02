import Foundation
import SekibanWasm

// Helper implementations the sample's WASM module wraps in `@_cdecl` exports. We do NOT emit
// `@_cdecl("mv_metadata")` etc. in the shared library because:
//
// * Swift's reactor-mode `_initialize` pass does not reliably run top-level code in dependent
//   modules when built via the 6.3.1 Wasm SDK. That means a projector `register(...)` call in
//   the sample never fires and this library cannot read projectors out of a shared
//   `MvRegistry`.
// * Defining the `@_cdecl` exports in the sample instead lets each MV export eagerly build its
//   own projector list from local state, sidestepping the lazy-init trap entirely.
//
// The helpers below accept the projector list as an argument so the sample can pass it in once
// per call (cheap — a handful of value-type instances) and get a fully encoded batch JSON
// response back.

public enum MvExportHelpers {
    public static func metadata(_ projectors: [any WasmMvProjector]) -> Int64 {
        let meta = projectors.map {
            WasmMvMetadata(
                viewName: $0.viewName,
                viewVersion: $0.viewVersion,
                logicalTables: $0.logicalTables)
        }
        return writeJSON(meta)
    }

    public static func initialize(
        _ projectors: [any WasmMvProjector],
        viewNamePtr: Int32, viewNameLen: Int32,
        viewVersion: Int32,
        bindingsPtr: Int32, bindingsLen: Int32
    ) -> Int64 {
        let viewName = readString(ptr: viewNamePtr, len: viewNameLen)
        let bindingsJson = readString(ptr: bindingsPtr, len: bindingsLen)
        guard let projector = lookup(projectors, viewName: viewName, viewVersion: viewVersion) else {
            let known = projectors
                .map { "\($0.viewName)/\($0.viewVersion)" }
                .joined(separator: ",")
            return writeErrorEnvelope(
                "unknown view \(viewName)/\(viewVersion). known=[\(known)] requested_len=\(projectors.count)")
        }
        guard let data = bindingsJson.data(using: .utf8) else {
            return writeErrorEnvelope("bindings are not valid UTF-8")
        }
        let bindings: MvTableBindingsDto
        do {
            bindings = try JSONDecoder().decode(MvTableBindingsDto.self, from: data)
        } catch {
            return writeErrorEnvelope("parse bindings: \(error.localizedDescription)")
        }
        let statements = projector.initialize(tables: bindings)
        return writeJSON(MvStatementBatchDto(statements: statements))
    }

    public static func applyEvent(
        _ projectors: [any WasmMvProjector],
        viewNamePtr: Int32, viewNameLen: Int32,
        viewVersion: Int32,
        bindingsPtr: Int32, bindingsLen: Int32,
        eventPtr: Int32, eventLen: Int32
    ) -> Int64 {
        let viewName = readString(ptr: viewNamePtr, len: viewNameLen)
        let bindingsJson = readString(ptr: bindingsPtr, len: bindingsLen)
        let eventJson = readString(ptr: eventPtr, len: eventLen)
        guard let projector = lookup(projectors, viewName: viewName, viewVersion: viewVersion) else {
            return writeErrorEnvelope("unknown view \(viewName)/\(viewVersion)")
        }
        guard let bData = bindingsJson.data(using: .utf8) else {
            return writeErrorEnvelope("bindings are not valid UTF-8")
        }
        guard let eData = eventJson.data(using: .utf8) else {
            return writeErrorEnvelope("event is not valid UTF-8")
        }
        let bindings: MvTableBindingsDto
        let event: MvSerializableEventDto
        do {
            bindings = try JSONDecoder().decode(MvTableBindingsDto.self, from: bData)
        } catch {
            return writeErrorEnvelope("parse bindings: \(error.localizedDescription)")
        }
        do {
            event = try JSONDecoder().decode(MvSerializableEventDto.self, from: eData)
        } catch {
            return writeErrorEnvelope("parse event: \(error.localizedDescription)")
        }
        let statements = projector.applyEvent(
            tables: bindings,
            event: event,
            queryPort: HostBackedMvQueryPort())
        return writeJSON(MvStatementBatchDto(statements: statements))
    }

    private static func lookup(
        _ projectors: [any WasmMvProjector],
        viewName: String,
        viewVersion: Int32
    ) -> (any WasmMvProjector)? {
        for p in projectors where p.viewName == viewName && p.viewVersion == viewVersion {
            return p
        }
        return nil
    }
}
