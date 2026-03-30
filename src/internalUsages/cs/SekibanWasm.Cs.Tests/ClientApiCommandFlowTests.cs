using System.Text.Json;
using ResultBoxes;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.WasmRuntime;
using SekibanWasm.Cs.ClientApi;
using SekibanWasm.Cs.Domain.Weather;
using Xunit;

namespace SekibanWasm.Cs.Tests;

public class ClientApiCommandFlowTests
{
    private static readonly JsonSerializerOptions DomainJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public async Task BuildCommitRequestAsync_Create_ShouldBuildCreatedEvent()
    {
        var flow = CreateFlow(tagExists: false, existingForecast: null);

        var result = await flow.BuildCommitRequestAsync(
            nameof(CreateWeatherForecast),
            new CreateWeatherForecast("f-1", "Tokyo", 20, "Cloudy"),
            CancellationToken.None);

        var candidate = Assert.Single(result.EventCandidates);
        Assert.Equal(nameof(WeatherForecastCreated), candidate.EventPayloadName);
        Assert.Equal(["weather:f-1"], candidate.Tags);
        Assert.Equal("weather:f-1", Assert.Single(result.ConsistencyTags).Tag);

        using var payloadDocument = JsonDocument.Parse(candidate.Payload);
        Assert.Equal("f-1", payloadDocument.RootElement.GetProperty("forecastId").GetString());
        Assert.Equal("Tokyo", payloadDocument.RootElement.GetProperty("location").GetString());
    }

    [Fact]
    public async Task BuildCommitRequestAsync_Create_ShouldThrow_WhenForecastAlreadyExists()
    {
        var flow = CreateFlow(tagExists: true, existingForecast: CreateForecast("f-1", "Tokyo"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            flow.BuildCommitRequestAsync(
                nameof(CreateWeatherForecast),
                new CreateWeatherForecast("f-1", "Tokyo", 20, "Cloudy"),
                CancellationToken.None));

        Assert.Contains("already exists", ex.Message);
    }

    [Fact]
    public async Task BuildCommitRequestAsync_Update_ShouldBuildUpdatedEvent()
    {
        var flow = CreateFlow(tagExists: true, existingForecast: CreateForecast("f-1", "Tokyo"));

        var result = await flow.BuildCommitRequestAsync(
            nameof(UpdateWeatherForecastLocation),
            new UpdateWeatherForecastLocation("f-1", "Osaka"),
            CancellationToken.None);

        var candidate = Assert.Single(result.EventCandidates);
        Assert.Equal(nameof(WeatherForecastLocationUpdated), candidate.EventPayloadName);

        using var payloadDocument = JsonDocument.Parse(candidate.Payload);
        Assert.Equal("Osaka", payloadDocument.RootElement.GetProperty("newLocation").GetString());
    }

    [Fact]
    public async Task BuildCommitRequestAsync_Update_ShouldReturnEmptyCommit_WhenLocationIsUnchanged()
    {
        var flow = CreateFlow(tagExists: true, existingForecast: CreateForecast("f-1", "Tokyo"));

        var result = await flow.BuildCommitRequestAsync(
            nameof(UpdateWeatherForecastLocation),
            new UpdateWeatherForecastLocation("f-1", "Tokyo"),
            CancellationToken.None);

        Assert.Empty(result.EventCandidates);
        Assert.Empty(result.ConsistencyTags);
    }

    [Fact]
    public async Task BuildCommitRequestAsync_Update_ShouldThrow_WhenForecastIsDeleted()
    {
        var flow = CreateFlow(tagExists: true, existingForecast: CreateForecast("f-1", "Tokyo", isDeleted: true));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            flow.BuildCommitRequestAsync(
                nameof(UpdateWeatherForecastLocation),
                new UpdateWeatherForecastLocation("f-1", "Osaka"),
                CancellationToken.None));

        Assert.Contains("has been deleted", ex.Message);
    }

    [Fact]
    public async Task BuildCommitRequestAsync_Delete_ShouldBuildDeletedEvent()
    {
        var flow = CreateFlow(tagExists: true, existingForecast: CreateForecast("f-1", "Tokyo"));

        var result = await flow.BuildCommitRequestAsync(
            nameof(DeleteWeatherForecast),
            new DeleteWeatherForecast("f-1"),
            CancellationToken.None);

        var candidate = Assert.Single(result.EventCandidates);
        Assert.Equal(nameof(WeatherForecastDeleted), candidate.EventPayloadName);
    }

    [Fact]
    public async Task BuildCommitRequestAsync_Delete_ShouldThrow_WhenForecastDoesNotExist()
    {
        var flow = CreateFlow(tagExists: false, existingForecast: null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            flow.BuildCommitRequestAsync(
                nameof(DeleteWeatherForecast),
                new DeleteWeatherForecast("f-1"),
                CancellationToken.None));

        Assert.Contains("does not exist", ex.Message);
    }

    private static ClientApiCommandFlow CreateFlow(bool tagExists, WeatherForecastItem? existingForecast)
    {
        var client = new StubSerializedDcbClient();
        var tagExistenceChecker = new StubTagExistenceChecker { Exists = tagExists };
        var queryClient = new StubWeatherQueryClient { ForecastToReturn = existingForecast };
        return new ClientApiCommandFlow(
            client,
            tagExistenceChecker,
            queryClient,
            new DomainSerializerOptions(DomainJsonOptions));
    }

    private static WeatherForecastItem CreateForecast(
        string forecastId,
        string location,
        bool isDeleted = false) =>
        new(
            ForecastId: forecastId,
            Location: location,
            TemperatureC: 20,
            Summary: "Cloudy",
            CreatedAt: DateTimeOffset.Parse("2026-03-11T23:00:00+00:00"),
            IsDeleted: isDeleted,
            DeletedAt: isDeleted ? DateTimeOffset.Parse("2026-03-12T00:00:00+00:00") : null);

    private sealed class StubWeatherQueryClient : IWeatherQueryClient
    {
        public WeatherForecastItem? ForecastToReturn { get; init; }

        public Task<WeatherForecastItem?> GetForecastAsync(string forecastId, CancellationToken ct) =>
            Task.FromResult(ForecastToReturn);
    }

    private sealed class StubTagExistenceChecker : ITagExistenceChecker
    {
        public bool Exists { get; init; }

        public Task<bool> ExistsAsync(ITag tag, CancellationToken ct) => Task.FromResult(Exists);
    }

    private sealed class StubSerializedDcbClient : ISerializedDcbClient
    {
        public Task<ResultBox<SerializableTagState>> GetSerializableTagStateAsync(TagStateId tagStateId) =>
            Task.FromResult(ResultBox<SerializableTagState>.FromException(new NotSupportedException()));

        public Task<ResultBox<SerializedCommitResult>> CommitSerializableEventsAsync(
            SerializedCommitRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ResultBox<SerializedCommitResult>.FromValue(
                new SerializedCommitResult([], [], TimeSpan.Zero)));

        public Task<ResultBox<SerializedCommandExecuteResponse>> ExecuteSerializedCommandAsync(
            SerializedCommandExecuteRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ResultBox<SerializedCommandExecuteResponse>.FromException(new NotSupportedException()));
    }
}
