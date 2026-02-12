using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;

namespace SekibanWasm.Cs.Domain.Weather;

public record UpdateWeatherForecastLocation(
    string ForecastId,
    string NewLocation) : ICommandWithHandler<UpdateWeatherForecastLocation>
{
    public static async Task<EventOrNone> HandleAsync(
        UpdateWeatherForecastLocation command,
        ICommandContext context)
    {
        var tag = new WeatherForecastTag(command.ForecastId);
        var state = await context.GetStateAsync<WeatherForecastProjector>(tag);

        if (state.Payload is EmptyTagStatePayload)
        {
            throw new InvalidOperationException($"Weather forecast {command.ForecastId} does not exist");
        }

        if (state.Payload is WeatherForecastState payload)
        {
            if (payload.IsDeleted)
            {
                throw new InvalidOperationException($"Weather forecast {command.ForecastId} has been deleted");
            }

            if (payload.Location == command.NewLocation)
            {
                return EventOrNone.Empty;
            }
        }

        var evt = new WeatherForecastLocationUpdated(
            command.ForecastId,
            command.NewLocation,
            DateTimeOffset.UtcNow);

        return EventOrNone.From(evt, tag);
    }
}
