using System.Text.Json.Serialization;

namespace Sekiban.Dcb.WasmRuntime.Host.MaterializedView;

/// <summary>
/// Versioned identity for the JSON ABI shared by every MV guest and the host.
/// Keep this value stable: changing it is a contract break, not a display version.
/// </summary>
public static class WasmMvContract
{
    public const string AbiVersion = "sekiban-wasm-mv/1";
    public const string QueryRowsCapability = "query-rows";
    public const int MaxQueryRows = 1000;

    public static IReadOnlySet<string> SupportedCapabilities { get; } =
        new HashSet<string>(StringComparer.Ordinal) { QueryRowsCapability };
}

// Host-side mirrors of the DTO shapes defined inside the WASM module
// (SekibanDcbDecider.Wasm.MaterializedView.*). These types are the wire format for the
// `mv_metadata`, `mv_initialize`, `mv_apply_event` exports and the `mv_host_query_rows`
// import. Keep these definitions in sync with the WASM side:
// src/samples/.../SekibanDcbDecider.Wasm/MaterializedView/WasmMvContracts.cs

public enum WasmMvParamKind
{
    Null = 0,
    String = 1,
    Int32 = 2,
    Int64 = 3,
    Boolean = 4,
    Guid = 5,
    DateTimeOffset = 6,
    Decimal = 7,
    Double = 8,
    Bytes = 9
}

public sealed class WasmMvParam
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public WasmMvParamKind Kind { get; set; }

    [JsonPropertyName("valueJson")]
    public string? ValueJson { get; set; }
}

public sealed class WasmMvSqlStatementDto
{
    [JsonPropertyName("sql")]
    public string Sql { get; set; } = string.Empty;

    [JsonPropertyName("parameters")]
    public List<WasmMvParam> Parameters { get; set; } = new();
}

public sealed class WasmMvTableBindingEntry
{
    [JsonPropertyName("logical")]
    public string Logical { get; set; } = string.Empty;

    [JsonPropertyName("physical")]
    public string Physical { get; set; } = string.Empty;
}

public sealed class WasmMvTableBindingsDto
{
    [JsonPropertyName("bindings")]
    public List<WasmMvTableBindingEntry> Bindings { get; set; } = new();
}

public sealed class WasmMvMetadataDto
{
    [JsonPropertyName("abiVersion")]
    public string AbiVersion { get; set; } = string.Empty;

    [JsonPropertyName("capabilities")]
    public List<string> Capabilities { get; set; } = new();

    [JsonPropertyName("viewName")]
    public string ViewName { get; set; } = string.Empty;

    [JsonPropertyName("viewVersion")]
    public int ViewVersion { get; set; }

    [JsonPropertyName("logicalTables")]
    public List<string> LogicalTables { get; set; } = new();

    [JsonPropertyName("schema")]
    public List<WasmMvSchemaTableDto> Schema { get; set; } = new();
}

/// <summary>
/// Provider-neutral schema metadata emitted by a guest. Values deliberately mirror
/// Sekiban.Dcb.MaterializedView.MvSchemaTypeFamily and are append-only.
/// </summary>
public enum WasmMvSchemaTypeFamily
{
    Any = 0,
    String = 1,
    Integer = 2,
    Boolean = 3,
    DateTime = 4,
    Decimal = 5,
    FloatingPoint = 6,
    Binary = 7,
    Json = 8,
    Guid = 9
}

public sealed class WasmMvSchemaColumnDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("typeFamily")]
    public WasmMvSchemaTypeFamily TypeFamily { get; set; }

    [JsonPropertyName("isNullable")]
    public bool IsNullable { get; set; }

    [JsonPropertyName("defaultSql")]
    public string? DefaultSql { get; set; }

    [JsonPropertyName("isGenerated")]
    public bool? IsGenerated { get; set; }

    [JsonPropertyName("generationExpression")]
    public string? GenerationExpression { get; set; }

    [JsonPropertyName("maxLength")]
    public int? MaxLength { get; set; }

    [JsonPropertyName("precision")]
    public int? Precision { get; set; }

    [JsonPropertyName("scale")]
    public int? Scale { get; set; }
}

public sealed class WasmMvSchemaIndexDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("columns")]
    public List<string> Columns { get; set; } = new();

    [JsonPropertyName("isUnique")]
    public bool IsUnique { get; set; }
}

public sealed class WasmMvSchemaTableDto
{
    [JsonPropertyName("logicalTable")]
    public string LogicalTable { get; set; } = string.Empty;

    [JsonPropertyName("columns")]
    public List<WasmMvSchemaColumnDto> Columns { get; set; } = new();

    [JsonPropertyName("primaryKeyColumns")]
    public List<string> PrimaryKeyColumns { get; set; } = new();

    [JsonPropertyName("indexes")]
    public List<WasmMvSchemaIndexDto> Indexes { get; set; } = new();
}

public sealed class WasmMvSerializableEventDto
{
    [JsonPropertyName("eventType")]
    public string EventType { get; set; } = string.Empty;

    [JsonPropertyName("payloadJson")]
    public string PayloadJson { get; set; } = string.Empty;

    [JsonPropertyName("sortableUniqueId")]
    public string SortableUniqueId { get; set; } = string.Empty;

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();
}

public sealed class WasmMvStatementBatchDto
{
    [JsonPropertyName("statements")]
    public List<WasmMvSqlStatementDto> Statements { get; set; } = new();
}

public sealed class WasmMvQueryRowDto
{
    [JsonPropertyName("columns")]
    public Dictionary<string, string?> Columns { get; set; } = new();
}

public sealed class WasmMvQueryResultDto
{
    [JsonPropertyName("rows")]
    public List<WasmMvQueryRowDto> Rows { get; set; } = new();
}
