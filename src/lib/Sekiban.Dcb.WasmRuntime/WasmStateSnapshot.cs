namespace Sekiban.Dcb.WasmRuntime;

public record WasmStateSnapshot(
    string StateJson,
    int SafeVersion,
    int UnsafeVersion,
    string? SafeLastSortableUniqueId,
    string? LastSortableUniqueId,
    Guid? LastEventId);
