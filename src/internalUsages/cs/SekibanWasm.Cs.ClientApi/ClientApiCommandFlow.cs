using System.Text.Json;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.WasmRuntime;

namespace SekibanWasm.Cs.ClientApi;

public class ClientApiCommandFlow
{
    private readonly ISerializedDcbClient _client;
    private readonly JsonSerializerOptions _jsonOptions;

    public ClientApiCommandFlow(ISerializedDcbClient client, JsonSerializerOptions jsonOptions)
    {
        _client = client;
        _jsonOptions = jsonOptions;
    }

    public async Task<IResult> ExecuteAndCommit(
        string commandName,
        object command,
        CancellationToken ct)
    {
        var executeRequest = new SerializedCommandExecuteRequest(
            CommandName: commandName,
            CommandJson: JsonSerializer.Serialize(command, command.GetType(), _jsonOptions),
            ConsistencyTags: null,
            Options: null);

        var executeResult = await _client.ExecuteSerializedCommandAsync(executeRequest, ct);
        if (!executeResult.IsSuccess)
        {
            return Results.BadRequest(new { error = executeResult.GetException().Message });
        }

        var executeResponse = executeResult.GetValue();

        var eventCandidates = executeResponse.EventCandidates
            .Select(ec => new SerializableEventCandidate(
                Payload: Convert.FromBase64String(ec.PayloadBase64),
                EventPayloadName: ec.EventPayloadName,
                Tags: ec.Tags.ToList()))
            .ToList();

        var commitRequest = new SerializedCommitRequest(
            EventCandidates: eventCandidates,
            ConsistencyTags: executeResponse.ConsistencyTags);

        var commitResult = await _client.CommitSerializableEventsAsync(commitRequest, ct);
        if (!commitResult.IsSuccess)
        {
            return Results.BadRequest(new { error = commitResult.GetException().Message });
        }

        return Results.Ok(commitResult.GetValue());
    }
}
