using System.Text.Json.Serialization;

namespace Sekiban.Dcb.WasmRuntime.Host.MaterializedView;

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
    [JsonPropertyName("viewName")]
    public string ViewName { get; set; } = string.Empty;

    [JsonPropertyName("viewVersion")]
    public int ViewVersion { get; set; }

    [JsonPropertyName("logicalTables")]
    public List<string> LogicalTables { get; set; } = new();
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
