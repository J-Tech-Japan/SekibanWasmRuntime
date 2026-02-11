using System.Net.Http.Json;
using System.Text.Json;
using Sekiban.Dcb.Primitives;

namespace Sekiban.Dcb.WasmRuntime.Remote;

public class RemotePrimitiveProjectionInstance : IPrimitiveProjectionInstance
{
    private readonly HttpClient _httpClient;
    private readonly string _endpoint;
    private readonly string _instanceId;

    public RemotePrimitiveProjectionInstance(
        HttpClient httpClient,
        string endpoint,
        string instanceId)
    {
        _httpClient = httpClient;
        _endpoint = endpoint.TrimEnd('/');
        _instanceId = instanceId;
    }

    public void ApplyEvent(
        string eventType,
        string eventPayloadJson,
        IReadOnlyList<string> tags,
        string? sortableUniqueId)
    {
        var request = new
        {
            events = new[]
            {
                new { eventType, payloadJson = eventPayloadJson, tags, sortableUniqueId }
            }
        };
        var response = _httpClient
            .PostAsJsonAsync($"{_endpoint}/v1/instances/{_instanceId}/apply-events", request)
            .GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
    }

    public string ExecuteQuery(string queryType, string queryParamsJson)
    {
        var request = new { queryType, queryParamsJson };
        var response = _httpClient
            .PostAsJsonAsync($"{_endpoint}/v1/instances/{_instanceId}/query", request)
            .GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();

        return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
    }

    public string ExecuteListQuery(string queryType, string queryParamsJson)
    {
        var request = new { queryType, queryParamsJson };
        var response = _httpClient
            .PostAsJsonAsync($"{_endpoint}/v1/instances/{_instanceId}/list-query", request)
            .GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();

        return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
    }

    public string SerializeState()
    {
        var response = _httpClient
            .GetAsync($"{_endpoint}/v1/instances/{_instanceId}/snapshot")
            .GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();

        return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
    }

    public void RestoreState(string stateJson)
    {
        var content = new StringContent(stateJson, System.Text.Encoding.UTF8, "application/json");
        var response = _httpClient
            .PutAsync($"{_endpoint}/v1/instances/{_instanceId}/snapshot", content)
            .GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
    }

    public void Dispose()
    {
        try
        {
            _httpClient
                .DeleteAsync($"{_endpoint}/v1/instances/{_instanceId}")
                .GetAwaiter().GetResult();
        }
        catch
        {
            // Best-effort cleanup for remote instances
        }
    }
}
