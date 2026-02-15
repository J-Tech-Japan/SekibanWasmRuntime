using System.Text;
using System.Text.Json;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Primitives;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.WasmRuntime;

/// <summary>
/// Bridges IPrimitiveProjectionInstance (WASM level) and SerializableTagState (Sekiban level).
/// Follows the same accumulator contract as native: ApplyState, ApplyEvents, GetSerializedState.
/// </summary>
public class WasmTagStateProjectionPrimitive : IDisposable
{
    private readonly IPrimitiveProjectionInstance _instance;
    private readonly string _projectorName;
    private readonly string _projectorVersion;
    private readonly JsonSerializerOptions _jsonOptions;

    private int _version;
    private string? _lastSortedUniqueId;
    private string? _tagPayloadName;
    private string? _tagGroup;
    private string? _tagContent;

    public WasmTagStateProjectionPrimitive(
        IPrimitiveProjectionInstance instance,
        string projectorName,
        string projectorVersion,
        JsonSerializerOptions jsonOptions)
    {
        _instance = instance;
        _projectorName = projectorName;
        _projectorVersion = projectorVersion;
        _jsonOptions = jsonOptions;
    }

    public int Version => _version;
    public string? LastSortedUniqueId => _lastSortedUniqueId;
    public string? TagPayloadName => _tagPayloadName;
    public string ProjectorVersion => _projectorVersion;
    public string? TagGroup => _tagGroup;
    public string? TagContent => _tagContent;

    public void ApplyState(SerializableTagState? state)
    {
        if (state is null)
        {
            return;
        }

        if (state.ProjectorVersion != _projectorVersion)
        {
            // Version mismatch: reset to initial state per native contract
            return;
        }

        var payloadJson = Encoding.UTF8.GetString(state.Payload);
        _instance.RestoreState(payloadJson);
        _version = state.Version;
        _lastSortedUniqueId = state.LastSortedUniqueId;
        _tagPayloadName = state.TagPayloadName;
        _tagGroup = state.TagGroup;
        _tagContent = state.TagContent;
    }

    public void ApplyEvents(IReadOnlyList<Event> events, string? safeWindowThreshold)
    {
        foreach (var ev in events)
        {
            var payloadJson = JsonSerializer.Serialize(
                ev.Payload, ev.Payload.GetType(), _jsonOptions);

            _instance.ApplyEvent(
                ev.EventType, payloadJson, ev.Tags, ev.SortableUniqueIdValue);

            _version++;
            _lastSortedUniqueId = ev.SortableUniqueIdValue;

            UpdateTagMetadataFromEvent(ev);
        }
    }

    public SerializableTagState GetSerializedState()
    {
        var payloadJson = _instance.SerializeState();
        var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);

        return new SerializableTagState(
            Payload: payloadBytes,
            Version: _version,
            LastSortedUniqueId: _lastSortedUniqueId ?? string.Empty,
            ProjectorVersion: _projectorVersion,
            TagPayloadName: _tagPayloadName ?? string.Empty,
            TagGroup: _tagGroup ?? string.Empty,
            TagContent: _tagContent ?? string.Empty,
            TagProjector: _projectorName);
    }

    private void UpdateTagMetadataFromEvent(Event ev)
    {
        if (ev.Tags.Count == 0)
        {
            return;
        }

        // Tags in Event are serialized as "group:content" strings
        var firstTag = ev.Tags[0];
        var separatorIndex = firstTag.IndexOf(':');
        if (separatorIndex >= 0)
        {
            _tagGroup = firstTag[..separatorIndex];
            _tagContent = firstTag[(separatorIndex + 1)..];
        }
        else
        {
            _tagGroup = firstTag;
            _tagContent = string.Empty;
        }

        _tagPayloadName = ev.Payload.GetType().Name;
    }

    public void Dispose() => _instance.Dispose();
}
