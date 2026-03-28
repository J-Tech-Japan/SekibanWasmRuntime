using System.Text;
using System.Text.Json;
using System.Linq;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Primitives;
using Sekiban.Dcb.Runtime;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.WasmRuntime;

/// <summary>
/// Bridges IPrimitiveProjectionInstance (WASM level) and SerializableTagState (Sekiban level).
/// Follows the same accumulator contract as native: ApplyState, ApplyEvents, GetSerializedState.
/// </summary>
public class WasmTagStateProjectionPrimitive : ITagStateProjectionAccumulator, IDisposable
{
    private readonly IPrimitiveProjectionInstance _instance;
    private readonly string _projectorName;
    private readonly string _projectorVersion;
    private readonly IEventTypes _eventTypes;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly string? _defaultTagPayloadName;

    private SerializableTagState? _cachedState;
    private int _version;
    private string? _lastSortedUniqueId;
    private string? _tagPayloadName;
    private string? _tagGroup;
    private string? _tagContent;
    private bool _hasChanges;

    public WasmTagStateProjectionPrimitive(
        IPrimitiveProjectionInstance instance,
        string projectorName,
        string projectorVersion,
        IEventTypes eventTypes,
        JsonSerializerOptions jsonOptions,
        string? defaultTagPayloadName = null)
    {
        _instance = instance;
        _projectorName = projectorName;
        _projectorVersion = projectorVersion;
        _eventTypes = eventTypes;
        _jsonOptions = jsonOptions;
        _defaultTagPayloadName = defaultTagPayloadName;
    }

    public int Version => _version;
    public string? LastSortedUniqueId => _lastSortedUniqueId;
    public string? TagPayloadName => _tagPayloadName;
    public string ProjectorVersion => _projectorVersion;
    public string? TagGroup => _tagGroup;
    public string? TagContent => _tagContent;

