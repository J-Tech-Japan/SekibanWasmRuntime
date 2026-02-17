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
        ISerializedDcbClient client = new InProcSerializedDcbClient(executor);

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
        ISerializedDcbClient client = new InProcSerializedDcbClient(executor);

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
        ISerializedDcbClient client = new InProcSerializedDcbClient(executor);

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
        ISerializedDcbClient client = new InProcSerializedDcbClient(executor);

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
        ISerializedDcbClient client = new InProcSerializedDcbClient(executor);

        // When
        var result = await client.CommitSerializableEventsAsync(request, cts.Token);

        // Then
        Assert.True(result.IsSuccess);
        Assert.Equal(cts.Token, executor.LastCancellationToken);
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
}
