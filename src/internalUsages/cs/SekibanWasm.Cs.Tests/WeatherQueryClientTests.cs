using System.Net;
using System.Text;
using System.Text.Json;
using SekibanWasm.Cs.ClientApi;
using SekibanWasm.Cs.Domain.Weather;
using Xunit;

namespace SekibanWasm.Cs.Tests;

public class WeatherQueryClientTests
{
    private static readonly JsonSerializerOptions DomainJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public async Task GetForecastAsync_ShouldSendSerializedListQuery_AndParseFirstItem()
    {
        var itemsJson = JsonSerializer.Serialize(new[]
        {
            new WeatherForecastItem(
                ForecastId: "f-1",
                Location: "Tokyo",
                TemperatureC: 20,
                Summary: "Cloudy",
                CreatedAt: DateTimeOffset.Parse("2026-03-11T23:00:00+00:00"))
        }, DomainJsonOptions);
        var responseBody = JsonSerializer.Serialize(new
        {
            itemsJson,
            totalCount = 1,
            totalPages = 1,
            currentPage = 1,
            pageSize = 1
        });

        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, responseBody);
        var client = new WeatherQueryClient(
            new StubHttpClientFactory(new HttpClient(handler)
            {
                BaseAddress = new Uri("https://localhost:5001")
            }),
            new DomainSerializerOptions(DomainJsonOptions),
            new TransportSerializerOptions(new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        var result = await client.GetForecastAsync("f-1", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("f-1", result.ForecastId);
        Assert.Equal("Tokyo", result.Location);
        Assert.NotNull(handler.LastRequestBody);

        using var document = JsonDocument.Parse(handler.LastRequestBody);
        Assert.Equal("GetWeatherForecastListQuery", document.RootElement.GetProperty("queryType").GetString());

        var queryJson = document.RootElement.GetProperty("queryParamsJson").GetString();
        Assert.False(string.IsNullOrWhiteSpace(queryJson));
        using var queryDocument = JsonDocument.Parse(queryJson!);
        Assert.Equal("f-1", queryDocument.RootElement.GetProperty("forecastId").GetString());
        Assert.True(queryDocument.RootElement.GetProperty("includeDeleted").GetBoolean());
        Assert.Equal(1, queryDocument.RootElement.GetProperty("pageSize").GetInt32());
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHttpMessageHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
    {
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
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
