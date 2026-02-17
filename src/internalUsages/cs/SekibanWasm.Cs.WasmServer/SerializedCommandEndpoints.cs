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

    public SerializedCommandEndpoints(
        ISekibanExecutor executor,
        SerializedCommandTypeRegistry registry,
        JsonSerializerOptions jsonOptions)
    {
        _executor = executor;
        _registry = registry;
        _jsonOptions = jsonOptions;
    }

    public async Task<ResultBox<SerializedCommandExecuteResponse>> ExecuteAsync(
        SerializedCommandExecuteRequest request,
        CancellationToken cancellationToken)
    {
        ExecutionResult executionResult;
        try
        {
            var commandType = _registry.GetCommandType(request.CommandName);
            var command = JsonSerializer.Deserialize(request.CommandJson, commandType, _jsonOptions);
            if (command is null)
            {
                return ResultBox<SerializedCommandExecuteResponse>.FromException(
                    new ArgumentException($"Failed to deserialize command: {request.CommandName}"));
            }

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
            CommandResultJson: commandResultJson);

        return ResultBox<SerializedCommandExecuteResponse>.FromValue(response);
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
