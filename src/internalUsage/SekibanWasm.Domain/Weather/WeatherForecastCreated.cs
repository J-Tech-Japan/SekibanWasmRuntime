using Sekiban.Dcb.Events;

namespace SekibanWasm.Domain.Weather;

public record WeatherForecastCreated(
    string ForecastId,
    string Location,
    int TemperatureC,
    string Summary,
    DateTimeOffset CreatedAt) : IEventPayload;
