using System.Text.Json;
using ResultBoxes;
using Sekiban.Dcb.WasmRuntime;
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
        var expectedItems = new List<WeatherForecastItem>
        {
            new WeatherForecastItem(
                ForecastId: "f-1",
                Location: "Tokyo",
                TemperatureC: 20,
                Summary: "Cloudy",
                CreatedAt: DateTimeOffset.Parse("2026-03-11T23:00:00+00:00"))
        };
        var queryClient = new StubSerializedQueryClient
        {
            ResultToReturn = expectedItems
        };
        var client = new WeatherQueryClient(
            queryClient,
            new DomainSerializerOptions(DomainJsonOptions));

        var result = await client.GetForecastAsync("f-1", "uid-001", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("f-1", result.ForecastId);
        Assert.Equal("Tokyo", result.Location);
        Assert.NotNull(queryClient.LastRequest);
        Assert.Equal("GetWeatherForecastListQuery", queryClient.LastRequest.QueryType);

        var queryJson = queryClient.LastRequest.QueryParamsJson;
        Assert.False(string.IsNullOrWhiteSpace(queryJson));
        using var queryDocument = JsonDocument.Parse(queryJson!);
        Assert.Equal("f-1", queryDocument.RootElement.GetProperty("forecastId").GetString());
        Assert.True(queryDocument.RootElement.GetProperty("includeDeleted").GetBoolean());
        Assert.Equal(1, queryDocument.RootElement.GetProperty("pageSize").GetInt32());
        Assert.Equal("uid-001", queryDocument.RootElement.GetProperty("waitForSortableUniqueId").GetString());
    }

    private sealed class StubSerializedQueryClient : ISerializedQueryClient
    {
        public SerializedQueryRequest? LastRequest { get; private set; }
        public List<WeatherForecastItem> ResultToReturn { get; init; } = [];

        public Task<ResultBox<TResult>> ExecuteQueryAsync<TResult>(
            SerializedQueryRequest request,
            CancellationToken cancellationToken = default)
            where TResult : notnull
        {
            throw new NotSupportedException();
        }

        public Task<ResultBox<TResult>> ExecuteListQueryAsync<TResult>(
            SerializedQueryRequest request,
            CancellationToken cancellationToken = default)
            where TResult : notnull
        {
            LastRequest = request;
            return Task.FromResult(ResultBox<TResult>.FromValue((TResult)(object)ResultToReturn));
        }
    }
}
