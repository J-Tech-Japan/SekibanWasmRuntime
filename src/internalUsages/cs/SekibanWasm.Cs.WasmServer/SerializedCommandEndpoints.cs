using System.Reflection;
using System.Text;
using System.Text.Json;
using ResultBoxes;
using Sekiban.Dcb;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.WasmRuntime;

public class SerializedCommandEndpoints : ISerializedCommandExecutor
{
    private static readonly JsonSerializerOptions FallbackJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly MethodInfo ExecuteAsyncOpenMethod =
        typeof(ICommandExecutor)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(m =>
                m.Name == "ExecuteAsync"
                && m.IsGenericMethod
                && m.GetParameters().Length == 2
                && m.GetParameters()[0].ParameterType.IsGenericParameter
                && m.GetParameters()[1].ParameterType == typeof(CancellationToken));

    private readonly ISekibanExecutor _executor;
    private readonly SerializedCommandTypeRegistry _registry;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ISekibanCommandCommitRequestBuilder? _commitRequestBuilder;

    public SerializedCommandEndpoints(
        ISekibanExecutor executor,
        SerializedCommandTypeRegistry registry,
        JsonSerializerOptions jsonOptions)
        : this(executor, registry, jsonOptions, null)
    {
    }

    public SerializedCommandEndpoints(
        ISekibanExecutor executor,
        SerializedCommandTypeRegistry registry,
        JsonSerializerOptions jsonOptions,
        ISekibanCommandCommitRequestBuilder? commitRequestBuilder)
    {
        _executor = executor;
        _registry = registry;
        _jsonOptions = jsonOptions;
        _commitRequestBuilder = commitRequestBuilder;
    }

    public async Task<ResultBox<SerializedCommandExecuteResponse>> ExecuteAsync(
        SerializedCommandExecuteRequest request,
        CancellationToken cancellationToken)
    {
        object command;
        Type commandType;
        try
        {
            commandType = _registry.GetCommandType(request.CommandName);
            command = JsonSerializer.Deserialize(request.CommandJson, commandType, _jsonOptions)
                ?? throw new ArgumentException($"Failed to deserialize command: {request.CommandName}");
        }
        catch (Exception ex)
        {
            var source = ex is TargetInvocationException tie && tie.InnerException is not null
                ? tie.InnerException
                : ex;
            return ResultBox<SerializedCommandExecuteResponse>.FromException(source);
        }

        if (_commitRequestBuilder is not null)
        {
            try
            {
                var commitRequest = await _commitRequestBuilder.BuildCommitRequestAsync(
                    request.CommandName,
                    command,
                    cancellationToken);
                return ResultBox<SerializedCommandExecuteResponse>.FromValue(
                    MapCommitRequest(commitRequest));
            }
            catch (Exception ex)
            {
                return ResultBox<SerializedCommandExecuteResponse>.FromException(ex);
            }
        }

        ExecutionResult executionResult;
        try
        {
            var closedMethod = ExecuteAsyncOpenMethod.MakeGenericMethod(commandType);
            var task = (Task<ExecutionResult>)closedMethod.Invoke(_executor, [command, cancellationToken])!;
            executionResult = await task;
        }
        catch (Exception ex)
        {
            var source = ex is TargetInvocationException tie && tie.InnerException is not null
                ? tie.InnerException
                : ex;
            return ResultBox<SerializedCommandExecuteResponse>.FromException(source);
        }

        var eventCandidates = executionResult.Events
            .Select(evt => new SerializedCommandEventCandidate(
                EventPayloadName: evt.EventType,
                PayloadBase64: Convert.ToBase64String(
                    Encoding.UTF8.GetBytes(
                        SerializeWithFallback(evt.Payload))),
                Tags: evt.Tags))
            .ToList();

        var sortableUniqueId = executionResult.SortableUniqueId ?? "";
        var consistencyTags = executionResult.TagWrites
            .Select(tw => new ConsistencyTagEntry(
                Tag: tw.Tag,
                LastSortableUniqueId: sortableUniqueId))
            .ToList();

        var commandResultJson = executionResult.Metadata is not null
            ? SerializeWithFallback(executionResult.Metadata)
            : null;

        var response = new SerializedCommandExecuteResponse(
            EventCandidates: eventCandidates,
            ConsistencyTags: consistencyTags,
            CommandResultJson: commandResultJson,
            FirstEventId: executionResult.EventId,
            LastSortableUniqueId: executionResult.SortableUniqueId);

        return ResultBox<SerializedCommandExecuteResponse>.FromValue(response);
    }

    private static SerializedCommandExecuteResponse MapCommitRequest(SerializedCommitRequest commitRequest)
    {
        var eventCandidates = commitRequest.EventCandidates
            .Select(candidate => new SerializedCommandEventCandidate(
                EventPayloadName: candidate.EventPayloadName,
                PayloadBase64: Convert.ToBase64String(candidate.Payload),
                Tags: candidate.Tags))
            .ToList();

        return new SerializedCommandExecuteResponse(
            EventCandidates: eventCandidates,
            ConsistencyTags: commitRequest.ConsistencyTags.ToList(),
            CommandResultJson: null);
    }

    private string SerializeWithFallback(object value)
    {
        try
        {
            return JsonSerializer.Serialize(value, value.GetType(), _jsonOptions);
        }
        catch (NotSupportedException)
        {
            return JsonSerializer.Serialize(value, value.GetType(), FallbackJsonOptions);
        }
    }

    public static void Map(WebApplication app)
    {
        app.MapPost("/api/sekiban/serialized/command/execute",
            async (HttpContext http, SerializedCommandExecuteRequest request, CancellationToken ct) =>
        {
            var endpoints = http.RequestServices.GetRequiredService<SerializedCommandEndpoints>();
            var result = await endpoints.ExecuteAsync(request, ct);
            if (!result.IsSuccess)
            {
                return Results.BadRequest(new { error = result.GetException().ToString() });
            }
            return Results.Ok(result.GetValue());
        });
    }
}
