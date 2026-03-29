using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.WasmRuntime.Remote;

public sealed class RemoteCommandContext(
    HttpClient httpClient,
    DcbDomainTypes domainTypes,
    ILogger<RemoteCommandContext> logger) : ICommandContext
{
    private static readonly JsonSerializerOptions TransportJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient = httpClient;
    private readonly DcbDomainTypes _domainTypes = domainTypes;
    private readonly ILogger<RemoteCommandContext> _logger = logger;
    private readonly Dictionary<ITag, TagState> _accessedTagStates = [];
    private readonly List<EventPayloadWithTags> _appendedEvents = [];

    public IReadOnlyDictionary<ITag, TagState> AccessedTagStates => _accessedTagStates;
    public IReadOnlyList<EventPayloadWithTags> AppendedEvents => _appendedEvents;

    public async Task<TagStateTyped<TState>> GetStateAsync<TState, TProjector>(ITag tag)
        where TState : ITagStatePayload
        where TProjector : ITagProjector<TProjector>
    {
        TagState tagState = await GetTagStateAsync(tag, new TagStateId(tag, TProjector.ProjectorName));
        if (tagState.Payload is not TState typedPayload)
        {
            throw new InvalidCastException(
                $"Expected state payload of type {typeof(TState).Name} but got {tagState.Payload.GetType().Name}");
        }

        return new TagStateTyped<TState>(tag, typedPayload, tagState.Version, DateTimeOffset.UtcNow);
    }

    public Task<TagState> GetStateAsync<TProjector>(ITag tag)
        where TProjector : ITagProjector<TProjector> =>
        GetTagStateAsync(tag, new TagStateId(tag, TProjector.ProjectorName));

    public async Task<bool> TagExistsAsync(ITag tag)
    {
        TagLatestSortableResponse response = await GetLatestSortableAsync(tag);
        return response.Exists;
    }

    public async Task<string> GetTagLatestSortableUniqueIdAsync(ITag tag)
    {
        TagLatestSortableResponse response = await GetLatestSortableAsync(tag);
        return response.LastSortableUniqueId;
    }

    public Task<EventOrNone> AppendEvent(IEventPayload ev, params ITag[] tags) =>
        AppendEvent(new EventPayloadWithTags(ev, tags?.ToList() ?? []));

    public Task<EventOrNone> AppendEvent(EventPayloadWithTags eventPayloadWithTags)
    {
        _appendedEvents.Add(eventPayloadWithTags);
        return Task.FromResult(EventOrNone.FromValue(eventPayloadWithTags));
    }

    private async Task<TagState> GetTagStateAsync(ITag tag, TagStateId tagStateId)
    {
        var response = await _httpClient.PostAsJsonAsync(
            "/api/sekiban/serialized/tag-state",
            new TagStateRequest(tagStateId.GetTagStateId()),
            TransportJsonOptions);
        await EnsureSuccessAsync(response, "serialized/tag-state");

        SerializableTagState serializableState =
            await response.Content.ReadFromJsonAsync<SerializableTagState>(TransportJsonOptions)
            ?? throw new InvalidOperationException("Null response from WASM host tag-state endpoint.");

        _logger.LogInformation(
            "WASM host command-context tag-state response {TagStateId}: payloadName={TagPayloadName}, payloadBytes={PayloadBytes}, version={Version}, lastSortable={LastSortableUniqueId}",
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
            "WASM host command-context tag-state deserialized {TagStateId}: clrType={ClrType}, assembly={Assembly}",
            tagStateId.GetTagStateId(),
            payloadType.FullName,
            payloadType.Assembly.FullName);

        var tagState = new TagState(
            payload,
            serializableState.Version,
            serializableState.LastSortedUniqueId,
            serializableState.TagGroup,
            serializableState.TagContent,
            serializableState.TagProjector,
            serializableState.ProjectorVersion);
        _accessedTagStates[tag] = tagState;
        return tagState;
    }

    private async Task<TagLatestSortableResponse> GetLatestSortableAsync(ITag tag)
    {
        var response = await _httpClient.PostAsJsonAsync(
            "/api/sekiban/serialized/tag-latest-sortable",
            new TagLatestSortableRequest(tag.GetTag()),
            TransportJsonOptions);
        await EnsureSuccessAsync(response, "serialized/tag-latest-sortable");

        return await response.Content.ReadFromJsonAsync<TagLatestSortableResponse>(TransportJsonOptions)
            ?? throw new InvalidOperationException("Null response from WASM host tag-latest-sortable endpoint.");
    }

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
    private sealed record TagLatestSortableRequest(string Tag);
    private sealed record TagLatestSortableResponse(bool Exists, string LastSortableUniqueId);
}
