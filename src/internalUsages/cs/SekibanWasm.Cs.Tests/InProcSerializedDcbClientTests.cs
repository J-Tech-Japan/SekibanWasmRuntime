using System.Text;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.WasmRuntime;
using Xunit;

namespace SekibanWasm.Cs.Tests;

public class InProcSerializedDcbClientTests
{
    [Fact]
    public async Task GetSerializableTagStateAsync_ShouldDelegateToExecutor()
    {
        // Given
        var tagStateId = TagStateId.Parse("weather:f-1:WeatherForecastProjector");
        var expected = new SerializableTagState(
            Payload: new byte[] { 1, 2, 3 },
            Version: 5,
            LastSortedUniqueId: "sorted-id-1",
            ProjectorVersion: "v1",
            TagPayloadName: "WeatherForecastState",
            TagGroup: "weather",
            TagContent: "f-1",
            TagProjector: "WeatherForecastProjector");

        var executor = new StubSerializedSekibanDcbExecutor
        {
            TagStateToReturn = ResultBox.FromValue(expected)
        };
        ISerializedDcbClient client = new InProcSerializedDcbClient(executor, new StubCommandExecutor());

        // When
        var result = await client.GetSerializableTagStateAsync(tagStateId);

        // Then
        Assert.True(result.IsSuccess);
        var state = result.GetValue();
        Assert.Equal(5, state.Version);
        Assert.Equal("v1", state.ProjectorVersion);
        Assert.Equal("weather", state.TagGroup);
        Assert.Equal("f-1", state.TagContent);
        Assert.Equal("WeatherForecastProjector", state.TagProjector);
        Assert.Equal(tagStateId.GetTagStateId(), executor.LastRequestedTagStateId);
    }

    [Fact]
    public async Task GetSerializableTagStateAsync_ShouldPropagateError()
    {
        // Given
        var tagStateId = TagStateId.Parse("weather:f-1:WeatherForecastProjector");
        var executor = new StubSerializedSekibanDcbExecutor
        {
            TagStateToReturn = ResultBox<SerializableTagState>.FromException(
                new InvalidOperationException("Tag not found"))
        };
        ISerializedDcbClient client = new InProcSerializedDcbClient(executor, new StubCommandExecutor());

        // When
        var result = await client.GetSerializableTagStateAsync(tagStateId);

        // Then
        Assert.False(result.IsSuccess);
        Assert.IsType<InvalidOperationException>(result.GetException());
        Assert.Equal("Tag not found", result.GetException().Message);
    }

    [Fact]
    public async Task CommitSerializableEventsAsync_ShouldDelegateToExecutor()
    {
        // Given
        var request = new SerializedCommitRequest(
            EventCandidates: new List<SerializableEventCandidate>
            {
                new(
                    Payload: new byte[] { 10, 20, 30 },
                    EventPayloadName: "WeatherForecastCreated",
                    Tags: new List<string> { "weather:f-1" })
            },
            ConsistencyTags: new List<ConsistencyTagEntry>
            {
                new(Tag: "weather:f-1", LastSortableUniqueId: "")
            });

        var expected = new SerializedCommitResult(
            WrittenEvents: new List<SerializableEvent>(),
            TagWriteResults: new List<TagWriteResult>(),
            Duration: TimeSpan.FromMilliseconds(42));

        var executor = new StubSerializedSekibanDcbExecutor
        {
            CommitResultToReturn = ResultBox.FromValue(expected)
        };
        ISerializedDcbClient client = new InProcSerializedDcbClient(executor, new StubCommandExecutor());

        // When
        var result = await client.CommitSerializableEventsAsync(request);

        // Then
        Assert.True(result.IsSuccess);
        var commitResult = result.GetValue();
        Assert.Equal(TimeSpan.FromMilliseconds(42), commitResult.Duration);
        Assert.Same(request, executor.LastCommitRequest);
    }

    [Fact]
    public async Task CommitSerializableEventsAsync_ShouldPropagateError()
    {
        // Given
        var request = new SerializedCommitRequest(
            EventCandidates: new List<SerializableEventCandidate>(),
            ConsistencyTags: new List<ConsistencyTagEntry>());

        var executor = new StubSerializedSekibanDcbExecutor
        {
            CommitResultToReturn = ResultBox<SerializedCommitResult>.FromException(
                new InvalidOperationException("Consistency conflict"))
        };
        ISerializedDcbClient client = new InProcSerializedDcbClient(executor, new StubCommandExecutor());

        // When
        var result = await client.CommitSerializableEventsAsync(request);

        // Then
        Assert.False(result.IsSuccess);
        Assert.Equal("Consistency conflict", result.GetException().Message);
    }

    [Fact]
    public async Task CommitSerializableEventsAsync_ShouldPassCancellationToken()
    {
        // Given
        var request = new SerializedCommitRequest(
            EventCandidates: new List<SerializableEventCandidate>(),
            ConsistencyTags: new List<ConsistencyTagEntry>());

        var expected = new SerializedCommitResult(
            WrittenEvents: new List<SerializableEvent>(),
            TagWriteResults: new List<TagWriteResult>(),
            Duration: TimeSpan.Zero);

        using var cts = new CancellationTokenSource();
        var executor = new StubSerializedSekibanDcbExecutor
        {
            CommitResultToReturn = ResultBox.FromValue(expected)
        };
        ISerializedDcbClient client = new InProcSerializedDcbClient(executor, new StubCommandExecutor());

        // When
        var result = await client.CommitSerializableEventsAsync(request, cts.Token);

        // Then
        Assert.True(result.IsSuccess);
        Assert.Equal(cts.Token, executor.LastCancellationToken);
    }

