using System.Text.Json;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.WasmRuntime;
using SekibanWasm.Cs.Domain.Weather;

namespace SekibanWasm.Cs.ClientApi;

public class ClientApiCommandFlow
{
    private readonly ISerializedDcbClient _client;
    private readonly IWeatherQueryClient _queryClient;
    private readonly JsonSerializerOptions _jsonOptions;

    public ClientApiCommandFlow(
        ISerializedDcbClient client,
        IWeatherQueryClient queryClient,
        JsonSerializerOptions jsonOptions)
    {
        _client = client;
        _queryClient = queryClient;
        _jsonOptions = jsonOptions;
    }

    public async Task<IResult> ExecuteAndCommit(
        string commandName,
        object command,
        CancellationToken ct)
    {
        try
        {
            var commitRequest = await BuildCommitRequestAsync(commandName, command, ct);
            var commitResult = await _client.CommitSerializableEventsAsync(commitRequest, ct);
            if (!commitResult.IsSuccess)
            {
                return Results.BadRequest(new { error = commitResult.GetException().Message });
            }

            return Results.Ok(commitResult.GetValue());
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private async Task<SerializedCommitRequest> BuildCommitRequestAsync(
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
        var existing = await _queryClient.GetForecastAsync(command.ForecastId, ct);
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

        return BuildCommitRequest(
            command.ForecastId,
            evt,
            nameof(WeatherForecastCreated));
    }

    private async Task<SerializedCommitRequest> BuildUpdateRequestAsync(
        UpdateWeatherForecastLocation command,
        CancellationToken ct)
    {
        var current = await _queryClient.GetForecastAsync(command.ForecastId, ct);
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
            throw new InvalidOperationException("New location must differ from the current location");
        }

        var evt = new WeatherForecastLocationUpdated(
            command.ForecastId,
            command.NewLocation,
            DateTimeOffset.UtcNow);

        return BuildCommitRequest(
            command.ForecastId,
            evt,
            nameof(WeatherForecastLocationUpdated));
    }

    private async Task<SerializedCommitRequest> BuildDeleteRequestAsync(
        DeleteWeatherForecast command,
        CancellationToken ct)
    {
        var current = await _queryClient.GetForecastAsync(command.ForecastId, ct);
        if (current is null)
        {
            throw new InvalidOperationException($"Weather forecast {command.ForecastId} does not exist");
        }

        if (current.IsDeleted)
        {
            throw new InvalidOperationException($"Weather forecast {command.ForecastId} has already been deleted");
        }

        var evt = new WeatherForecastDeleted(command.ForecastId, DateTimeOffset.UtcNow);

        return BuildCommitRequest(
            command.ForecastId,
            evt,
            nameof(WeatherForecastDeleted));
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
