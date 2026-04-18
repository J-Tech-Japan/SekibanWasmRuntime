import Foundation

// Wire DTOs crossing the WASM <-> host boundary. Shape is byte-for-byte compatible with the
// C# DTOs declared in `Sekiban.Dcb.WasmRuntime.Host.MaterializedView.WasmMvBoundaryContracts`
// and with the Rust `sekiban_mv::dto::*` types: camelCase JSON keys, MvParamKind encoded as an
// Int 0..9, and the same field layout on every DTO.

/// Kinds of scalar parameter values supported across the WASM boundary. Numeric values MUST
/// match `WasmMvParamKind` on the host side (0..9). Swift's synthesized Codable for `Int`-backed
/// enums emits the raw integer automatically.
public enum MvParamKind: Int, Codable, Sendable {
    case null = 0
    case string = 1
    case int32 = 2
    case int64 = 3
    case boolean = 4
    case guid = 5
    case dateTimeOffset = 6
    case decimal = 7
    case double = 8
    case bytes = 9
}

public struct MvParam: Codable, Sendable {
    public var name: String
    public var kind: MvParamKind
    /// Raw JSON token for the value. `nil` iff `kind == .null`.
    public var valueJson: String?

    public init(name: String, kind: MvParamKind, valueJson: String?) {
        self.name = name
        self.kind = kind
        self.valueJson = valueJson
    }
}

public struct MvSqlStatementDto: Codable, Sendable {
    public var sql: String
    public var parameters: [MvParam]

    public init(sql: String, parameters: [MvParam] = []) {
        self.sql = sql
        self.parameters = parameters
    }
}

public struct MvStatementBatchDto: Codable, Sendable {
    public var statements: [MvSqlStatementDto]
    public init(statements: [MvSqlStatementDto] = []) {
        self.statements = statements
    }
}

public struct MvTableBindingEntry: Codable, Sendable {
    public var logical: String
    public var physical: String
    public init(logical: String, physical: String) {
        self.logical = logical
        self.physical = physical
    }
}

public struct MvTableBindingsDto: Codable, Sendable {
    public var bindings: [MvTableBindingEntry]
    public init(bindings: [MvTableBindingEntry] = []) {
        self.bindings = bindings
    }

    /// Returns the physical name registered for a logical table. Missing entries are surfaced as
    /// a sentinel string (mirroring the Rust impl) so the resulting SQL fails with a
    /// human-readable error instead of a silent empty-string substitution.
    public func getPhysicalName(_ logical: String) -> String {
        for entry in bindings where entry.logical == logical {
            return entry.physical
        }
        return "__missing_binding_\(logical)__"
    }
}

public struct WasmMvMetadata: Codable, Sendable {
    public var viewName: String
    public var viewVersion: Int32
    public var logicalTables: [String]

    public init(viewName: String, viewVersion: Int32, logicalTables: [String]) {
        self.viewName = viewName
        self.viewVersion = viewVersion
        self.logicalTables = logicalTables
    }
}

public struct MvSerializableEventDto: Codable, Sendable {
    public var eventType: String
    public var payloadJson: String
    public var sortableUniqueId: String
    public var tags: [String]

    public init(eventType: String, payloadJson: String, sortableUniqueId: String, tags: [String] = []) {
        self.eventType = eventType
        self.payloadJson = payloadJson
        self.sortableUniqueId = sortableUniqueId
        self.tags = tags
    }
}

/// Row returned by a `mv_host_query_rows` callback. Columns are stringified JSON tokens so a
/// projector can downcast to whatever concrete type it needs.
public struct MvQueryRowDto: Codable, Sendable {
    public var columns: [String: String?]
    public init(columns: [String: String?] = [:]) {
        self.columns = columns
    }
}

public struct MvQueryResultDto: Codable, Sendable {
    public var rows: [MvQueryRowDto]
    public init(rows: [MvQueryRowDto] = []) {
        self.rows = rows
    }
}
