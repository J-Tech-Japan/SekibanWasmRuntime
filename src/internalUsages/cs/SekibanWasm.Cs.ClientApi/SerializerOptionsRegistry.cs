using System.Text.Json;

namespace SekibanWasm.Cs.ClientApi;

public sealed record DomainSerializerOptions(JsonSerializerOptions Value);

public sealed record TransportSerializerOptions(JsonSerializerOptions Value);
