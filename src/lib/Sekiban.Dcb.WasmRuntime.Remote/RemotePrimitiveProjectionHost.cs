using System.Net.Http.Json;
using System.Text.Json;
using Sekiban.Dcb.Primitives;

namespace Sekiban.Dcb.WasmRuntime.Remote;

public class RemotePrimitiveProjectionHost : IPrimitiveProjectionHost
{
    private readonly HttpClient _httpClient;
    private readonly RemoteRunnerOptions _options;

    public RemotePrimitiveProjectionHost(HttpClient httpClient, RemoteRunnerOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public IPrimitiveProjectionInstance CreateInstance(string projectorName)
    {
        var endpoint = _options.Endpoint.TrimEnd('/');
        var request = new
        {
            projectorName,
            instanceKey = $"{projectorName}/{Guid.NewGuid()}"
        };

        var response = _httpClient
            .PostAsJsonAsync($"{endpoint}/v1/instances", request)
            .GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();

        var resultJson = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        var result = JsonSerializer.Deserialize<JsonElement>(resultJson);
        var instanceId = result.GetProperty("instanceId").GetString()
            ?? throw new InvalidOperationException("Remote runner did not return instanceId");

        return new RemotePrimitiveProjectionInstance(_httpClient, endpoint, instanceId);
    }
}