    [Fact]
    public async Task ExecuteSerializedCommandAsync_ShouldDelegateToCommandExecutor()
    {
        // Given
        var payloadBase64 = Convert.ToBase64String(
            Encoding.UTF8.GetBytes("{\"forecastId\":\"f-1\"}"));

        var expectedResponse = new SerializedCommandExecuteResponse(
            EventCandidates: new List<SerializedCommandEventCandidate>
            {
                new(
                    EventPayloadName: "WeatherForecastCreated",
                    PayloadBase64: payloadBase64,
                    Tags: new List<string> { "weather:f-1" })
            },
            ConsistencyTags: new List<ConsistencyTagEntry>
            {
                new(Tag: "weather:f-1", LastSortableUniqueId: "uid-001")
            },
            CommandResultJson: null);

        var commandExecutor = new StubCommandExecutor
        {
            ResponseToReturn = ResultBox.FromValue(expectedResponse)
        };
        var executor = new StubSerializedSekibanDcbExecutor();
        ISerializedDcbClient client = new InProcSerializedDcbClient(executor, commandExecutor);

        var request = new SerializedCommandExecuteRequest(
            CommandName: "CreateWeatherForecast",
            CommandJson: "{\"forecastId\":\"f-1\"}",
            ConsistencyTags: null,
            Options: null);

        // When
        var result = await client.ExecuteSerializedCommandAsync(request);

        // Then
        Assert.True(result.IsSuccess);
        var response = result.GetValue();
        Assert.Single(response.EventCandidates);
        Assert.Equal("WeatherForecastCreated", response.EventCandidates[0].EventPayloadName);
        Assert.Same(request, commandExecutor.LastRequest);
    }

    [Fact]
    public async Task ExecuteSerializedCommandAsync_ShouldPropagateError()
    {
        // Given
        var commandExecutor = new StubCommandExecutor
        {
            ResponseToReturn = ResultBox<SerializedCommandExecuteResponse>.FromException(
                new ArgumentException("Unknown command type: BadCommand"))
        };
        var executor = new StubSerializedSekibanDcbExecutor();
        ISerializedDcbClient client = new InProcSerializedDcbClient(executor, commandExecutor);

        var request = new SerializedCommandExecuteRequest(
            CommandName: "BadCommand",
            CommandJson: "{}",
            ConsistencyTags: null,
            Options: null);

        // When
        var result = await client.ExecuteSerializedCommandAsync(request);

        // Then
        Assert.False(result.IsSuccess);
        Assert.IsType<ArgumentException>(result.GetException());
        Assert.Contains("Unknown command type", result.GetException().Message);
    }

    [Fact]
    public async Task ExecuteSerializedCommandAsync_ShouldPassCancellationToken()
    {
        // Given
        var commandExecutor = new StubCommandExecutor
        {
            ResponseToReturn = ResultBox.FromValue(new SerializedCommandExecuteResponse(
                new List<SerializedCommandEventCandidate>(),
                new List<ConsistencyTagEntry>(),
                null))
        };
        var executor = new StubSerializedSekibanDcbExecutor();
        ISerializedDcbClient client = new InProcSerializedDcbClient(executor, commandExecutor);

        using var cts = new CancellationTokenSource();
        var request = new SerializedCommandExecuteRequest(
            CommandName: "CreateWeatherForecast",
            CommandJson: "{}",
            ConsistencyTags: null,
            Options: null);

        // When
        var result = await client.ExecuteSerializedCommandAsync(request, cts.Token);

        // Then
        Assert.True(result.IsSuccess);
        Assert.Equal(cts.Token, commandExecutor.LastCancellationToken);
    }

    /// <summary>
    ///     Manual stub for ISerializedSekibanDcbExecutor.
    ///     Records arguments and returns pre-configured results.
    /// </summary>
    private sealed class StubSerializedSekibanDcbExecutor : ISerializedSekibanDcbExecutor
    {
        public ResultBox<SerializableTagState>? TagStateToReturn { get; set; }
        public ResultBox<SerializedCommitResult>? CommitResultToReturn { get; set; }

        public string? LastRequestedTagStateId { get; private set; }
        public SerializedCommitRequest? LastCommitRequest { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }

        public Task<ResultBox<SerializableTagState>> GetSerializableTagStateAsync(TagStateId tagStateId)
        {
            LastRequestedTagStateId = tagStateId.GetTagStateId();
            return Task.FromResult(TagStateToReturn
                ?? ResultBox<SerializableTagState>.FromException(
                    new InvalidOperationException("TagStateToReturn not configured")));
        }

        public Task<ResultBox<SerializedCommitResult>> CommitSerializableEventsAsync(
            SerializedCommitRequest request,
            CancellationToken cancellationToken = default)
        {
            LastCommitRequest = request;
            LastCancellationToken = cancellationToken;
            return Task.FromResult(CommitResultToReturn
                ?? ResultBox<SerializedCommitResult>.FromException(
                    new InvalidOperationException("CommitResultToReturn not configured")));
        }
    }

    /// <summary>
    ///     Manual stub for ISerializedCommandExecutor.
    ///     Records arguments and returns pre-configured results.
    /// </summary>
    private sealed class StubCommandExecutor : ISerializedCommandExecutor
    {
        public ResultBox<SerializedCommandExecuteResponse>? ResponseToReturn { get; set; }
        public SerializedCommandExecuteRequest? LastRequest { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }

        public Task<ResultBox<SerializedCommandExecuteResponse>> ExecuteAsync(
            SerializedCommandExecuteRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastCancellationToken = cancellationToken;
            return Task.FromResult(ResponseToReturn
                ?? ResultBox<SerializedCommandExecuteResponse>.FromException(
                    new InvalidOperationException("ResponseToReturn not configured")));
        }
    }
}
