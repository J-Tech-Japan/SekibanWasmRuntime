using System.Text.Json.Serialization;
using PublicContainerCsDecider.Wasm.MaterializedView;

namespace PublicContainerCsDecider.Wasm;

[JsonSerializable(typeof(LocationQuery))]
[JsonSerializable(typeof(CountResult))]
// Materialized view boundary contracts (mv_metadata / mv_initialize / mv_apply_event +
// the mv_host_query_rows callback). Source-generated so the AOT WASM guest serializes them
// without reflection.
[JsonSerializable(typeof(List<WasmMvMetadata>))]
[JsonSerializable(typeof(MvTableBindingsDto))]
[JsonSerializable(typeof(MvStatementBatchDto))]
[JsonSerializable(typeof(MvSerializableEventDto))]
[JsonSerializable(typeof(MvQueryResultDto))]
[JsonSerializable(typeof(List<MvParam>))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
public partial class WasmJsonContext : JsonSerializerContext
{
}

public record LocationQuery(string? LocationFilter, string? ForecastId = null, bool IncludeDeleted = false);
public record CountResult(int Count);
