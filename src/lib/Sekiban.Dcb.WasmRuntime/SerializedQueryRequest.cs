namespace Sekiban.Dcb.WasmRuntime;

public record SerializedQueryRequest(
    string QueryType,
    string QueryParamsJson,
    string? WaitForSortableUniqueId = null);
