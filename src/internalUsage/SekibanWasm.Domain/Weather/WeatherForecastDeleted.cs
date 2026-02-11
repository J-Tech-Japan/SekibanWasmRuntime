using Sekiban.Dcb.Events;

namespace SekibanWasm.Domain.Weather;

public record WeatherForecastDeleted(
    string ForecastId,
    DateTimeOffset DeletedAt) : IEventPayload;
