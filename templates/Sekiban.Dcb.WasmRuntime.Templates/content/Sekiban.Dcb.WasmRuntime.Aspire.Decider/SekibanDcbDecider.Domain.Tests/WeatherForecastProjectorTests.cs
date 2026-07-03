using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
using SekibanDcbDecider.Domain.Weather;
using Xunit;

namespace SekibanDcbDecider.Domain.Tests;

public class WeatherForecastProjectorTests
{
    private static Event MakeEvent(IEventPayload payload) => new(
        payload,
        SortableUniqueIdValue: Guid.NewGuid().ToString("N"),
        EventType: payload.GetType().Name,
        Id: Guid.NewGuid(),
        EventMetadata: new EventMetadata("test", "test", "tests"),
        Tags: []);

    [Fact]
    public void Created_event_projects_initial_state()
    {
        var createdAt = DateTimeOffset.UtcNow;
        var ev = MakeEvent(new WeatherForecastCreated("f-1", "Kyoto", 24, "Sunny", createdAt));

        var state = WeatherForecastProjector.Project(new EmptyTagStatePayload(), ev);

        var forecast = Assert.IsType<WeatherForecastState>(state);
        Assert.Equal("f-1", forecast.ForecastId);
        Assert.Equal("Kyoto", forecast.Location);
        Assert.Equal(24, forecast.TemperatureC);
        Assert.False(forecast.IsDeleted);
    }

    [Fact]
    public void Location_update_changes_only_location()
    {
        var created = WeatherForecastProjector.Project(
            new EmptyTagStatePayload(),
            MakeEvent(new WeatherForecastCreated("f-1", "Kyoto", 24, "Sunny", DateTimeOffset.UtcNow)));

        var updated = WeatherForecastProjector.Project(
            created,
            MakeEvent(new WeatherForecastLocationUpdated("f-1", "Osaka", DateTimeOffset.UtcNow)));

        var forecast = Assert.IsType<WeatherForecastState>(updated);
        Assert.Equal("Osaka", forecast.Location);
        Assert.Equal(24, forecast.TemperatureC);
    }

    [Fact]
    public void Delete_event_marks_state_deleted()
    {
        var created = WeatherForecastProjector.Project(
            new EmptyTagStatePayload(),
            MakeEvent(new WeatherForecastCreated("f-1", "Kyoto", 24, "Sunny", DateTimeOffset.UtcNow)));

        var deletedAt = DateTimeOffset.UtcNow;
        var deleted = WeatherForecastProjector.Project(
            created,
            MakeEvent(new WeatherForecastDeleted("f-1", deletedAt)));

        var forecast = Assert.IsType<WeatherForecastState>(deleted);
        Assert.True(forecast.IsDeleted);
        Assert.Equal(deletedAt, forecast.DeletedAt);
    }
}
