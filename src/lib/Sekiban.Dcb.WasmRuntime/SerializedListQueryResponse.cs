namespace Sekiban.Dcb.WasmRuntime;

public record SerializedListQueryResponse(
    string ItemsJson,
    int? TotalCount,
    int? TotalPages,
    int? CurrentPage,
    int? PageSize);
