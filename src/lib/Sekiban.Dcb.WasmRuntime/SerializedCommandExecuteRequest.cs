using Sekiban.Dcb.Commands;

namespace Sekiban.Dcb.WasmRuntime;

public record SerializedCommandExecuteRequest(
    string CommandName,
    string CommandJson,
    IReadOnlyList<ConsistencyTagEntry>? ConsistencyTags,
    SerializedCommandOptions? Options)
{
    public IReadOnlyDictionary<string, string>? SupplementalProperties { get; init; }
}

public record SerializedCommandOptions(
    bool DryRun,
    string? WaitForSortableUniqueId);
