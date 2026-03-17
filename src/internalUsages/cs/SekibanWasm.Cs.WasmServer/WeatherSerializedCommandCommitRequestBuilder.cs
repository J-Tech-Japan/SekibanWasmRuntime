using System.Text.Json;
using Sekiban.Dcb;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.WasmRuntime;
using SekibanWasm.Cs.Domain.Weather;

public sealed class WeatherSerializedCommandCommitRequestBuilder : ISekibanCommandCommitRequestBuilder
{
    private readonly ISekibanExecutor _executor;
    private readonly JsonSerializerOptions _jsonOptions;

    public WeatherSerializedCommandCommitRequestBuilder(
        ISekibanExecutor executor,
        JsonSerializerOptions jsonOptions)
    {
        _executor = executor;
        _jsonOptions = jsonOptions;
    }

    public async Task<SerializedCommitRequest> BuildCommitRequestAsync(
        string commandName,
        object command,
        CancellationToken cancellationToken = default) =>
        command switch
        {
            CreateWeatherForecast create => await BuildCreateRequestAsync(create),
            UpdateWeatherForecastLocation update => await BuildUpdateRequestAsync(update),
            DeleteWeatherForecast delete => await BuildDeleteRequestAsync(delete),
            _ => throw new InvalidOperationException($"Unsupported command: {commandName}")
        };

    private async Task<SerializedCommitRequest> BuildCreateRequestAsync(CreateWeatherForecast command)
    {
        var existing = await GetForecastAsync(command.ForecastId);
        if (existing is not null)
        {
            throw new InvalidOperationException($"Weather forecast {command.ForecastId} already exists");
        }

        var evt = new WeatherForecastCreated(
            command.ForecastId,
            command.Location,
            command.TemperatureC,
            command.Summary,
            DateTimeOffset.UtcNow);

        return BuildCommitRequest(command.ForecastId, evt, nameof(WeatherForecastCreated));
    }

    private async Task<SerializedCommitRequest> BuildUpdateRequestAsync(UpdateWeatherForecastLocation command)
    {
        var current = await GetForecastAsync(command.ForecastId);
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

        return BuildCommitRequest(command.ForecastId, evt, nameof(WeatherForecastLocationUpdated));
    }

    private async Task<SerializedCommitRequest> BuildDeleteRequestAsync(DeleteWeatherForecast command)
    {
        var current = await GetForecastAsync(command.ForecastId);
        if (current is null)
        {
            throw new InvalidOperationException($"Weather forecast {command.ForecastId} does not exist");
        }

        if (current.IsDeleted)
        {
            throw new InvalidOperationException($"Weather forecast {command.ForecastId} has already been deleted");
        }

        var evt = new WeatherForecastDeleted(command.ForecastId, DateTimeOffset.UtcNow);
        return BuildCommitRequest(command.ForecastId, evt, nameof(WeatherForecastDeleted));
    }

    private async Task<WeatherForecastItem?> GetForecastAsync(string forecastId)
    {
        var result = await _executor.QueryAsync(new GetWeatherForecastListQuery
        {
            ForecastId = forecastId,
            IncludeDeleted = true,
            PageSize = 1
        });

        return result.Items.SingleOrDefault();
    }

    private SerializedCommitRequest BuildCommitRequest(
        string forecastId,
        object evt,
        string eventPayloadName)
    {
        var tag = new WeatherForecastTag(forecastId).GetTag();
        var eventCandidate = new SerializableEventCandidate(
            Payload: JsonSerializer.SerializeToUtf8Bytes(evt, evt.GetType(), _jsonOptions),
            EventPayloadName: eventPayloadName,
            Tags: [tag]);

        return new SerializedCommitRequest(
            EventCandidates: [eventCandidate],
            ConsistencyTags: [new ConsistencyTagEntry(tag, string.Empty)]);
    }
}
