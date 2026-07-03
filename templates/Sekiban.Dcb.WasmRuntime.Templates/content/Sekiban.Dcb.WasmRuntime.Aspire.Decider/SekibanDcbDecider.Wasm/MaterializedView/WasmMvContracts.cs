using System.Text.Json.Serialization;

namespace SekibanDcbDecider.Wasm.MaterializedView;

// WASM-internal materialized view contracts. Mirror the concepts in
// Sekiban.Dcb.MaterializedView (IMaterializedViewProjector / IMvApplyContext) but avoid
// referencing that assembly from the WASM module — it uses System.Linq.Expressions-backed
// MvRowMapper which is not AOT compatible. The runtime host bridges these WASM-internal
// types back into Sekiban's MV runtime via the mv_* exports and the mv_host_* imports.

/// <summary>
/// Kinds of scalar parameter values we support across the WASM boundary.
/// </summary>
public enum MvParamKind
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

/// <summary>
/// A single named SQL parameter carried across the WASM boundary. The value is held as a JSON
/// token whose shape depends on <see cref="Kind"/>.
/// </summary>
public sealed class MvParam
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public MvParamKind Kind { get; set; }

    [JsonPropertyName("valueJson")]
    public string? ValueJson { get; set; }
}

/// <summary>
/// A SQL statement produced by a materialized view projector, ready for the host to execute
/// with Dapper in the apply transaction.
/// </summary>
public sealed class MvSqlStatementDto
{
    [JsonPropertyName("sql")]
    public string Sql { get; set; } = string.Empty;

    [JsonPropertyName("parameters")]
    public List<MvParam> Parameters { get; set; } = new();
}

/// <summary>
/// Logical → physical table name map passed by the host for each `mv_*` WASM call.
/// </summary>
public sealed class MvTableBindingsDto
{
    [JsonPropertyName("bindings")]
    public List<MvTableBindingEntry> Bindings { get; set; } = new();

    public string GetPhysicalName(string logicalName) =>
        Bindings.FirstOrDefault(b => string.Equals(b.Logical, logicalName, StringComparison.Ordinal))?.Physical
            ?? throw new InvalidOperationException($"Materialized view table '{logicalName}' has no physical binding.");
}

public sealed class MvTableBindingEntry
{
    [JsonPropertyName("logical")]
    public string Logical { get; set; } = string.Empty;

    [JsonPropertyName("physical")]
    public string Physical { get; set; } = string.Empty;
}

/// <summary>
/// Metadata describing a materialized view projector, returned by the `mv_metadata` export so the
/// host can enumerate registered MVs at startup.
/// </summary>
public sealed class WasmMvMetadata
{
    [JsonPropertyName("viewName")]
    public string ViewName { get; set; } = string.Empty;

    [JsonPropertyName("viewVersion")]
    public int ViewVersion { get; set; }

    [JsonPropertyName("logicalTables")]
    public List<string> LogicalTables { get; set; } = new();
}

/// <summary>
/// Serializable representation of a Sekiban event, constructed by the host before calling
/// `mv_apply_event`.
/// </summary>
public sealed class MvSerializableEventDto
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

/// <summary>
/// Response envelope returned by `mv_initialize` / `mv_apply_event`.
/// </summary>
public sealed class MvStatementBatchDto
{
    [JsonPropertyName("statements")]
    public List<MvSqlStatementDto> Statements { get; set; } = new();
}

/// <summary>
/// A single row returned by an `mv_host_query_*` host import.
/// </summary>
public sealed class MvQueryRowDto
{
    [JsonPropertyName("columns")]
    public Dictionary<string, string?> Columns { get; set; } = new();
}

public sealed class MvQueryResultDto
{
    [JsonPropertyName("rows")]
    public List<MvQueryRowDto> Rows { get; set; } = new();
}

/// <summary>
/// Query port exposed to WASM projectors. Each call round-trips through a host import so the
/// host can run Dapper against the apply-time connection/transaction.
/// </summary>
public interface IWasmMvQueryPort
{
    MvQueryRowDto? QuerySingleOrDefaultRow(string sql, IReadOnlyList<MvParam> parameters);
    IReadOnlyList<MvQueryRowDto> QueryRows(string sql, IReadOnlyList<MvParam> parameters);
    string? ExecuteScalarJson(string sql, IReadOnlyList<MvParam> parameters);
}

/// <summary>
/// WASM-internal projector contract. A WASM MV projector defines its logical tables, emits DDL in
/// Initialize, and transforms one event into zero-or-more SQL statements in ApplyEvent.
/// </summary>
public interface IWasmMvProjector
{
    string ViewName { get; }
    int ViewVersion { get; }
    IReadOnlyList<string> LogicalTables { get; }

    IReadOnlyList<MvSqlStatementDto> Initialize(MvTableBindingsDto tables);

    IReadOnlyList<MvSqlStatementDto> ApplyEvent(
        MvTableBindingsDto tables,
        MvSerializableEventDto serializableEvent,
        IWasmMvQueryPort queryPort);
}
