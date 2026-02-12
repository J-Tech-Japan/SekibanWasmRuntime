using System.Text.Json.Serialization;

namespace SekibanWasm.Cs.Wasm;

[JsonSerializable(typeof(LocationQuery))]
[JsonSerializable(typeof(CountResult))]
[JsonSerializable(typeof(List<string>))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
public partial class WasmJsonContext : JsonSerializerContext
{
}

public record LocationQuery(string? LocationFilter);
public record CountResult(int Count);
