using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.WasmRuntime.Remote;
using SekibanWasm.Cs.Domain;
using SekibanWasm.Cs.Domain.Weather;
using Xunit;

namespace SekibanWasm.Cs.Tests;

public class SerializedCommitResultRepublisherTests
{
    [Fact]
    public async Task PublishWrittenEventsAsync_ShouldPublishDeserializedEventsWithTags()
    {
        var domainTypes = DomainType.GetDomainTypes();

        var publisher = new RecordingEventPublisher();
        var payload = new WeatherForecastCreated(
            "forecast-1",
            "Tokyo",
            22,
            "Sunny",
            DateTimeOffset.Parse("2026-03-27T15:00:00+00:00"));
        var serializableEvent = new Event(
            payload,
            "063910220999999999999999999999",
            nameof(WeatherForecastCreated),
            Guid.NewGuid(),
            new EventMetadata("", "", ""),
            [new WeatherForecastTag("9004").GetTag()])
            .ToSerializableEvent(domainTypes.EventTypes);
        var result = new SerializedCommitResult(
            [serializableEvent],
            [],
            TimeSpan.Zero);

        await SerializedCommitResultRepublisher.PublishWrittenEventsAsync(result, domainTypes, publisher);

        var published = Assert.Single(publisher.Published);
        Assert.Equal(nameof(WeatherForecastCreated), published.Event.EventType);
        var deserialized = Assert.IsType<WeatherForecastCreated>(published.Event.Payload);
        Assert.Equal("forecast-1", deserialized.ForecastId);
        Assert.Equal("Tokyo", deserialized.Location);
        Assert.Equal("weather:9004", Assert.Single(published.Tags).GetTag());
    }

    private sealed class RecordingEventPublisher : IEventPublisher
    {
        public List<(Event Event, IReadOnlyCollection<ITag> Tags)> Published { get; } = [];

        public Task PublishAsync(
            IReadOnlyCollection<(Event Event, IReadOnlyCollection<ITag> Tags)> events,
            CancellationToken cancellationToken = default)
        {
            Published.AddRange(events);
            return Task.CompletedTask;
        }
    }
}
