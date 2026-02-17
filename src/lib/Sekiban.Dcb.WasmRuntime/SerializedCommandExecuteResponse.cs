using Sekiban.Dcb.Commands;

namespace Sekiban.Dcb.WasmRuntime;

public record SerializedCommandExecuteResponse(
    IReadOnlyList<SerializedCommandEventCandidate> EventCandidates,
    IReadOnlyList<ConsistencyTagEntry> ConsistencyTags,
    string? CommandResultJson);

public record SerializedCommandEventCandidate(
    string EventPayloadName,
    string PayloadBase64,
    IReadOnlyList<string> Tags);
