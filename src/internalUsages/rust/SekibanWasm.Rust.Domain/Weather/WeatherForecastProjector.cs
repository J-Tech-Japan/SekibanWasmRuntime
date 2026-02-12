using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;

namespace SekibanWasm.Rust.Domain.Weather;

public record WeatherForecastProjector : ITagProjector<WeatherForecastProjector>
{
    public static string ProjectorVersion => "v1";
    public static string ProjectorName => "WeatherForecastProjector";

    public static ITagStatePayload Project(ITagStatePayload current, Event ev)
    {
        if (current is EmptyTagStatePayload)
        {
            return ev.Payload switch
            {
                WeatherForecastCreated created => new WeatherForecastState(
                    created.ForecastId,
                    created.Location,
                    created.TemperatureC,
                    created.Summary,
                    created.CreatedAt),
                _ => current
            };
        }

        var state = current as WeatherForecastState ?? WeatherForecastState.Empty;

        return ev.Payload switch
        {
            WeatherForecastCreated created => new WeatherForecastState(
                created.ForecastId,
                created.Location,
                created.TemperatureC,
                created.Summary,
                created.CreatedAt),

            WeatherForecastLocationUpdated updated => state with
            {
                Location = updated.NewLocation
            },

            WeatherForecastDeleted deleted => state with
            {
                IsDeleted = true,
                DeletedAt = deleted.DeletedAt
            },

            _ => state
        };
    }
}
