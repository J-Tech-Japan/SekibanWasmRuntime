using Sekiban.Dcb.Events;

namespace SekibanWasm.Rust.Domain.Weather;

public record WeatherForecastCreated(
    string ForecastId,
    string Location,
    int TemperatureC,
    string Summary,
    DateTimeOffset CreatedAt) : IEventPayload;
