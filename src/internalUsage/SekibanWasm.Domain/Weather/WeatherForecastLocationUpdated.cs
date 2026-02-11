using Sekiban.Dcb.Events;

namespace SekibanWasm.Domain.Weather;

public record WeatherForecastLocationUpdated(
    string ForecastId,
    string NewLocation,
    DateTimeOffset UpdatedAt) : IEventPayload;
