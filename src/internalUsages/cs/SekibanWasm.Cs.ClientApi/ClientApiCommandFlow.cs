using System.Text.Json;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.WasmRuntime;
using SekibanWasm.Cs.Domain.Weather;

namespace SekibanWasm.Cs.ClientApi;

public sealed class ClientApiCommandFlow
{
    private readonly ISerializedDcbClient _client;
    private readonly ITagVersionReader _tagVersionReader;
    private readonly IWeatherQueryClient _queryClient;
    private readonly IWeatherForecastConsistencyTracker _consistencyTracker;
    private readonly JsonSerializerOptions _jsonOptions;

    public ClientApiCommandFlow(
        ISerializedDcbClient client,
        ITagVersionReader tagVersionReader,
        IWeatherQueryClient queryClient,
        IWeatherForecastConsistencyTracker consistencyTracker,
        DomainSerializerOptions jsonOptions)
    {
        _client = client;
        _tagVersionReader = tagVersionReader;
        _queryClient = queryClient;
        _consistencyTracker = consistencyTracker;
        _jsonOptions = jsonOptions.Value;
    }

    public async Task<SerializedCommitResult> ExecuteAsync(
        string commandName,
        object command,
        CancellationToken ct)
    {
        var commitRequest = await BuildCommitRequestAsync(commandName, command, ct);
        var commitResult = await _client.CommitSerializableEventsAsync(commitRequest, ct);
        if (!commitResult.IsSuccess)
        {
            throw commitResult.GetException();
        }

        var value = commitResult.GetValue();
        TrackCommittedSortableUniqueId(command, value);
        return value;
    }

    public async Task<SerializedCommitRequest> BuildCommitRequestAsync(
        string commandName,
        object command,
        CancellationToken ct)
    {
        return command switch
        {
            CreateWeatherForecast create => await BuildCreateRequestAsync(create, ct),
            UpdateWeatherForecastLocation update => await BuildUpdateRequestAsync(update, ct),
            DeleteWeatherForecast delete => await BuildDeleteRequestAsync(delete, ct),
            _ => throw new InvalidOperationException($"Unsupported command: {commandName}")
        };
    }

    private async Task<SerializedCommitRequest> BuildCreateRequestAsync(
        CreateWeatherForecast command,
        CancellationToken ct)
    {
        TagVersion tagVersion = await _tagVersionReader.ReadAsync(
            new WeatherForecastTag(command.ForecastId),
            ct);
        if (tagVersion.Exists)
        {
            throw new InvalidOperationException($"Weather forecast {command.ForecastId} already exists");
        }

        var evt = new WeatherForecastCreated(
            command.ForecastId,
            command.Location,
            command.TemperatureC,
            command.Summary,
            DateTimeOffset.UtcNow);

        return BuildCommitRequest(
            command.ForecastId,
            evt,
            nameof(WeatherForecastCreated),
            expectedTagVersion: string.Empty);
    }

    private async Task<SerializedCommitRequest> BuildUpdateRequestAsync(
        UpdateWeatherForecastLocation command,
        CancellationToken ct)
    {
        var current = await _queryClient.GetForecastAsync(
            command.ForecastId,
            _consistencyTracker.GetWaitForSortableUniqueId(command.ForecastId),
            ct);
        if (current is null)
        {
            throw new InvalidOperationException($"Weather forecast {command.ForecastId} does not exist");
        }

        if (current.IsDeleted)
        {
            throw new InvalidOperationException($"Weather forecast {command.ForecastId} has been deleted");
        }

        if (current.Location == command.NewLocation)
        {
            return new SerializedCommitRequest([], []);
        }

        var evt = new WeatherForecastLocationUpdated(
            command.ForecastId,
            command.NewLocation,
            DateTimeOffset.UtcNow);

        TagVersion tagVersion = await GetExistingTagVersionAsync(command.ForecastId, ct);
        return BuildCommitRequest(
            command.ForecastId,
            evt,
            nameof(WeatherForecastLocationUpdated),
            tagVersion.LastSortableUniqueId);
    }

    private async Task<SerializedCommitRequest> BuildDeleteRequestAsync(
        DeleteWeatherForecast command,
        CancellationToken ct)
    {
        var current = await _queryClient.GetForecastAsync(
            command.ForecastId,
            _consistencyTracker.GetWaitForSortableUniqueId(command.ForecastId),
            ct);
        if (current is null)
        {
            throw new InvalidOperationException($"Weather forecast {command.ForecastId} does not exist");
        }

        if (current.IsDeleted)
        {
            throw new InvalidOperationException($"Weather forecast {command.ForecastId} has already been deleted");
        }

        var evt = new WeatherForecastDeleted(command.ForecastId, DateTimeOffset.UtcNow);
        TagVersion tagVersion = await GetExistingTagVersionAsync(command.ForecastId, ct);
        return BuildCommitRequest(
            command.ForecastId,
            evt,
            nameof(WeatherForecastDeleted),
            tagVersion.LastSortableUniqueId);
    }

    private async Task<TagVersion> GetExistingTagVersionAsync(string forecastId, CancellationToken ct)
    {
        // Sekiban.Dcb 10.8.0 treats an empty expected version as "expect this tag to be empty".
        // Existing-tag writes must therefore carry the exact current tag version.
        TagVersion tagVersion = await _tagVersionReader.ReadAsync(new WeatherForecastTag(forecastId), ct);
        if (!tagVersion.Exists || string.IsNullOrEmpty(tagVersion.LastSortableUniqueId))
        {
            throw new InvalidOperationException($"Weather forecast {forecastId} does not exist");
        }

        return tagVersion;
    }

    private SerializedCommitRequest BuildCommitRequest(
        string forecastId,
        object evt,
        string eventPayloadName,
        string expectedTagVersion)
    {
        var tag = new WeatherForecastTag(forecastId).GetTag();
        var eventCandidate = new SerializableEventCandidate(
            Payload: JsonSerializer.SerializeToUtf8Bytes(evt, evt.GetType(), _jsonOptions),
            EventPayloadName: eventPayloadName,
            Tags: [tag]);

        return new SerializedCommitRequest(
            EventCandidates: [eventCandidate],
            ConsistencyTags: [new ConsistencyTagEntry(tag, expectedTagVersion)]);
    }

    private void TrackCommittedSortableUniqueId(object command, SerializedCommitResult result)
    {
        string? sortableUniqueId = result.WrittenEvents.LastOrDefault()?.SortableUniqueIdValue;
        if (string.IsNullOrWhiteSpace(sortableUniqueId))
        {
            return;
        }

        switch (command)
        {
            case CreateWeatherForecast create:
                _consistencyTracker.Record(create.ForecastId, sortableUniqueId);
                break;
            case UpdateWeatherForecastLocation update:
                _consistencyTracker.Record(update.ForecastId, sortableUniqueId);
                break;
            case DeleteWeatherForecast delete:
                _consistencyTracker.Record(delete.ForecastId, sortableUniqueId);
                break;
        }
    }
}
