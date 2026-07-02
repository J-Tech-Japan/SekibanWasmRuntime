import Foundation

/// Minimal query port protocol a projector can use to consult already-projected rows mid-apply.
/// The default `HostBackedMvQueryPort` in `QueryPort.swift` routes through the host import
/// `env.mv_host_query_rows`. Projectors that don't need mid-apply queries can ignore the
/// argument entirely (the sample `ClassRoomEnrollmentMvV1` does).
public protocol MvQueryPort {
    func queryRows(_ sql: String, _ params: [MvParam]) -> [MvQueryRowDto]
    func querySingleRow(_ sql: String, _ params: [MvParam]) -> MvQueryRowDto?
}

/// Protocol a WASM-side materialized view projector implements. Mirrors the Rust
/// `WasmMvProjector` trait one-for-one.
public protocol WasmMvProjector: Sendable {
    var viewName: String { get }
    var viewVersion: Int32 { get }
    var logicalTables: [String] { get }

    func initialize(tables: MvTableBindingsDto) -> [MvSqlStatementDto]
    func applyEvent(
        tables: MvTableBindingsDto,
        event: MvSerializableEventDto,
        queryPort: MvQueryPort
    ) -> [MvSqlStatementDto]
}

/// Singleton registry of projectors. Populated at module init time via `register(_:)` (typically
/// from `@main`-style code in the wasm executable). Dispatches `mv_metadata`/`mv_initialize`/
/// `mv_apply_event` lookups by `(viewName, viewVersion)`.
public final class MvRegistry: @unchecked Sendable {
    public static let shared = MvRegistry()
    private var byKey: [String: WasmMvProjector] = [:]
    private init() {}

    public func register(_ projector: WasmMvProjector) {
        byKey[Self.key(projector.viewName, projector.viewVersion)] = projector
    }

    public func projector(viewName: String, viewVersion: Int32) -> WasmMvProjector? {
        byKey[Self.key(viewName, viewVersion)]
    }

    public func allProjectors() -> [WasmMvProjector] {
        Array(byKey.values)
    }

    private static func key(_ name: String, _ version: Int32) -> String {
        "\(name)/\(version)"
    }
}
