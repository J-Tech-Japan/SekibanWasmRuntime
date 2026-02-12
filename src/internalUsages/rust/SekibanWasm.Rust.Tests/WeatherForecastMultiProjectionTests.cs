using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
using SekibanWasm.Rust.Domain;
using SekibanWasm.Rust.Domain.Weather;
using Xunit;

namespace SekibanWasm.Rust.Tests;

public class WeatherForecastMultiProjectionTests
{
    private readonly Sekiban.Dcb.DcbDomainTypes _domainTypes = DomainType.GetDomainTypes();

    [Fact]
    public void Project_ShouldAddForecastFromCreatedEvent()
    {
        // Given
        var initial = WeatherForecastMultiProjection.GenerateInitialPayload();
        var created = new WeatherForecastCreated(
            "forecast-1", "Tokyo", 25, "Warm", DateTimeOffset.UtcNow);
        var ev = CreateEvent(created, nameof(WeatherForecastCreated));
        var tags = new List<ITag> { new WeatherForecastTag("forecast-1") };

        // When
        var result = WeatherForecastMultiProjection.Project(
            initial, ev, tags, _domainTypes,
            Sekiban.Dcb.Common.SortableUniqueId.Generate(DateTime.UtcNow, Guid.NewGuid()));

        // Then
        Assert.Single(result.Forecasts);
        Assert.True(result.Forecasts.ContainsKey("forecast-1"));
        Assert.Equal("Tokyo", result.Forecasts["forecast-1"].Location);
    }

    [Fact]
    public void Project_ShouldUpdateLocationFromLocationUpdatedEvent()
    {
        // Given
        var state = WeatherForecastMultiProjection.GenerateInitialPayload() with
        {
            Forecasts = new Dictionary<string, WeatherForecastItem>
            {
                ["forecast-1"] = new("forecast-1", "Tokyo", 25, "Warm", DateTimeOffset.UtcNow)
            }
        };
        var updated = new WeatherForecastLocationUpdated(
            "forecast-1", "Osaka", DateTimeOffset.UtcNow);
        var ev = CreateEvent(updated, nameof(WeatherForecastLocationUpdated));
        var tags = new List<ITag> { new WeatherForecastTag("forecast-1") };

        // When
        var result = WeatherForecastMultiProjection.Project(
            state, ev, tags, _domainTypes,
            Sekiban.Dcb.Common.SortableUniqueId.Generate(DateTime.UtcNow, Guid.NewGuid()));

        // Then
        Assert.Equal("Osaka", result.Forecasts["forecast-1"].Location);
    }

    [Fact]
    public void Project_ShouldMarkDeletedFromDeletedEvent()
    {
        // Given
        var state = WeatherForecastMultiProjection.GenerateInitialPayload() with
        {
            Forecasts = new Dictionary<string, WeatherForecastItem>
            {
                ["forecast-1"] = new("forecast-1", "Tokyo", 25, "Warm", DateTimeOffset.UtcNow)
            }
        };
        var deleted = new WeatherForecastDeleted("forecast-1", DateTimeOffset.UtcNow);
        var ev = CreateEvent(deleted, nameof(WeatherForecastDeleted));
        var tags = new List<ITag> { new WeatherForecastTag("forecast-1") };

        // When
        var result = WeatherForecastMultiProjection.Project(
            state, ev, tags, _domainTypes,
            Sekiban.Dcb.Common.SortableUniqueId.Generate(DateTime.UtcNow, Guid.NewGuid()));

        // Then
        Assert.True(result.Forecasts["forecast-1"].IsDeleted);
    }

    [Fact]
    public void Project_ShouldHandleMultipleForecasts()
    {
        // Given
        var state = WeatherForecastMultiProjection.GenerateInitialPayload();
        var threshold = Sekiban.Dcb.Common.SortableUniqueId.Generate(DateTime.UtcNow, Guid.NewGuid());

        var created1 = new WeatherForecastCreated(
            "forecast-1", "Tokyo", 25, "Warm", DateTimeOffset.UtcNow);
        var created2 = new WeatherForecastCreated(
            "forecast-2", "London", 15, "Cool", DateTimeOffset.UtcNow);

        // When
        state = WeatherForecastMultiProjection.Project(
            state, CreateEvent(created1, nameof(WeatherForecastCreated)),
            [new WeatherForecastTag("forecast-1")], _domainTypes, threshold);
        state = WeatherForecastMultiProjection.Project(
            state, CreateEvent(created2, nameof(WeatherForecastCreated)),
            [new WeatherForecastTag("forecast-2")], _domainTypes, threshold);

        // Then
        Assert.Equal(2, state.Forecasts.Count);
        Assert.Equal("Tokyo", state.Forecasts["forecast-1"].Location);
        Assert.Equal("London", state.Forecasts["forecast-2"].Location);
    }

    private static Event CreateEvent(IEventPayload payload, string eventType)
    {
        var id = Guid.NewGuid();
        var sortableId = Sekiban.Dcb.Common.SortableUniqueId.Generate(DateTime.UtcNow, id);
        var metadata = new EventMetadata(id.ToString(), eventType, "test");
        return new Event(payload, sortableId, eventType, id, metadata, []);
    }
}
