using System.Net;
using System.Text;
using System.Text.Json;
using Sekiban.Dcb.WasmRuntime;
using Sekiban.Dcb.WasmRuntime.Remote;
using SekibanWasm.Cs.Domain.Weather;
using Xunit;

namespace SekibanWasm.Cs.Tests;

public class HttpSerializedQueryClientTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public async Task ExecuteQueryAsync_ShouldReturnDeserializedPayload_WhenServerReturns200()
    {
        var responseBody = JsonSerializer.Serialize(
            new SerializedQueryResponse(
                JsonSerializer.Serialize(
                    new WeatherForecastItem(
                        ForecastId: "f-1",
                        Location: "Tokyo",
                        TemperatureC: 20,
                        Summary: "Cloudy",
                        CreatedAt: DateTimeOffset.Parse("2026-03-11T23:00:00+00:00")),
                    JsonOptions)),
            JsonOptions);
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, responseBody);
        var client = CreateClient(handler);

        var result = await client.ExecuteQueryAsync<WeatherForecastItem>(
            new SerializedQueryRequest("GetWeatherForecastQuery", "{\"forecastId\":\"f-1\"}"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Tokyo", result.GetValue().Location);
        Assert.NotNull(handler.LastRequest);
        Assert.Equal(
            "https://localhost:5001/api/sekiban/serialized/query",
            handler.LastRequest.RequestUri?.ToString());
    }

    [Fact]
    public async Task ExecuteListQueryAsync_ShouldReturnDeserializedItems_WhenServerReturns200()
    {
        var itemsJson = JsonSerializer.Serialize(
            new[]
            {
                new WeatherForecastItem(
                    ForecastId: "f-1",
                    Location: "Tokyo",
                    TemperatureC: 20,
                    Summary: "Cloudy",
                    CreatedAt: DateTimeOffset.Parse("2026-03-11T23:00:00+00:00"))
            },
            JsonOptions);
        var responseBody = JsonSerializer.Serialize(
            new SerializedListQueryResponse(
                ItemsJson: itemsJson,
                TotalCount: 1,
                TotalPages: 1,
                CurrentPage: 1,
                PageSize: 20),
            JsonOptions);
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, responseBody);
        var client = CreateClient(handler);

        var result = await client.ExecuteListQueryAsync<List<WeatherForecastItem>>(
            new SerializedQueryRequest("GetWeatherForecastListQuery", "{\"pageSize\":20}"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.GetValue());
        Assert.NotNull(handler.LastRequest);
        Assert.Equal(
            "https://localhost:5001/api/sekiban/serialized/list-query",
            handler.LastRequest.RequestUri?.ToString());
    }

    [Fact]
    public async Task ExecuteListQueryAsync_ShouldReturnError_WhenServerReturns400()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.BadRequest, "{\"error\":\"invalid query\"}");
        var client = CreateClient(handler);

        var result = await client.ExecuteListQueryAsync<List<WeatherForecastItem>>(
            new SerializedQueryRequest("GetWeatherForecastListQuery", "{}"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.IsType<HttpRequestException>(result.GetException());
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void Constructor_ShouldThrow_WhenBaseUrlIsEmpty(string? baseUrl)
    {
        var options = new SerializedDcbClientOptions { BaseUrl = baseUrl! };
        var httpClient = new HttpClient();

        var ex = Assert.Throws<ArgumentException>(
            () => new HttpSerializedQueryClient(httpClient, options, JsonOptions, JsonOptions));
        Assert.Contains("BaseUrl", ex.Message);
    }

    private static HttpSerializedQueryClient CreateClient(StubHttpMessageHandler handler)
    {
        var options = new SerializedDcbClientOptions { BaseUrl = "https://localhost:5001" };
        return new HttpSerializedQueryClient(new HttpClient(handler), options, JsonOptions, JsonOptions);
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
