using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Validation;

namespace Sekiban.Dcb.WasmRuntime.Remote;

public sealed class RemoteSekibanExecutor(
    HttpClient httpClient,
    DcbDomainTypes domainTypes,
    IEventPublisher eventPublisher,
    ILogger<RemoteSekibanExecutor> logger,
    ILoggerFactory loggerFactory) : ISekibanExecutor
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly DcbDomainTypes _domainTypes = domainTypes;
    private readonly IEventPublisher _eventPublisher = eventPublisher;
    private readonly ILogger<RemoteSekibanExecutor> _logger = logger;
    private readonly ILoggerFactory _loggerFactory = loggerFactory;

    public async Task<TagState> GetTagStateAsync(TagStateId tagStateId)
    {
        _logger.LogInformation("Using WASM host tag-state path for {TagStateId}", tagStateId.GetTagStateId());

        var response = await _httpClient.PostAsJsonAsync(
            "/api/sekiban/serialized/tag-state",
            new TagStateRequest(tagStateId.GetTagStateId()),
            TransportJsonOptions);
        await EnsureSuccessAsync(response, "serialized/tag-state");

        SerializableTagState serializableState =
            await response.Content.ReadFromJsonAsync<SerializableTagState>(TransportJsonOptions)
            ?? throw new InvalidOperationException("Null response from WASM host tag-state endpoint.");

        _logger.LogInformation(
            "WASM host tag-state response {TagStateId}: payloadName={TagPayloadName}, payloadBytes={PayloadBytes}, version={Version}, lastSortable={LastSortableUniqueId}",
            tagStateId.GetTagStateId(),
            serializableState.TagPayloadName,
            serializableState.Payload.Length,
            serializableState.Version,
            serializableState.LastSortedUniqueId);

        var payloadResult = _domainTypes.TagStatePayloadTypes.DeserializePayload(
            serializableState.TagPayloadName,
            serializableState.Payload);
        if (!payloadResult.IsSuccess)
        {
            throw payloadResult.GetException();
        }

        ITagStatePayload payload = payloadResult.GetValue();
        Type payloadType = payload.GetType();

        _logger.LogInformation(
            "WASM host tag-state deserialized {TagStateId}: clrType={ClrType}, assembly={Assembly}",
            tagStateId.GetTagStateId(),
            payloadType.FullName,
            payloadType.Assembly.FullName);

        return new TagState(
            payload,
            serializableState.Version,
            serializableState.LastSortedUniqueId,
            serializableState.TagGroup,
            serializableState.TagContent,
            serializableState.TagProjector,
            serializableState.ProjectorVersion);
    }

    public async Task<TResult> QueryAsync<TResult>(IQueryCommon<TResult> queryCommon)
        where TResult : notnull
    {
        _logger.LogInformation("Using WASM host query path for {QueryType}", queryCommon.GetType().Name);

        SerializedQueryRequest request = BuildSerializedQueryRequest(queryCommon);
        var response = await _httpClient.PostAsJsonAsync(
            "/api/sekiban/serialized/query",
            request,
            TransportJsonOptions);
        await EnsureSuccessAsync(response, "serialized/query");

        SerializedQueryResponse body =
            await response.Content.ReadFromJsonAsync<SerializedQueryResponse>(TransportJsonOptions)
            ?? throw new InvalidOperationException("Null response from WASM host query endpoint.");

        return JsonSerializer.Deserialize<TResult>(body.ResultJson, _domainTypes.JsonSerializerOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize query result for {request.QueryType}.");
    }

    public async Task<ListQueryResult<TResult>> QueryAsync<TResult>(IListQueryCommon<TResult> queryCommon)
        where TResult : notnull
    {
        _logger.LogInformation("Using WASM host list-query path for {QueryType}", queryCommon.GetType().Name);

        SerializedQueryRequest request = BuildSerializedQueryRequest(queryCommon);
        var response = await _httpClient.PostAsJsonAsync(
            "/api/sekiban/serialized/list-query",
            request,
            TransportJsonOptions);
        await EnsureSuccessAsync(response, "serialized/list-query");

        SerializedListQueryResponse body =
            await response.Content.ReadFromJsonAsync<SerializedListQueryResponse>(TransportJsonOptions)
            ?? throw new InvalidOperationException("Null response from WASM host list-query endpoint.");

        IReadOnlyList<TResult> items =
            JsonSerializer.Deserialize<List<TResult>>(body.ItemsJson, _domainTypes.JsonSerializerOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize list-query result for {request.QueryType}.");

        _logger.LogInformation(
            "WASM host list-query response {QueryType}: total={TotalCount}, page={CurrentPage}, size={PageSize}, itemCount={ItemCount}",
            request.QueryType,
            body.TotalCount,
            body.CurrentPage,
            body.PageSize,
            items.Count);

        return new ListQueryResult<TResult>(
            body.TotalCount,
            body.TotalPages,
            body.CurrentPage,
            body.PageSize,
            items);
    }

    public Task<ExecutionResult> ExecuteAsync<TCommand>(
        TCommand command,
        Func<TCommand, ICommandContext, Task<EventOrNone>> handlerFunc,
        CancellationToken cancellationToken = default)
        where TCommand : ICommand =>
        ExecuteCommandCoreAsync(
            command,
            command.GetType().Name,
            context => handlerFunc(command, context),
            cancellationToken);

    public Task<ExecutionResult> ExecuteCommandAsync(
        Func<ICommandContext, Task<EventOrNone>> handlerFunc,
        CancellationToken cancellationToken = default) =>
        ExecuteCommandCoreAsync(
            AnonymousCommand.Instance,
            nameof(AnonymousCommand),
            handlerFunc,
            cancellationToken);

    public Task<ExecutionResult> ExecuteAsync<TCommand>(
        TCommand command,
        CancellationToken cancellationToken = default)
        where TCommand : ICommandWithHandler<TCommand> =>
        ExecuteCommandCoreAsync(
            command,
            command.GetType().Name,
            context => TCommand.HandleAsync(command, context),
            cancellationToken);

    private async Task<ExecutionResult> ExecuteCommandCoreAsync(
        ICommand command,
        string commandName,
        Func<ICommandContext, Task<EventOrNone>> handlerFunc,
        CancellationToken cancellationToken)
    {
        var validationErrors = SekibanValidator.Validate(command);
        if (validationErrors.Count > 0)
        {
            throw new SekibanValidationException(validationErrors);
        }

        var context = new RemoteCommandContext(
            _httpClient,
            _domainTypes,
            _loggerFactory.CreateLogger<RemoteCommandContext>());
        EventOrNone eventOrNone = await handlerFunc(context);
        IReadOnlyList<EventPayloadWithTags> collectedEvents = CollectEvents(context, eventOrNone);
        if (collectedEvents.Count == 0)
        {
            return new ExecutionResult(Guid.Empty, 0, [], TimeSpan.Zero, []);
        }

        List<ITag> allTags = collectedEvents
            .SelectMany(static e => e.Tags)
            .GroupBy(static tag => tag.GetTag(), StringComparer.Ordinal)
            .Select(static group => group.First())
            .ToList();
        TagValidator.ValidateTagsAndThrow(allTags);

        SerializedCommitRequest commitRequest = BuildCommitRequest(collectedEvents, context.AccessedTagStates);
        var response = await _httpClient.PostAsJsonAsync(
            "/api/sekiban/serialized/commit",
            commitRequest,
            TransportJsonOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, "serialized/commit");

        SerializedCommitResult commitResult =
            await response.Content.ReadFromJsonAsync<SerializedCommitResult>(TransportJsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Null response from WASM host commit endpoint.");

        await SerializedCommitResultRepublisher.PublishWrittenEventsAsync(
            commitResult,
            _domainTypes,
            _eventPublisher,
            cancellationToken);

        List<Event> writtenEvents = commitResult.WrittenEvents
            .Select(DeserializeEvent)
            .ToList();
        SerializableEvent? firstSerializableEvent = commitResult.WrittenEvents.FirstOrDefault();

        return new ExecutionResult(
            EventId: firstSerializableEvent?.Id ?? Guid.Empty,
            EventPosition: writtenEvents.Count,
            TagWrites: commitResult.TagWriteResults,
            Duration: commitResult.Duration,
            Events: writtenEvents,
            Metadata: new Dictionary<string, object>
            {
                ["EventCount"] = writtenEvents.Count,
                ["TagCount"] = allTags.Count,
                ["CommandName"] = commandName
            },
            SortableUniqueId: firstSerializableEvent?.SortableUniqueIdValue);
    }

    private static IReadOnlyList<EventPayloadWithTags> CollectEvents(
        RemoteCommandContext context,
        EventOrNone eventOrNone)
    {
        var collectedEvents = new List<EventPayloadWithTags>(context.AppendedEvents);
        if (!eventOrNone.HasEvent)
        {
            return collectedEvents;
        }

        EventPayloadWithTags returnedEvent = eventOrNone.GetValue();
        if (!collectedEvents.Contains(returnedEvent))
        {
            collectedEvents.Add(returnedEvent);
        }

        return collectedEvents;
    }

    private SerializedCommitRequest BuildCommitRequest(
        IReadOnlyList<EventPayloadWithTags> collectedEvents,
        IReadOnlyDictionary<ITag, TagState> accessedTagStates)
    {
        List<SerializableEventCandidate> eventCandidates = collectedEvents
            .Select(e => new SerializableEventCandidate(
                Payload: JsonSerializer.SerializeToUtf8Bytes(e.Event, e.Event.GetType(), _domainTypes.JsonSerializerOptions),
                EventPayloadName: e.Event.GetType().Name,
                Tags: e.Tags.Select(static tag => tag.GetTag()).ToList()))
            .ToList();

        List<ConsistencyTagEntry> consistencyTags = collectedEvents
            .SelectMany(static e => e.Tags)
            .Where(static tag => tag.IsConsistencyTag())
            .GroupBy(static tag => tag.GetTag(), StringComparer.Ordinal)
            .Select(group => BuildConsistencyTagEntry(group.First(), accessedTagStates))
            .ToList();

        return new SerializedCommitRequest(eventCandidates, consistencyTags);
    }

    private static ConsistencyTagEntry BuildConsistencyTagEntry(
        ITag tag,
        IReadOnlyDictionary<ITag, TagState> accessedTagStates)
    {
        if (tag is ConsistencyTag consistencyTag && consistencyTag.SortableUniqueId.HasValue)
        {
            return new ConsistencyTagEntry(tag.GetTag(), consistencyTag.SortableUniqueId.GetValue().Value);
        }

        ITag lookupTag = tag is ConsistencyTag innerConsistencyTag
            ? innerConsistencyTag.InnerTag
            : tag;

        return accessedTagStates.TryGetValue(lookupTag, out TagState? tagState)
            ? new ConsistencyTagEntry(tag.GetTag(), tagState.LastSortedUniqueId)
            : new ConsistencyTagEntry(tag.GetTag(), string.Empty);
    }

    private Event DeserializeEvent(SerializableEvent serializableEvent)
    {
        var eventResult = serializableEvent.ToEvent(_domainTypes.EventTypes);
        if (!eventResult.IsSuccess)
        {
            throw eventResult.GetException();
        }

        return eventResult.GetValue();
    }

    private SerializedQueryRequest BuildSerializedQueryRequest(object query) =>
        new(
            QueryType: query.GetType().Name,
            QueryParamsJson: JsonSerializer.Serialize(query, query.GetType(), _domainTypes.JsonSerializerOptions),
            WaitForSortableUniqueId: ExtractWaitForSortableUniqueId(query));

    private static string? ExtractWaitForSortableUniqueId(object query) =>
        query.GetType().GetProperty("WaitForSortableUniqueId")?.GetValue(query) as string;

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string endpointName)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string errorBody = await response.Content.ReadAsStringAsync();
        throw new HttpRequestException(
            $"WASM host {endpointName} failed with {(int)response.StatusCode} {response.StatusCode}: {errorBody}");
    }

    private sealed record TagStateRequest(string TagStateId);

    private sealed record AnonymousCommand : ICommand
    {
        public static AnonymousCommand Instance { get; } = new();
    }

    private static readonly JsonSerializerOptions TransportJsonOptions = new(JsonSerializerDefaults.Web);
}
