using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sekiban.Dcb.WasmRuntime;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(WasmStateSnapshot))]
internal partial class WasmRuntimeJsonContext : JsonSerializerContext
{
    public static string SerializeSnapshot(WasmStateSnapshot snapshot) =>
        JsonSerializer.Serialize(snapshot, Default.WasmStateSnapshot);

    public static byte[] SerializeSnapshotToUtf8Bytes(WasmStateSnapshot snapshot) =>
        JsonSerializer.SerializeToUtf8Bytes(snapshot, Default.WasmStateSnapshot);

    public static WasmStateSnapshot? DeserializeSnapshot(string json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, Default.WasmStateSnapshot);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static WasmStateSnapshot? DeserializeSnapshot(ReadOnlySpan<byte> utf8Json)
    {
        try
        {
            return JsonSerializer.Deserialize(utf8Json, Default.WasmStateSnapshot);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
