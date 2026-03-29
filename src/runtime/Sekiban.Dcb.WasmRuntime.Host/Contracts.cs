namespace Sekiban.Dcb.WasmRuntime.Host;

public record TagStateRequest(string TagStateId);
public record TagLatestSortableRequest(string Tag);
public record TagLatestSortableResponse(bool Exists, string LastSortableUniqueId);
