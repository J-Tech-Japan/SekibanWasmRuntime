namespace Sekiban.Dcb.WasmRuntime;

public record WasmStateSnapshot(
    string StateJson,
    int SafeVersion,
    int UnsafeVersion,
    string? SafeLastSortableUniqueId,
    string? LastSortableUniqueId,
    Guid? LastEventId,
    string? TagPayloadName = null,
    string? ProjectorVersion = null,
    string? TagGroup = null,
    string? TagContent = null,
    string? TagProjector = null);
