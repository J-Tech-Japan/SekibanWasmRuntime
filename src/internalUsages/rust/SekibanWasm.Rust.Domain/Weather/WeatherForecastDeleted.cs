using Sekiban.Dcb.Events;

namespace SekibanWasm.Rust.Domain.Weather;

public record WeatherForecastDeleted(
    string ForecastId,
    DateTimeOffset DeletedAt) : IEventPayload;
