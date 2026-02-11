using Sekiban.Dcb.Primitives;

namespace SekibanWasm.Tests;

public class FakePrimitiveProjectionInstance : IPrimitiveProjectionInstance
{
    private string _stateJson = "{}";

    public void ApplyEvent(string eventType, string eventPayloadJson, IReadOnlyList<string> tags, string? sortableUniqueId)
    {
        // Record event for assertion
        AppliedEvents.Add(new AppliedEvent(eventType, eventPayloadJson, tags.ToList(), sortableUniqueId));
    }

    public string ExecuteQuery(string queryType, string queryParamsJson)
    {
        return QueryResponses.TryGetValue(queryType, out var response) ? response : "null";
    }

    public string ExecuteListQuery(string queryType, string queryParamsJson)
    {
        return ListQueryResponses.TryGetValue(queryType, out var response) ? response : "[]";
    }

    public string SerializeState()
    {
        return _stateJson;
    }

    public void RestoreState(string stateJson)
    {
        _stateJson = stateJson;
    }

    public void Dispose()
    {
        IsDisposed = true;
    }

    // Test helpers
    public List<AppliedEvent> AppliedEvents { get; } = [];
    public Dictionary<string, string> QueryResponses { get; } = new();
    public Dictionary<string, string> ListQueryResponses { get; } = new();
    public bool IsDisposed { get; private set; }

    public void SetStateJson(string json) => _stateJson = json;

    public record AppliedEvent(string EventType, string PayloadJson, List<string> Tags, string? SortableUniqueId);
}
