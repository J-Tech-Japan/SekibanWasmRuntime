using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;

namespace SekibanDcbDecider.Domain.Weather;

// Command + handler (Decider write side). The generic runtime container persists
// serialized events; this command documents the intended decision logic and is
// the in-process command path for hosts that wire ICommandContext.
public record CreateWeatherForecast(
    string ForecastId,
    string Location,
    int TemperatureC,
    string Summary) : ICommandWithHandler<CreateWeatherForecast>
{
    public static async Task<EventOrNone> HandleAsync(
        CreateWeatherForecast command,
        ICommandContext context)
    {
        var tag = new WeatherForecastTag(command.ForecastId);
        var state = await context.GetStateAsync<WeatherForecastProjector>(tag);

        if (state.Payload is not EmptyTagStatePayload)
        {
            throw new InvalidOperationException($"Weather forecast {command.ForecastId} already exists");
        }

        var evt = new WeatherForecastCreated(
            command.ForecastId,
            command.Location,
            command.TemperatureC,
            command.Summary,
            DateTimeOffset.UtcNow);

        return await context.AppendEvent(evt, tag);
    }
}