    public bool ApplyState(SerializableTagState? state)
    {
        _cachedState = state;
        _hasChanges = false;

        if (state is null)
        {
            ResetToInitial();
            return true;
        }

        if (state.ProjectorVersion != _projectorVersion ||
            state.TagProjector != _projectorName)
        {
            // Version mismatch: reset to initial state per native contract
            ResetToInitial();
            return true;
        }

        try
        {
            _instance.RestoreState(BuildRestoreSnapshotJson(state));
            _version = state.Version;
            _lastSortedUniqueId = state.LastSortedUniqueId;
            _tagPayloadName = state.TagPayloadName;
            _tagGroup = state.TagGroup;
            _tagContent = state.TagContent;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool ApplyEvents(
        IReadOnlyList<SerializableEvent> events,
        string? latestSortableUniqueId,
        CancellationToken cancellationToken = default)
    {
        foreach (var serializableEvent in events.OrderBy(e => e.SortableUniqueIdValue, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.IsNullOrEmpty(_lastSortedUniqueId) &&
                string.Compare(serializableEvent.SortableUniqueIdValue, _lastSortedUniqueId, StringComparison.Ordinal) <= 0)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(latestSortableUniqueId) &&
                string.Compare(serializableEvent.SortableUniqueIdValue, latestSortableUniqueId, StringComparison.Ordinal) > 0)
            {
                continue;
            }

            ApplySerializableEvent(serializableEvent);
        }

        return true;
    }

    public void ApplyEvents(IReadOnlyList<Event> events, string? safeWindowThreshold)
    {
        foreach (var ev in events)
        {
            ApplyEvent(ev);
        }
    }

    public SerializableTagState GetSerializedState()
    {
        if (!_hasChanges && _cachedState != null)
        {
            return _cachedState;
        }

        var serializedState = _instance.SerializeState();
        var (payloadJson, payloadName, projectorVersion, tagGroup, tagContent, tagProjector) =
            ExtractSerializedStateMetadata(serializedState);
        var isEmptyPayload = string.IsNullOrWhiteSpace(payloadJson) || payloadJson == "{}";
        var payloadBytes = isEmptyPayload ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(payloadJson);
        var effectivePayloadName = isEmptyPayload
            ? nameof(EmptyTagStatePayload)
            : payloadName ?? _tagPayloadName ?? _defaultTagPayloadName ?? string.Empty;

        return new SerializableTagState(
            Payload: payloadBytes,
            Version: _version,
            LastSortedUniqueId: _lastSortedUniqueId ?? string.Empty,
            ProjectorVersion: projectorVersion ?? _projectorVersion,
            TagPayloadName: effectivePayloadName,
            TagGroup: tagGroup ?? _tagGroup ?? string.Empty,
            TagContent: tagContent ?? _tagContent ?? string.Empty,
            TagProjector: tagProjector ?? _projectorName);
    }

    private void ApplyEvent(Event ev)
    {
        var payloadJson = JsonSerializer.Serialize(
            ev.Payload, ev.Payload.GetType(), _jsonOptions);

        _instance.ApplyEvent(
            ev.EventType, payloadJson, ev.Tags, ev.SortableUniqueIdValue);

        _version++;
        _lastSortedUniqueId = ev.SortableUniqueIdValue;
        _hasChanges = true;

        UpdateTagMetadataFromEvent(ev);
    }

    private void ApplySerializableEvent(SerializableEvent ev)
    {
        string payloadJson = Encoding.UTF8.GetString(ev.Payload);
        _instance.ApplyEvent(
            ev.EventPayloadName,
            payloadJson,
            ev.Tags,
            ev.SortableUniqueIdValue);

        _version++;
        _lastSortedUniqueId = ev.SortableUniqueIdValue;
        _hasChanges = true;
        UpdateTagMetadataFromSerializableEvent(ev);
    }

    private void ResetToInitial()
    {
        _version = 0;
        _lastSortedUniqueId = null;
        _tagPayloadName = nameof(EmptyTagStatePayload);
        _tagGroup = null;
        _tagContent = null;
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

        _tagPayloadName = _defaultTagPayloadName ?? ev.Payload.GetType().Name;
    }

    private void UpdateTagMetadataFromSerializableEvent(SerializableEvent ev)
    {
        if (ev.Tags.Count == 0)
        {
            return;
        }

        string firstTag = ev.Tags[0];
        int separatorIndex = firstTag.IndexOf(':');
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

        if (string.IsNullOrWhiteSpace(_tagPayloadName))
        {
            _tagPayloadName = _defaultTagPayloadName;
        }
    }

    private string BuildRestoreSnapshotJson(SerializableTagState state)
    {
        string payloadJson = state.Payload.Length == 0
            ? "{}"
            : Encoding.UTF8.GetString(state.Payload);

        return WasmRuntimeJsonContext.SerializeSnapshot(
            new WasmStateSnapshot(
                StateJson: payloadJson,
                SafeVersion: state.Version,
                UnsafeVersion: state.Version,
                SafeLastSortableUniqueId: state.LastSortedUniqueId,
                LastSortableUniqueId: state.LastSortedUniqueId,
                LastEventId: null,
                TagPayloadName: state.TagPayloadName,
                ProjectorVersion: state.ProjectorVersion,
                TagGroup: state.TagGroup,
                TagContent: state.TagContent,
                TagProjector: state.TagProjector));
    }

    private (string PayloadJson, string? PayloadName, string? ProjectorVersion, string? TagGroup, string? TagContent, string? TagProjector)
        ExtractSerializedStateMetadata(string serializedState)
    {
        var snapshot = WasmRuntimeJsonContext.DeserializeSnapshot(serializedState);
        if (snapshot is null || snapshot.StateJson is null)
        {
            return (serializedState, null, null, null, null, null);
        }

        return (
            snapshot.StateJson,
            snapshot.TagPayloadName,
            snapshot.ProjectorVersion,
            snapshot.TagGroup,
            snapshot.TagContent,
            snapshot.TagProjector);
    }

    public void Dispose() => _instance.Dispose();
}
