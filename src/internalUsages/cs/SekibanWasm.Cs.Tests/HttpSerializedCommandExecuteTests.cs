using System.Net;
using System.Text;
using System.Text.Json;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.WasmRuntime;
using Sekiban.Dcb.WasmRuntime.Remote;
using Xunit;

namespace SekibanWasm.Cs.Tests;

/// <summary>
///     Tests for HttpSerializedDcbClient.ExecuteSerializedCommandAsync.
/// </summary>
public class HttpSerializedCommandExecuteTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public async Task ExecuteSerializedCommandAsync_ShouldReturnResponse_WhenServerReturns200()
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
            CommandResultJson: null,
            FirstEventId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            LastSortableUniqueId: "uid-001");

        var handler = new StubHttpMessageHandler(
            HttpStatusCode.OK,
            JsonSerializer.Serialize(expectedResponse, JsonOptions));

        var client = CreateClient(handler);

        var request = new SerializedCommandExecuteRequest(
            CommandName: "CreateWeatherForecast",
            CommandJson: "{\"forecastId\":\"f-1\",\"location\":\"Tokyo\",\"temperatureC\":22,\"summary\":\"Warm\"}",
            ConsistencyTags: null,
            Options: null);

        // When
        var result = await client.ExecuteSerializedCommandAsync(request, CancellationToken.None);

        // Then
        Assert.True(result.IsSuccess);
        var response = result.GetValue();
        Assert.Single(response.EventCandidates);
        Assert.Equal("WeatherForecastCreated", response.EventCandidates[0].EventPayloadName);
        Assert.Equal(payloadBase64, response.EventCandidates[0].PayloadBase64);
        Assert.Single(response.ConsistencyTags);
        Assert.Equal("weather:f-1", response.ConsistencyTags[0].Tag);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), response.FirstEventId);
        Assert.Equal("uid-001", response.LastSortableUniqueId);
    }

    [Fact]
    public async Task ExecuteSerializedCommandAsync_ShouldPostToCorrectUrl()
    {
        // Given
        var handler = new StubHttpMessageHandler(
            HttpStatusCode.OK,
            JsonSerializer.Serialize(
                new SerializedCommandExecuteResponse(
                    new List<SerializedCommandEventCandidate>(),
                    new List<ConsistencyTagEntry>(),
                    null,
                    null,
                    null),
                JsonOptions));

        var client = CreateClient(handler);

        var request = new SerializedCommandExecuteRequest(
            CommandName: "DeleteWeatherForecast",
            CommandJson: "{\"forecastId\":\"f-1\"}",
            ConsistencyTags: null,
            Options: null);

        // When
        await client.ExecuteSerializedCommandAsync(request, CancellationToken.None);

        // Then
        Assert.NotNull(handler.LastRequest);
        Assert.Equal("https://localhost:5001/api/sekiban/serialized/command/execute",
            handler.LastRequest.RequestUri?.ToString());
        Assert.Equal(HttpMethod.Post, handler.LastRequest.Method);
    }

    [Fact]
    public async Task ExecuteSerializedCommandAsync_ShouldSendCorrectRequestBody()
    {
        // Given
        var handler = new StubHttpMessageHandler(
            HttpStatusCode.OK,
            JsonSerializer.Serialize(
                new SerializedCommandExecuteResponse(
                    new List<SerializedCommandEventCandidate>(),
                    new List<ConsistencyTagEntry>(),
                    null,
                    null,
                    null),
                JsonOptions));

        var client = CreateClient(handler);

        var request = new SerializedCommandExecuteRequest(
            CommandName: "CreateWeatherForecast",
            CommandJson: "{\"forecastId\":\"f-1\"}",
            ConsistencyTags: new List<ConsistencyTagEntry>
            {
                new(Tag: "weather:f-1", LastSortableUniqueId: "uid-001")
            },
            Options: new SerializedCommandOptions(DryRun: true, WaitForSortableUniqueId: null));

        // When
        await client.ExecuteSerializedCommandAsync(request, CancellationToken.None);

        // Then
        Assert.NotNull(handler.LastRequestBody);
        Assert.Contains("CreateWeatherForecast", handler.LastRequestBody);
        Assert.Contains("forecastId", handler.LastRequestBody);
        Assert.Contains("weather:f-1", handler.LastRequestBody);
    }

    [Fact]
    public async Task ExecuteSerializedCommandAsync_ShouldReturnError_WhenServerReturns400()
    {
        // Given
        var handler = new StubHttpMessageHandler(
            HttpStatusCode.BadRequest,
            "{\"error\":\"Unknown command type: BadCommand\"}");

        var client = CreateClient(handler);

        var request = new SerializedCommandExecuteRequest(
            CommandName: "BadCommand",
            CommandJson: "{}",
            ConsistencyTags: null,
            Options: null);

        // When
        var result = await client.ExecuteSerializedCommandAsync(request, CancellationToken.None);

        // Then
        Assert.False(result.IsSuccess);
        var ex = result.GetException();
        Assert.IsType<HttpRequestException>(ex);
        Assert.Contains("BadRequest", ex.Message);
    }

    [Fact]
    public async Task ExecuteSerializedCommandAsync_ShouldReturnError_WhenServerReturns500()
    {
        // Given
        var handler = new StubHttpMessageHandler(
            HttpStatusCode.InternalServerError,
            "Internal Server Error");

        var client = CreateClient(handler);

        var request = new SerializedCommandExecuteRequest(
            CommandName: "CreateWeatherForecast",
            CommandJson: "{}",
            ConsistencyTags: null,
            Options: null);

        // When
        var result = await client.ExecuteSerializedCommandAsync(request, CancellationToken.None);

        // Then
        Assert.False(result.IsSuccess);
        Assert.IsType<HttpRequestException>(result.GetException());
    }

    [Fact]
    public async Task ExecuteSerializedCommandAsync_WithEmptyResponse_ShouldReturnResponse()
    {
        // Given
        var handler = new StubHttpMessageHandler(
            HttpStatusCode.OK,
            JsonSerializer.Serialize(
                new SerializedCommandExecuteResponse(
                    new List<SerializedCommandEventCandidate>(),
                    new List<ConsistencyTagEntry>(),
                    null,
                    null,
                    null),
                JsonOptions));

        var client = CreateClient(handler);

        var request = new SerializedCommandExecuteRequest(
            CommandName: "UpdateWeatherForecastLocation",
            CommandJson: "{\"forecastId\":\"f-1\",\"newLocation\":\"Tokyo\"}",
            ConsistencyTags: null,
            Options: null);

        // When
        var result = await client.ExecuteSerializedCommandAsync(request, CancellationToken.None);

        // Then
        Assert.True(result.IsSuccess);
        var response = result.GetValue();
        Assert.Empty(response.EventCandidates);
        Assert.Empty(response.ConsistencyTags);
        Assert.Null(response.CommandResultJson);
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
