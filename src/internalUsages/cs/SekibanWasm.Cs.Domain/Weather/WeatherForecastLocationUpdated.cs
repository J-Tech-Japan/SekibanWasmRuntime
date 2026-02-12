using Sekiban.Dcb.Events;

namespace SekibanWasm.Cs.Domain.Weather;

public record WeatherForecastLocationUpdated(
    string ForecastId,
    string NewLocation,
    DateTimeOffset UpdatedAt) : IEventPayload;
