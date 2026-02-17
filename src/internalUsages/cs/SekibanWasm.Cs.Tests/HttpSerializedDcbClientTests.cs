using System.Net;
using System.Text;
using System.Text.Json;
using ResultBoxes;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.WasmRuntime;
using Sekiban.Dcb.WasmRuntime.Remote;
using Xunit;

namespace SekibanWasm.Cs.Tests;

public class HttpSerializedDcbClientTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public async Task GetSerializableTagStateAsync_ShouldReturnState_WhenServerReturns200()
    {
        // Given
        var tagStateId = TagStateId.Parse("weather:f-1:WeatherForecastProjector");
        var expectedState = new SerializableTagState(
            Payload: Encoding.UTF8.GetBytes("{\"forecastId\":\"f-1\"}"),
            Version: 5,
            LastSortedUniqueId: "sorted-id-1",
            ProjectorVersion: "v1",
            TagPayloadName: "WeatherForecastState",
            TagGroup: "weather",
            TagContent: "f-1",
            TagProjector: "WeatherForecastProjector");

        var handler = new StubHttpMessageHandler(
            HttpStatusCode.OK,
            JsonSerializer.Serialize(expectedState, JsonOptions));

        var client = CreateClient(handler);

        // When
        var result = await client.GetSerializableTagStateAsync(tagStateId);

        // Then
        Assert.True(result.IsSuccess);
        var state = result.GetValue();
        Assert.Equal(5, state.Version);
        Assert.Equal("v1", state.ProjectorVersion);
        Assert.Equal("weather", state.TagGroup);
        Assert.Equal("f-1", state.TagContent);

        // Verify the request was sent to the correct URL
        Assert.NotNull(handler.LastRequest);
        Assert.Equal("https://localhost:5001/api/sekiban/serialized/tag-state",
            handler.LastRequest.RequestUri?.ToString());
        Assert.Equal(HttpMethod.Post, handler.LastRequest.Method);
    }

    [Fact]
    public async Task GetSerializableTagStateAsync_ShouldReturnError_WhenServerReturns400()
    {
        // Given
        var tagStateId = TagStateId.Parse("weather:f-1:WeatherForecastProjector");
        var handler = new StubHttpMessageHandler(
            HttpStatusCode.BadRequest,
            "{\"error\":\"Tag not found\"}");

        var client = CreateClient(handler);

        // When
        var result = await client.GetSerializableTagStateAsync(tagStateId);

        // Then
        Assert.False(result.IsSuccess);
        var ex = result.GetException();
        Assert.IsType<HttpRequestException>(ex);
        Assert.Contains("BadRequest", ex.Message);
    }

    [Fact]
    public async Task GetSerializableTagStateAsync_ShouldReturnError_WhenServerReturns500()
    {
        // Given
        var tagStateId = TagStateId.Parse("weather:f-1:WeatherForecastProjector");
        var handler = new StubHttpMessageHandler(
            HttpStatusCode.InternalServerError,
            "Internal Server Error");

        var client = CreateClient(handler);

        // When
        var result = await client.GetSerializableTagStateAsync(tagStateId);

        // Then
        Assert.False(result.IsSuccess);
        Assert.IsType<HttpRequestException>(result.GetException());
    }

    [Fact]
    public async Task CommitSerializableEventsAsync_ShouldReturnResult_WhenServerReturns200()
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

        var expectedResult = new SerializedCommitResult(
            WrittenEvents: new List<SerializableEvent>(),
            TagWriteResults: new List<TagWriteResult>(),
            Duration: TimeSpan.FromMilliseconds(42));

        var handler = new StubHttpMessageHandler(
            HttpStatusCode.OK,
            JsonSerializer.Serialize(expectedResult, JsonOptions));

        var client = CreateClient(handler);

        // When
        var result = await client.CommitSerializableEventsAsync(request, CancellationToken.None);

        // Then
        Assert.True(result.IsSuccess);

        // Verify the request was sent to the correct URL
        Assert.NotNull(handler.LastRequest);
        Assert.Equal("https://localhost:5001/api/sekiban/serialized/commit",
            handler.LastRequest.RequestUri?.ToString());
        Assert.Equal(HttpMethod.Post, handler.LastRequest.Method);
    }

    [Fact]
    public async Task CommitSerializableEventsAsync_ShouldReturnError_WhenServerReturns400()
    {
        // Given
        var request = new SerializedCommitRequest(
            EventCandidates: new List<SerializableEventCandidate>(),
            ConsistencyTags: new List<ConsistencyTagEntry>());

        var handler = new StubHttpMessageHandler(
            HttpStatusCode.BadRequest,
            "{\"error\":\"Consistency conflict\"}");

        var client = CreateClient(handler);

        // When
        var result = await client.CommitSerializableEventsAsync(request, CancellationToken.None);

        // Then
        Assert.False(result.IsSuccess);
        Assert.IsType<HttpRequestException>(result.GetException());
        Assert.Contains("BadRequest", result.GetException().Message);
    }

    [Fact]
    public async Task CommitSerializableEventsAsync_ShouldSendCorrectRequestBody()
    {
        // Given
        var request = new SerializedCommitRequest(
            EventCandidates: new List<SerializableEventCandidate>
            {
                new(
                    Payload: new byte[] { 1, 2, 3 },
                    EventPayloadName: "TestEvent",
                    Tags: new List<string> { "tag:value" })
            },
            ConsistencyTags: new List<ConsistencyTagEntry>
            {
                new(Tag: "tag:value", LastSortableUniqueId: "uid-1")
            });

        var expectedResult = new SerializedCommitResult(
            WrittenEvents: new List<SerializableEvent>(),
            TagWriteResults: new List<TagWriteResult>(),
            Duration: TimeSpan.Zero);

        var handler = new StubHttpMessageHandler(
            HttpStatusCode.OK,
            JsonSerializer.Serialize(expectedResult, JsonOptions));

        var client = CreateClient(handler);

        // When
        await client.CommitSerializableEventsAsync(request, CancellationToken.None);

        // Then
        Assert.NotNull(handler.LastRequestBody);
        Assert.Contains("TestEvent", handler.LastRequestBody);
        Assert.Contains("tag:value", handler.LastRequestBody);
    }

    [Fact]
    public async Task BaseUrl_TrailingSlash_ShouldBeHandledCorrectly()
    {
        // Given - BaseUrl with trailing slash
        var tagStateId = TagStateId.Parse("weather:f-1:WeatherForecastProjector");
        var expectedState = new SerializableTagState(
            Payload: Array.Empty<byte>(),
            Version: 0,
            LastSortedUniqueId: "",
            ProjectorVersion: "v1",
            TagPayloadName: "",
            TagGroup: "weather",
            TagContent: "f-1",
            TagProjector: "WeatherForecastProjector");

        var handler = new StubHttpMessageHandler(
            HttpStatusCode.OK,
            JsonSerializer.Serialize(expectedState, JsonOptions));

        var options = new SerializedDcbClientOptions { BaseUrl = "https://localhost:5001/" };
        var httpClient = new HttpClient(handler);
        var client = new HttpSerializedDcbClient(httpClient, options, JsonOptions);

        // When
        var result = await client.GetSerializableTagStateAsync(tagStateId);

        // Then
        Assert.True(result.IsSuccess);
        Assert.NotNull(handler.LastRequest);
        // Should NOT have double slash
        Assert.Equal("https://localhost:5001/api/sekiban/serialized/tag-state",
            handler.LastRequest.RequestUri?.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void Constructor_ShouldThrow_WhenBaseUrlIsEmpty(string? baseUrl)
    {
        // Given
        var options = new SerializedDcbClientOptions { BaseUrl = baseUrl! };
        var httpClient = new HttpClient();

        // When / Then
        var ex = Assert.Throws<ArgumentException>(
            () => new HttpSerializedDcbClient(httpClient, options, JsonOptions));
        Assert.Contains("BaseUrl", ex.Message);
    }

    private static HttpSerializedDcbClient CreateClient(StubHttpMessageHandler handler)
    {
        var options = new SerializedDcbClientOptions { BaseUrl = "https://localhost:5001" };
        var httpClient = new HttpClient(handler);
        return new HttpSerializedDcbClient(httpClient, options, JsonOptions);
    }

    /// <summary>
    ///     Stub HttpMessageHandler that captures the request and returns a pre-configured response.
    /// </summary>
    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseBody;

        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        public StubHttpMessageHandler(HttpStatusCode statusCode, string responseBody)
        {
            _statusCode = statusCode;
            _responseBody = responseBody;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content is not null)
            {
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
