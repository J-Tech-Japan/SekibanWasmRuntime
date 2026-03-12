using System.Text;
using System.Text.Json;
using System.Net;
using ResultBoxes;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.WasmRuntime;
using Sekiban.Dcb.WasmRuntime.Remote;
using SekibanWasm.Cs.ClientApi;
using SekibanWasm.Cs.Domain.Weather;
using Xunit;

namespace SekibanWasm.Cs.Tests;

public class ClientApiCommandFlowTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public async Task ExecuteAndCommit_Create_ShouldCommitCreatedEvent_WhenStateIsEmpty()
    {
        var commitResult = new SerializedCommitResult(
            WrittenEvents: [],
            TagWriteResults: [],
            Duration: TimeSpan.FromMilliseconds(10));
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, JsonSerializer.Serialize(commitResult, JsonOptions));
        var stubClient = CreateClient(handler);
        var queryClient = new StubWeatherQueryClient();

        var flow = new ClientApiCommandFlow(stubClient, queryClient, new DomainSerializerOptions(JsonOptions));
        var command = new CreateWeatherForecast("f-1", "Tokyo", 22, "Warm");

        var result = await flow.ExecuteAndCommit(nameof(CreateWeatherForecast), command, CancellationToken.None);

        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.Ok<SerializedCommitResult>>(result);
        Assert.Equal("f-1", queryClient.LastForecastId);
        Assert.NotNull(handler.LastRequestBody);
        var payloadJson = ExtractFirstPayloadJson(handler.LastRequestBody);
        Assert.Contains(nameof(WeatherForecastCreated), handler.LastRequestBody);
        Assert.Contains("weather:f-1", handler.LastRequestBody);
        Assert.Contains("\"forecastId\":\"f-1\"", payloadJson);
        Assert.Contains("\"location\":\"Tokyo\"", payloadJson);
    }

    [Fact]
    public async Task ExecuteAndCommit_Create_ShouldReturnBadRequest_WhenStateAlreadyExists()
    {
        var existing = new WeatherForecastItem(
            ForecastId: "f-1",
            Location: "Tokyo",
            TemperatureC: 20,
            Summary: "Cloudy",
            CreatedAt: DateTimeOffset.UtcNow);

        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, JsonSerializer.Serialize(new SerializedCommitResult([], [], TimeSpan.Zero), JsonOptions));
        var stubClient = CreateClient(handler);
        var queryClient = new StubWeatherQueryClient { ForecastToReturn = existing };

        var flow = new ClientApiCommandFlow(stubClient, queryClient, new DomainSerializerOptions(JsonOptions));
        var result = await flow.ExecuteAndCommit(
            nameof(CreateWeatherForecast),
            new CreateWeatherForecast("f-1", "Tokyo", 22, "Warm"),
            CancellationToken.None);

        Assert.Contains("BadRequest", result.GetType().Name);
        Assert.Null(handler.LastRequestBody);
    }

    [Fact]
    public async Task ExecuteAndCommit_Update_ShouldCommitUpdatedEvent()
    {
        var existing = new WeatherForecastItem(
            ForecastId: "f-1",
            Location: "Tokyo",
            TemperatureC: 20,
            Summary: "Cloudy",
            CreatedAt: DateTimeOffset.UtcNow);

        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, JsonSerializer.Serialize(new SerializedCommitResult([], [], TimeSpan.Zero), JsonOptions));
        var stubClient = CreateClient(handler);
        var queryClient = new StubWeatherQueryClient { ForecastToReturn = existing };

        var flow = new ClientApiCommandFlow(stubClient, queryClient, new DomainSerializerOptions(JsonOptions));
        var result = await flow.ExecuteAndCommit(
            nameof(UpdateWeatherForecastLocation),
            new UpdateWeatherForecastLocation("f-1", "Osaka"),
            CancellationToken.None);

        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.Ok<SerializedCommitResult>>(result);
        Assert.NotNull(handler.LastRequestBody);
        var payloadJson = ExtractFirstPayloadJson(handler.LastRequestBody);
        Assert.Contains(nameof(WeatherForecastLocationUpdated), handler.LastRequestBody);
        Assert.Contains("\"newLocation\":\"Osaka\"", payloadJson);
        Assert.Contains("\"lastSortableUniqueId\":\"\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task ExecuteAndCommit_Delete_ShouldReturnBadRequest_WhenStateDoesNotExist()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, JsonSerializer.Serialize(new SerializedCommitResult([], [], TimeSpan.Zero), JsonOptions));
        var stubClient = CreateClient(handler);
        var queryClient = new StubWeatherQueryClient();

        var flow = new ClientApiCommandFlow(stubClient, queryClient, new DomainSerializerOptions(JsonOptions));
        var result = await flow.ExecuteAndCommit(
            nameof(DeleteWeatherForecast),
            new DeleteWeatherForecast("f-1"),
            CancellationToken.None);

        Assert.Contains("BadRequest", result.GetType().Name);
        Assert.Null(handler.LastRequestBody);
    }

    [Fact]
    public async Task ExecuteAndCommit_ShouldReturnBadRequest_WhenCommitFails()
    {
        var existing = new WeatherForecastItem(
            ForecastId: "f-1",
            Location: "Tokyo",
            TemperatureC: 20,
            Summary: "Cloudy",
            CreatedAt: DateTimeOffset.UtcNow);

        var handler = new StubHttpMessageHandler(HttpStatusCode.BadRequest, "{\"error\":\"Consistency conflict\"}");
        var stubClient = CreateClient(handler);
        var queryClient = new StubWeatherQueryClient { ForecastToReturn = existing };

        var flow = new ClientApiCommandFlow(stubClient, queryClient, new DomainSerializerOptions(JsonOptions));
        var result = await flow.ExecuteAndCommit(
            nameof(UpdateWeatherForecastLocation),
            new UpdateWeatherForecastLocation("f-1", "Osaka"),
            CancellationToken.None);

        Assert.Contains("BadRequest", result.GetType().Name);
        Assert.NotNull(handler.LastRequestBody);
    }

    private static ISerializedDcbClient CreateClient(StubHttpMessageHandler handler)
    {
        return new HttpSerializedDcbClient(
            new HttpClient(handler),
            new SerializedDcbClientOptions { BaseUrl = "https://localhost:5001" },
            JsonOptions);
    }

    private static string ExtractFirstPayloadJson(string requestBody)
    {
        using var document = JsonDocument.Parse(requestBody);
        var payloadBase64 = document.RootElement
            .GetProperty("eventCandidates")[0]
            .GetProperty("payload")
            .GetString();
        Assert.False(string.IsNullOrWhiteSpace(payloadBase64));
        return Encoding.UTF8.GetString(Convert.FromBase64String(payloadBase64!));
    }

    private sealed class StubWeatherQueryClient : IWeatherQueryClient
    {
        public WeatherForecastItem? ForecastToReturn { get; set; }
        public string? LastForecastId { get; private set; }

        public Task<WeatherForecastItem?> GetForecastAsync(string forecastId, CancellationToken ct)
        {
            LastForecastId = forecastId;
            return Task.FromResult(ForecastToReturn);
        }
    }

    private sealed class StubHttpMessageHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content is not null)
            {
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
