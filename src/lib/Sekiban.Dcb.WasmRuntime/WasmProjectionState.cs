using System.Text.Json;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Primitives;
using Sekiban.Dcb.Runtime;

namespace Sekiban.Dcb.WasmRuntime;

public class WasmProjectionState : IProjectionState, IDisposable
{
    public IPrimitiveProjectionInstance Instance { get; }
    public string ProjectorName { get; }

    public int SafeVersion { get; private set; }
    public int UnsafeVersion { get; private set; }
    public string? SafeLastSortableUniqueId { get; private set; }
    public string? LastSortableUniqueId { get; private set; }
    public Guid? LastEventId { get; private set; }

    public WasmProjectionState(IPrimitiveProjectionInstance instance, string projectorName)
    {
        Instance = instance;
        ProjectorName = projectorName;
    }

    public WasmProjectionState(
        IPrimitiveProjectionInstance instance,
        string projectorName,
        WasmStateSnapshot snapshot)
    {
        Instance = instance;
        ProjectorName = projectorName;
        SafeVersion = snapshot.SafeVersion;
        UnsafeVersion = snapshot.UnsafeVersion;
        SafeLastSortableUniqueId = snapshot.SafeLastSortableUniqueId;
        LastSortableUniqueId = snapshot.LastSortableUniqueId;
        LastEventId = snapshot.LastEventId;
    }

    public object? GetSafePayload() => null;
    public object? GetUnsafePayload() => null;

    public long EstimatePayloadSizeBytes(JsonSerializerOptions? options)
    {
        var json = Instance.SerializeState();
        return json.Length * 2; // UTF-16 estimate
    }

    public void UpdateMetadata(Event ev)
    {
        UnsafeVersion++;
        LastSortableUniqueId = ev.SortableUniqueIdValue;
        LastEventId = ev.Id;
    }

    public void Dispose() => Instance.Dispose();
}
