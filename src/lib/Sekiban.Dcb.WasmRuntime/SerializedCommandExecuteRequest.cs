using Sekiban.Dcb.Commands;

namespace Sekiban.Dcb.WasmRuntime;

public record SerializedCommandExecuteRequest(
    string CommandName,
    string CommandJson,
    IReadOnlyList<ConsistencyTagEntry>? ConsistencyTags,
    SerializedCommandOptions? Options);

public record SerializedCommandOptions(
    bool DryRun,
    string? WaitForSortableUniqueId);
