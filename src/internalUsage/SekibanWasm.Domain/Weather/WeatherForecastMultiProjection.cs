using Sekiban.Dcb;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Tags;

namespace SekibanWasm.Domain.Weather;

public record WeatherForecastMultiProjection : IMultiProjector<WeatherForecastMultiProjection>
{
    public Dictionary<string, WeatherForecastItem> Forecasts { get; init; } = new();

    public static string MultiProjectorName => "WeatherForecastMultiProjection";
    public static string MultiProjectorVersion => "1.0.0";

    public static WeatherForecastMultiProjection GenerateInitialPayload() => new();

    public static WeatherForecastMultiProjection Project(
        WeatherForecastMultiProjection payload,
        Event ev,
        List<ITag> tags,
        DcbDomainTypes domainTypes,
        SortableUniqueId safeWindowThreshold)
    {
        var forecastId = ev.Payload switch
        {
            WeatherForecastCreated created => created.ForecastId,
            WeatherForecastLocationUpdated updated => updated.ForecastId,
            WeatherForecastDeleted deleted => deleted.ForecastId,
            _ => null
        };

        if (string.IsNullOrEmpty(forecastId))
        {
            return payload;
        }

        var updatedForecasts = new Dictionary<string, WeatherForecastItem>(payload.Forecasts);

        WeatherForecastItem? result = ev.Payload switch
        {
            WeatherForecastCreated created => new WeatherForecastItem(
                created.ForecastId,
                created.Location,
                created.TemperatureC,
                created.Summary,
                created.CreatedAt),
            WeatherForecastLocationUpdated updated when updatedForecasts.TryGetValue(forecastId, out var existing) =>
                existing with { Location = updated.NewLocation },
            WeatherForecastDeleted deleted when updatedForecasts.TryGetValue(forecastId, out var existing) =>
                existing with { IsDeleted = true, DeletedAt = deleted.DeletedAt },
            _ => updatedForecasts.TryGetValue(forecastId, out var existing) ? existing : null
        };

        if (result != null)
        {
            updatedForecasts[forecastId] = result;
        }

        return payload with { Forecasts = updatedForecasts };
    }
}
