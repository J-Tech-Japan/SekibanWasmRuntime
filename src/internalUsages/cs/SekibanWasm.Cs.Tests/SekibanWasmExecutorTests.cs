using System.Text.Json;
using ResultBoxes;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.WasmRuntime;
using SekibanWasm.Cs.Domain.Weather;
using Xunit;

namespace SekibanWasm.Cs.Tests;

public class SekibanWasmExecutorTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public async Task ExecuteCommandAsync_ShouldUseBuilderAndCommitClient()
    {
        var commitRequest = new SerializedCommitRequest(
            EventCandidates:
            [
                new SerializableEventCandidate(
                    Payload: JsonSerializer.SerializeToUtf8Bytes(
                        new WeatherForecastCreated(
                            "f-1",
                            "Tokyo",
                            20,
                            "Cloudy",
                            DateTimeOffset.Parse("2026-03-11T23:00:00+00:00")),
                        JsonOptions),
                    EventPayloadName: nameof(WeatherForecastCreated),
                    Tags: ["weather:f-1"])
            ],
            ConsistencyTags:
            [
                new ConsistencyTagEntry("weather:f-1", string.Empty)
            ]);
        var commitResult = new SerializedCommitResult([], [], TimeSpan.FromMilliseconds(10));
        var dcbClient = new StubSerializedDcbClient
        {
            CommitResult = ResultBox<SerializedCommitResult>.FromValue(commitResult)
        };
        var queryClient = new StubSerializedQueryClient();
        var requestBuilder = new StubCommitRequestBuilder { RequestToReturn = commitRequest };
        var executor = new SekibanWasmExecutor(dcbClient, queryClient, requestBuilder, JsonOptions);

        var result = await executor.ExecuteCommandAsync(
            nameof(CreateWeatherForecast),
            new CreateWeatherForecast("f-1", "Tokyo", 20, "Cloudy"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Same(commitRequest, dcbClient.LastCommitRequest);
        Assert.Equal(nameof(CreateWeatherForecast), requestBuilder.LastCommandName);
    }

    [Fact]
    public async Task ExecuteListQueryAsync_ShouldSerializeQueryAndForwardWaitId()
    {
        var expectedItems = new List<WeatherForecastItem>
        {
            new(
                ForecastId: "f-1",
                Location: "Tokyo",
                TemperatureC: 20,
                Summary: "Cloudy",
                CreatedAt: DateTimeOffset.Parse("2026-03-11T23:00:00+00:00"))
        };
        var dcbClient = new StubSerializedDcbClient();
        var queryClient = new StubSerializedQueryClient
        {
            ListResult = ResultBox<List<WeatherForecastItem>>.FromValue(expectedItems)
        };
        var executor = new SekibanWasmExecutor(
            dcbClient,
            queryClient,
            new StubCommitRequestBuilder(),
            JsonOptions);

        var result = await executor.ExecuteListQueryAsync<List<WeatherForecastItem>>(
            nameof(GetWeatherForecastListQuery),
            new GetWeatherForecastListQuery
            {
                ForecastId = "f-1",
                IncludeDeleted = true,
                PageSize = 1
            },
            "sort-1",
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.GetValue());
        Assert.NotNull(queryClient.LastListRequest);
        Assert.Equal("sort-1", queryClient.LastListRequest.WaitForSortableUniqueId);

        using var document = JsonDocument.Parse(queryClient.LastListRequest.QueryParamsJson);
        Assert.Equal("f-1", document.RootElement.GetProperty("forecastId").GetString());
        Assert.True(document.RootElement.GetProperty("includeDeleted").GetBoolean());
    }

    private sealed class StubCommitRequestBuilder : ISekibanCommandCommitRequestBuilder
    {
        public string? LastCommandName { get; private set; }
        public object? LastCommand { get; private set; }
        public SerializedCommitRequest RequestToReturn { get; init; } =
            new([], []);

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

    private sealed class StubSerializedQueryClient : ISerializedQueryClient
    {
        public SerializedQueryRequest? LastQueryRequest { get; private set; }
        public SerializedQueryRequest? LastListRequest { get; private set; }
        public ResultBox<List<WeatherForecastItem>>? ListResult { get; init; }

        public Task<ResultBox<TResult>> ExecuteQueryAsync<TResult>(
            SerializedQueryRequest request,
            CancellationToken cancellationToken = default)
            where TResult : notnull
        {
            LastQueryRequest = request;
            return Task.FromResult(ResultBox<TResult>.FromException(new NotSupportedException()));
        }

        public Task<ResultBox<TResult>> ExecuteListQueryAsync<TResult>(
            SerializedQueryRequest request,
            CancellationToken cancellationToken = default)
            where TResult : notnull
        {
            LastListRequest = request;
            return Task.FromResult((ResultBox<TResult>)(object)(ListResult ?? ResultBox<List<WeatherForecastItem>>.FromException(new NotSupportedException())));
        }
    }

    private sealed class StubSerializedDcbClient : ISerializedDcbClient
    {
        public SerializedCommitRequest? LastCommitRequest { get; private set; }
        public ResultBox<SerializedCommitResult> CommitResult { get; init; } =
            ResultBox<SerializedCommitResult>.FromValue(
                new SerializedCommitResult([], [], TimeSpan.Zero));

        public Task<ResultBox<SerializableTagState>> GetSerializableTagStateAsync(TagStateId tagStateId) =>
            Task.FromResult(ResultBox<SerializableTagState>.FromException(new NotSupportedException()));

        public Task<ResultBox<SerializedCommitResult>> CommitSerializableEventsAsync(
            SerializedCommitRequest request,
            CancellationToken cancellationToken = default)
        {
            LastCommitRequest = request;
            return Task.FromResult(CommitResult);
        }

        public Task<ResultBox<SerializedCommandExecuteResponse>> ExecuteSerializedCommandAsync(
            SerializedCommandExecuteRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ResultBox<SerializedCommandExecuteResponse>.FromException(new NotSupportedException()));
    }
}
