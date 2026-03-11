using Sekiban.Dcb.Events;

namespace SekibanWasm.Rust.Domain.Weather;

public record WeatherForecastLocationUpdated(
    string ForecastId,
    string NewLocation,
    DateTimeOffset UpdatedAt) : IEventPayload;
