using System.Text.Json.Serialization;

namespace PublicContainerCsDecider.Wasm;

[JsonSerializable(typeof(LocationQuery))]
[JsonSerializable(typeof(CountResult))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
public partial class WasmJsonContext : JsonSerializerContext
{
}

public record LocationQuery(string? LocationFilter, string? ForecastId = null, bool IncludeDeleted = false);
public record CountResult(int Count);
