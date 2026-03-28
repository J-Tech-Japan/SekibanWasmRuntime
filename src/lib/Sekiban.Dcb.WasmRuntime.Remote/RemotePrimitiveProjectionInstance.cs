using System.Net.Http.Json;
using System.Text;
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
            .PostAsJsonAsync($"{_endpoint}/v1/instances/{_instanceId}/events", request)
            .GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
    }

    public void ApplyEvents(IReadOnlyList<PrimitiveProjectionEventEnvelope> events)
    {
        var request = new
        {
            events = events.Select(ev => new
            {
                eventType = ev.EventType,
                payloadJson = ev.EventPayloadJson,
                tags = ev.Tags,
                sortableUniqueId = ev.SortableUniqueId
            })
        };
        var response = _httpClient
            .PostAsJsonAsync($"{_endpoint}/v1/instances/{_instanceId}/events", request)
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

        var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("resultJson").GetString()!;
    }

    public string ExecuteListQuery(string queryType, string queryParamsJson)
    {
        var request = new { queryType, queryParamsJson };
        var response = _httpClient
            .PostAsJsonAsync($"{_endpoint}/v1/instances/{_instanceId}/list-query", request)
            .GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();

        var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("resultJson").GetString()!;
    }

    public string SerializeState()
    {
        var response = _httpClient
            .GetAsync($"{_endpoint}/v1/instances/{_instanceId}/snapshot")
            .GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();

        var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("stateJson").GetString()!;
    }

    public void RestoreState(string stateJson)
    {
        var envelope = JsonSerializer.Serialize(new { stateJson });
        var content = new StringContent(envelope, Encoding.UTF8, "application/json");
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
