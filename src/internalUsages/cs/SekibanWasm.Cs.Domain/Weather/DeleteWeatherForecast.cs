using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;

namespace SekibanWasm.Cs.Domain.Weather;

public record DeleteWeatherForecast(
    string ForecastId) : ICommandWithHandler<DeleteWeatherForecast>
{
    public static async Task<EventOrNone> HandleAsync(
        DeleteWeatherForecast command,
        ICommandContext context)
    {
        var tag = new WeatherForecastTag(command.ForecastId);
        var state = await context.GetStateAsync<WeatherForecastProjector>(tag);

        if (state.Payload is EmptyTagStatePayload)
        {
            throw new InvalidOperationException($"Weather forecast {command.ForecastId} does not exist");
        }

        if (state.Payload is WeatherForecastState payload && payload.IsDeleted)
        {
            throw new InvalidOperationException($"Weather forecast {command.ForecastId} has already been deleted");
        }

        var evt = new WeatherForecastDeleted(command.ForecastId, DateTimeOffset.UtcNow);
        return EventOrNone.From(evt, tag);
    }
}
