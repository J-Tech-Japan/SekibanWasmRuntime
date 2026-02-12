using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
using SekibanWasm.Rust.Domain.Weather;
using Xunit;

namespace SekibanWasm.Rust.Tests;

public class WeatherForecastProjectorTests
{
    [Fact]
    public void Project_ShouldCreateStateFromCreatedEvent()
    {
        // Given
        var empty = new EmptyTagStatePayload();
        var created = new WeatherForecastCreated(
            "forecast-1", "Tokyo", 25, "Warm", DateTimeOffset.UtcNow);
        var ev = CreateEvent(created, nameof(WeatherForecastCreated));

        // When
        var result = WeatherForecastProjector.Project(empty, ev);

        // Then
        var state = Assert.IsType<WeatherForecastState>(result);
        Assert.Equal("forecast-1", state.ForecastId);
        Assert.Equal("Tokyo", state.Location);
        Assert.Equal(25, state.TemperatureC);
        Assert.Equal("Warm", state.Summary);
        Assert.False(state.IsDeleted);
    }

    [Fact]
    public void Project_ShouldUpdateLocationFromLocationUpdatedEvent()
    {
        // Given
        var state = new WeatherForecastState(
            "forecast-1", "Tokyo", 25, "Warm", DateTimeOffset.UtcNow);
        var updated = new WeatherForecastLocationUpdated(
            "forecast-1", "Osaka", DateTimeOffset.UtcNow);
        var ev = CreateEvent(updated, nameof(WeatherForecastLocationUpdated));

        // When
        var result = WeatherForecastProjector.Project(state, ev);

        // Then
        var updatedState = Assert.IsType<WeatherForecastState>(result);
        Assert.Equal("Osaka", updatedState.Location);
        Assert.Equal(25, updatedState.TemperatureC);
    }

    [Fact]
    public void Project_ShouldMarkDeletedFromDeletedEvent()
    {
        // Given
        var state = new WeatherForecastState(
            "forecast-1", "Tokyo", 25, "Warm", DateTimeOffset.UtcNow);
        var deleted = new WeatherForecastDeleted(
            "forecast-1", DateTimeOffset.UtcNow);
        var ev = CreateEvent(deleted, nameof(WeatherForecastDeleted));

        // When
        var result = WeatherForecastProjector.Project(state, ev);

        // Then
        var deletedState = Assert.IsType<WeatherForecastState>(result);
        Assert.True(deletedState.IsDeleted);
        Assert.NotNull(deletedState.DeletedAt);
    }

    [Fact]
    public void Project_ShouldReturnEmptyForUnknownEventOnEmptyState()
    {
        // Given
        var empty = new EmptyTagStatePayload();
        var updated = new WeatherForecastLocationUpdated(
            "forecast-1", "Osaka", DateTimeOffset.UtcNow);
        var ev = CreateEvent(updated, nameof(WeatherForecastLocationUpdated));

        // When
        var result = WeatherForecastProjector.Project(empty, ev);

        // Then
        Assert.IsType<EmptyTagStatePayload>(result);
    }

    private static Event CreateEvent(IEventPayload payload, string eventType)
    {
        var id = Guid.NewGuid();
        var sortableId = Sekiban.Dcb.Common.SortableUniqueId.Generate(DateTime.UtcNow, id);
        var metadata = new EventMetadata(id.ToString(), eventType, "test");
        return new Event(payload, sortableId, eventType, id, metadata, []);
    }
}
