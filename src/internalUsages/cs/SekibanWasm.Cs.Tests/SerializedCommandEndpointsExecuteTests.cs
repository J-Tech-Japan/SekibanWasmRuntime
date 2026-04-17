using System.Text;
using System.Text.Json;
using ResultBoxes;
using Sekiban.Dcb;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.WasmRuntime;
using SekibanWasm.Cs.Domain;
using SekibanWasm.Cs.Domain.Weather;
using Xunit;

namespace SekibanWasm.Cs.Tests;

public class SerializedCommandEndpointsExecuteTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static SerializedCommandTypeRegistry CreateRegistryWithWeatherCommands() =>
        new(new[] { typeof(CreateWeatherForecast), typeof(DeleteWeatherForecast) });

    [Fact]
    public async Task ExecuteAsync_ShouldReturnResponse_WhenCommandSucceeds()
    {
        // Given
        var testPayload = new TestEventPayload("f-1", "Tokyo");
        var executionResult = new ExecutionResult(
            EventId: Guid.NewGuid(),
            EventPosition: 1,
            TagWrites: new List<TagWriteResult>
            {
                new("weather:f-1", Version: 1, WrittenAt: DateTimeOffset.UtcNow)
            },
            Duration: TimeSpan.FromMilliseconds(10),
            Events: new List<Event>
            {
                new(
                    Payload: testPayload,
                    SortableUniqueIdValue: "uid-001",
                    EventType: "WeatherForecastCreated",
                    Id: Guid.NewGuid(),
                    EventMetadata: new EventMetadata("", "", ""),
                    Tags: new List<string> { "weather:f-1" })
            },
            Metadata: new Dictionary<string, object>(),
            SortableUniqueId: "uid-001");

        var stubExecutor = new StubSekibanExecutor { ResultToReturn = executionResult };
        var registry = CreateRegistryWithWeatherCommands();
        var eventTypes = DomainType.GetDomainTypes().EventTypes;
        var endpoints = new SerializedCommandEndpoints(stubExecutor, eventTypes, registry, JsonOptions);

        var request = new SerializedCommandExecuteRequest(
            CommandName: "CreateWeatherForecast",
            CommandJson: JsonSerializer.Serialize(
                new CreateWeatherForecast("f-1", "Tokyo", 22, "Warm"), JsonOptions),
            ConsistencyTags: null,
            Options: null);

        // When
        var result = await endpoints.ExecuteAsync(request, CancellationToken.None);

        // Then
        Assert.True(result.IsSuccess);
        var response = result.GetValue();
        Assert.Single(response.EventCandidates);
        Assert.Equal("WeatherForecastCreated", response.EventCandidates[0].EventPayloadName);
        Assert.Single(response.EventCandidates[0].Tags);
        Assert.Equal("weather:f-1", response.EventCandidates[0].Tags[0]);

        Assert.Single(response.ConsistencyTags);
        Assert.Equal("weather:f-1", response.ConsistencyTags[0].Tag);
        Assert.Equal("uid-001", response.ConsistencyTags[0].LastSortableUniqueId);
        Assert.Equal(executionResult.EventId, response.FirstEventId);
        Assert.Equal("uid-001", response.LastSortableUniqueId);

        var decodedPayload = Encoding.UTF8.GetString(
            Convert.FromBase64String(response.EventCandidates[0].PayloadBase64));
        Assert.Contains("f-1", decodedPayload);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldThrow_WhenCommandNameIsUnknown()
    {
        // Given
        var stubExecutor = new StubSekibanExecutor();
        var registry = CreateRegistryWithWeatherCommands();
        var endpoints = new SerializedCommandEndpoints(
            stubExecutor,
            DomainType.GetDomainTypes().EventTypes,
            registry,
            JsonOptions);

        var request = new SerializedCommandExecuteRequest(
            CommandName: "NonExistentCommand",
            CommandJson: "{}",
            ConsistencyTags: null,
            Options: null);

        // When
        var result = await endpoints.ExecuteAsync(request, CancellationToken.None);

        // Then
        Assert.False(result.IsSuccess);
        Assert.IsType<ArgumentException>(result.GetException());
        Assert.Contains("Unknown command type", result.GetException().Message);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnException_WhenDeserializationFails()
    {
        // Given
        var stubExecutor = new StubSekibanExecutor();
        var registry = CreateRegistryWithWeatherCommands();
        var endpoints = new SerializedCommandEndpoints(
            stubExecutor,
            DomainType.GetDomainTypes().EventTypes,
            registry,
            JsonOptions);

        var request = new SerializedCommandExecuteRequest(
            CommandName: "CreateWeatherForecast",
            CommandJson: "null",
            ConsistencyTags: null,
            Options: null);

        // When
        var result = await endpoints.ExecuteAsync(request, CancellationToken.None);

        // Then
        Assert.False(result.IsSuccess);
        Assert.IsType<ArgumentException>(result.GetException());
        Assert.Contains("Failed to deserialize", result.GetException().Message);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldMapEventsAndTagsCorrectly()
    {
        // Given
        var payload1 = new TestEventPayload("f-1", "Tokyo");
        var payload2 = new TestEventPayload("f-1", "Updated");
        var executionResult = new ExecutionResult(
            EventId: Guid.NewGuid(),
            EventPosition: 2,
            TagWrites: new List<TagWriteResult>
            {
                new("weather:f-1", Version: 1, WrittenAt: DateTimeOffset.UtcNow),
                new("forecast:global", Version: 3, WrittenAt: DateTimeOffset.UtcNow)
            },
            Duration: TimeSpan.FromMilliseconds(5),
            Events: new List<Event>
            {
                new(
                    Payload: payload1,
                    SortableUniqueIdValue: "uid-002",
                    EventType: "WeatherForecastCreated",
                    Id: Guid.NewGuid(),
                    EventMetadata: new EventMetadata("", "", ""),
                    Tags: new List<string> { "weather:f-1" }),
                new(
                    Payload: payload2,
                    SortableUniqueIdValue: "uid-002",
                    EventType: "LocationUpdated",
                    Id: Guid.NewGuid(),
                    EventMetadata: new EventMetadata("", "", ""),
                    Tags: new List<string> { "weather:f-1", "forecast:global" })
            },
            Metadata: new Dictionary<string, object>(),
            SortableUniqueId: "uid-002");

        var stubExecutor = new StubSekibanExecutor { ResultToReturn = executionResult };
        var registry = CreateRegistryWithWeatherCommands();
        var endpoints = new SerializedCommandEndpoints(
            stubExecutor,
            DomainType.GetDomainTypes().EventTypes,
            registry,
            JsonOptions);

        var request = new SerializedCommandExecuteRequest(
            CommandName: "CreateWeatherForecast",
            CommandJson: JsonSerializer.Serialize(
                new CreateWeatherForecast("f-1", "Tokyo", 22, "Warm"), JsonOptions),
            ConsistencyTags: null,
            Options: null);

        // When
        var result = await endpoints.ExecuteAsync(request, CancellationToken.None);

        // Then
        Assert.True(result.IsSuccess);
        var response = result.GetValue();

        Assert.Equal(2, response.EventCandidates.Count);
        Assert.Equal("WeatherForecastCreated", response.EventCandidates[0].EventPayloadName);
        Assert.Equal("LocationUpdated", response.EventCandidates[1].EventPayloadName);
        Assert.Equal(2, response.EventCandidates[1].Tags.Count);

        Assert.Equal(2, response.ConsistencyTags.Count);
        Assert.Equal("weather:f-1", response.ConsistencyTags[0].Tag);
        Assert.Equal("forecast:global", response.ConsistencyTags[1].Tag);
        Assert.All(response.ConsistencyTags, ct => Assert.Equal("uid-002", ct.LastSortableUniqueId));
        Assert.Equal(executionResult.EventId, response.FirstEventId);
        Assert.Equal("uid-002", response.LastSortableUniqueId);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldUseCommitRequestBuilder_WhenAvailable()
    {
        var commitRequest = new SerializedCommitRequest(
            EventCandidates:
            [
                new SerializableEventCandidate(
                    Payload: Encoding.UTF8.GetBytes("""{"forecastId":"f-1"}"""),
                    EventPayloadName: nameof(WeatherForecastCreated),
                    Tags: ["weather:f-1"])
            ],
            ConsistencyTags:
            [
                new ConsistencyTagEntry("weather:f-1", string.Empty)
            ]);

        var stubExecutor = new StubSekibanExecutor();
        var stubBuilder = new StubCommitRequestBuilder { RequestToReturn = commitRequest };
        var endpoints = new SerializedCommandEndpoints(
            stubExecutor,
            DomainType.GetDomainTypes().EventTypes,
            CreateRegistryWithWeatherCommands(),
            JsonOptions,
            stubBuilder);

        var request = new SerializedCommandExecuteRequest(
            CommandName: "CreateWeatherForecast",
            CommandJson: JsonSerializer.Serialize(
                new CreateWeatherForecast("f-1", "Tokyo", 22, "Warm"), JsonOptions),
            ConsistencyTags: null,
            Options: null);

        var result = await endpoints.ExecuteAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(nameof(CreateWeatherForecast), stubBuilder.LastCommandName);
        Assert.Single(result.GetValue().EventCandidates);
        Assert.Equal("weather:f-1", result.GetValue().ConsistencyTags[0].Tag);
        Assert.Null(result.GetValue().FirstEventId);
        Assert.Null(result.GetValue().LastSortableUniqueId);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldNotifyPersistedEventObservers_WhenCommandPersistsEvents()
    {
        var executionResult = new ExecutionResult(
            EventId: Guid.NewGuid(),
            EventPosition: 1,
            TagWrites: [new("weather:f-1", 1, DateTimeOffset.UtcNow)],
            Duration: TimeSpan.FromMilliseconds(10),
            Events:
            [
                new Event(
                    Payload: new TestEventPayload("f-1", "Tokyo"),
                    SortableUniqueIdValue: "uid-003",
                    EventType: "WeatherForecastCreated",
                    Id: Guid.NewGuid(),
                    EventMetadata: new EventMetadata("", "", ""),
                    Tags: ["weather:f-1"])
            ],
            Metadata: null,
            SortableUniqueId: "uid-003");

        var observer = new StubPersistedEventObserver();
        var endpoints = new SerializedCommandEndpoints(
            new StubSekibanExecutor { ResultToReturn = executionResult },
            DomainType.GetDomainTypes().EventTypes,
            CreateRegistryWithWeatherCommands(),
            JsonOptions,
            commitRequestBuilder: null,
            persistedEventObservers: [observer]);

        var request = new SerializedCommandExecuteRequest(
            CommandName: "CreateWeatherForecast",
            CommandJson: JsonSerializer.Serialize(
                new CreateWeatherForecast("f-1", "Tokyo", 22, "Warm"), JsonOptions),
            ConsistencyTags: null,
            Options: null);

        var result = await endpoints.ExecuteAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(observer.PersistedEvents);
        Assert.Equal("WeatherForecastCreated", observer.PersistedEvents[0].EventPayloadName);
        Assert.Equal("uid-003", observer.PersistedEvents[0].SortableUniqueIdValue);
    }

    private record TestEventPayload(string ForecastId, string Location) : IEventPayload;

    private sealed class StubCommitRequestBuilder : ISekibanCommandCommitRequestBuilder
    {
        public string? LastCommandName { get; private set; }
        public object? LastCommand { get; private set; }
        public SerializedCommitRequest RequestToReturn { get; init; } = new([], []);

        public Task<SerializedCommitRequest> BuildCommitRequestAsync(
            string commandName,
            object command,
            CancellationToken cancellationToken = default)
        {
            LastCommandName = commandName;
            LastCommand = command;
            return Task.FromResult(RequestToReturn);
        }
    }

    private sealed class StubSekibanExecutor : ISekibanExecutor
    {
        public ExecutionResult? ResultToReturn { get; set; }

        Task<ExecutionResult> ICommandExecutor.ExecuteAsync<TCommand>(
            TCommand command,
            CancellationToken cancellationToken)
        {
            if (ResultToReturn is null)
            {
                throw new InvalidOperationException("ResultToReturn not configured");
            }
            return Task.FromResult(ResultToReturn);
        }

        Task<ExecutionResult> ICommandExecutor.ExecuteAsync<TCommand>(
            TCommand command,
            Func<TCommand, ICommandContext, Task<EventOrNone>> handlerFunc,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        Task<ExecutionResult> ICommandExecutor.ExecuteCommandAsync(
            Func<ICommandContext, Task<EventOrNone>> handlerFunc,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        Task<TagState> ISekibanExecutor.GetTagStateAsync(TagStateId tagStateId) =>
            throw new NotSupportedException();

        Task<TagState> ISekibanExecutor.GetTagStateAsync<TProjector>(ITag tag) =>
            throw new NotSupportedException();

        Task<TResult> ISekibanExecutor.QueryAsync<TResult>(IQueryCommon<TResult> queryCommon) =>
            throw new NotSupportedException();

        Task<ListQueryResult<TResult>> ISekibanExecutor.QueryAsync<TResult>(IListQueryCommon<TResult> queryCommon) =>
            throw new NotSupportedException();

        Task<string> ISekibanExecutor.GetLatestSortableUniqueIdAsync() =>
            throw new NotSupportedException();

        Task<ProjectionHeadStatus> ISekibanExecutor.GetProjectionHeadStatusAsync(
            string projectorName,
            string? expectedProjectorVersion) =>
            throw new NotSupportedException();

        Task<EventStoreHeadStatus> ISekibanExecutor.GetEventStoreHeadStatusAsync(bool includeTotalEventCount) =>
            throw new NotSupportedException();
    }

    private sealed class StubPersistedEventObserver : IPersistedSerializableEventObserver
    {
        public List<SerializableEvent> PersistedEvents { get; } = [];

        public Task OnPersistedAsync(
            IReadOnlyList<SerializableEvent> events,
            CancellationToken cancellationToken)
        {
            PersistedEvents.AddRange(events);
            return Task.CompletedTask;
        }
    }
}
