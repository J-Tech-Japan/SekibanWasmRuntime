using System.Text;
using System.Text.Json;
using ResultBoxes;
using Sekiban.Dcb;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.WasmRuntime;
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
        var endpoints = new SerializedCommandEndpoints(stubExecutor, registry, JsonOptions);

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
        var endpoints = new SerializedCommandEndpoints(stubExecutor, registry, JsonOptions);

        var request = new SerializedCommandExecuteRequest(
            CommandName: "NonExistentCommand",
            CommandJson: "{}",
            ConsistencyTags: null,
            Options: null);

        // When / Then
        await Assert.ThrowsAsync<ArgumentException>(
            () => endpoints.ExecuteAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnException_WhenDeserializationFails()
    {
        // Given
        var stubExecutor = new StubSekibanExecutor();
        var registry = CreateRegistryWithWeatherCommands();
        var endpoints = new SerializedCommandEndpoints(stubExecutor, registry, JsonOptions);

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
        var endpoints = new SerializedCommandEndpoints(stubExecutor, registry, JsonOptions);

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
    }

    private record TestEventPayload(string ForecastId, string Location) : IEventPayload;

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
    }
}
