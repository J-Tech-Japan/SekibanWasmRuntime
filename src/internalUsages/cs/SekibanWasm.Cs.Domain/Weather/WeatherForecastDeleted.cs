using Sekiban.Dcb.Events;

namespace SekibanWasm.Cs.Domain.Weather;

public record WeatherForecastDeleted(
    string ForecastId,
    DateTimeOffset DeletedAt) : IEventPayload;
