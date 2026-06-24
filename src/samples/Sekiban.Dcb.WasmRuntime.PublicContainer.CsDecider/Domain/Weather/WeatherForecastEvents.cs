using Sekiban.Dcb.Events;

namespace PublicContainerCsDecider.Domain.Weather;

// Events — the only thing persisted to the external event store.
public record WeatherForecastCreated(
    string ForecastId,
    string Location,
    int TemperatureC,
    string Summary,
    DateTimeOffset CreatedAt) : IEventPayload;

public record WeatherForecastLocationUpdated(
    string ForecastId,
    string NewLocation,
    DateTimeOffset UpdatedAt) : IEventPayload;

public record WeatherForecastDeleted(
    string ForecastId,
    DateTimeOffset DeletedAt) : IEventPayload;
