using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using ResultBoxes;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.WasmRuntime;
using SekibanWasm.Cs.ClientApi;
using Xunit;

namespace SekibanWasm.Cs.Tests;

public class ClientApiCommandFlowTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public async Task ExecuteAndCommit_ShouldReturnOk_WhenExecuteAndCommitBothSucceed()
    {
        // Given
        var payloadBase64 = Convert.ToBase64String(
            Encoding.UTF8.GetBytes("{\"forecastId\":\"f-1\"}"));

        var executeResponse = new SerializedCommandExecuteResponse(
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

        var commitResult = new SerializedCommitResult(
            WrittenEvents: new List<SerializableEvent>(),
            TagWriteResults: new List<TagWriteResult>(),
            Duration: TimeSpan.FromMilliseconds(10));

        var stubClient = new StubSerializedDcbClient
        {
            ExecuteResponseToReturn = ResultBox.FromValue(executeResponse),
            CommitResultToReturn = ResultBox.FromValue(commitResult)
        };

        var flow = new ClientApiCommandFlow(stubClient, JsonOptions);
        var command = new { forecastId = "f-1", location = "Tokyo", temperatureC = 22, summary = "Warm" };

        // When
        var result = await flow.ExecuteAndCommit("CreateWeatherForecast", command, CancellationToken.None);

        // Then
        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.Ok<SerializedCommitResult>>(result);

        Assert.NotNull(stubClient.LastExecuteRequest);
        Assert.Equal("CreateWeatherForecast", stubClient.LastExecuteRequest.CommandName);
        Assert.Contains("forecastId", stubClient.LastExecuteRequest.CommandJson);

        Assert.NotNull(stubClient.LastCommitRequest);
        Assert.Single(stubClient.LastCommitRequest.EventCandidates);
        var candidate = stubClient.LastCommitRequest.EventCandidates[0];
        Assert.Equal("WeatherForecastCreated", candidate.EventPayloadName);
        var decodedPayload = Encoding.UTF8.GetString(candidate.Payload);
        Assert.Equal("{\"forecastId\":\"f-1\"}", decodedPayload);
    }

    [Fact]
    public async Task ExecuteAndCommit_ShouldReturnBadRequest_WhenExecuteFails()
    {
        // Given
        var stubClient = new StubSerializedDcbClient
        {
            ExecuteResponseToReturn = ResultBox<SerializedCommandExecuteResponse>.FromException(
                new InvalidOperationException("Command execution failed"))
        };

        var flow = new ClientApiCommandFlow(stubClient, JsonOptions);
        var command = new { forecastId = "f-1" };

        // When
        var result = await flow.ExecuteAndCommit("CreateWeatherForecast", command, CancellationToken.None);

        // Then
        Assert.Contains("BadRequest", result.GetType().Name);
        Assert.Null(stubClient.LastCommitRequest);
    }

    [Fact]
    public async Task ExecuteAndCommit_ShouldReturnBadRequest_WhenCommitFails()
    {
        // Given
        var payloadBase64 = Convert.ToBase64String(
            Encoding.UTF8.GetBytes("{\"forecastId\":\"f-1\"}"));

        var executeResponse = new SerializedCommandExecuteResponse(
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

        var stubClient = new StubSerializedDcbClient
        {
            ExecuteResponseToReturn = ResultBox.FromValue(executeResponse),
            CommitResultToReturn = ResultBox<SerializedCommitResult>.FromException(
                new InvalidOperationException("Consistency conflict"))
        };

        var flow = new ClientApiCommandFlow(stubClient, JsonOptions);
        var command = new { forecastId = "f-1" };

        // When
        var result = await flow.ExecuteAndCommit("CreateWeatherForecast", command, CancellationToken.None);

        // Then
        Assert.Contains("BadRequest", result.GetType().Name);
        Assert.NotNull(stubClient.LastCommitRequest);
    }

    [Fact]
    public async Task ExecuteAndCommit_ShouldConvertBase64PayloadToBytes()
    {
        // Given
        var originalPayload = "{\"forecastId\":\"f-1\",\"location\":\"Tokyo\"}";
        var payloadBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(originalPayload));

        var executeResponse = new SerializedCommandExecuteResponse(
            EventCandidates: new List<SerializedCommandEventCandidate>
            {
                new(
                    EventPayloadName: "WeatherForecastCreated",
                    PayloadBase64: payloadBase64,
                    Tags: new List<string> { "weather:f-1", "forecast:f-1" })
            },
            ConsistencyTags: new List<ConsistencyTagEntry>
            {
                new(Tag: "weather:f-1", LastSortableUniqueId: "uid-001")
            },
            CommandResultJson: null);

        var commitResult = new SerializedCommitResult(
            WrittenEvents: new List<SerializableEvent>(),
            TagWriteResults: new List<TagWriteResult>(),
            Duration: TimeSpan.FromMilliseconds(5));

        var stubClient = new StubSerializedDcbClient
        {
            ExecuteResponseToReturn = ResultBox.FromValue(executeResponse),
            CommitResultToReturn = ResultBox.FromValue(commitResult)
        };

        var flow = new ClientApiCommandFlow(stubClient, JsonOptions);
        var command = new { forecastId = "f-1", location = "Tokyo" };

        // When
        await flow.ExecuteAndCommit("CreateWeatherForecast", command, CancellationToken.None);

        // Then
        Assert.NotNull(stubClient.LastCommitRequest);
        Assert.Single(stubClient.LastCommitRequest.EventCandidates);
        var candidate = stubClient.LastCommitRequest.EventCandidates[0];
        var decodedPayload = Encoding.UTF8.GetString(candidate.Payload);
        Assert.Equal(originalPayload, decodedPayload);
        Assert.Equal("WeatherForecastCreated", candidate.EventPayloadName);
        Assert.Equal(2, candidate.Tags.Count);
        Assert.Equal("weather:f-1", candidate.Tags[0]);
        Assert.Equal("forecast:f-1", candidate.Tags[1]);
    }

    private sealed class StubSerializedDcbClient : ISerializedDcbClient
    {
        public ResultBox<SerializedCommandExecuteResponse>? ExecuteResponseToReturn { get; set; }
        public ResultBox<SerializedCommitResult>? CommitResultToReturn { get; set; }

        public SerializedCommandExecuteRequest? LastExecuteRequest { get; private set; }
        public SerializedCommitRequest? LastCommitRequest { get; private set; }

        public Task<ResultBox<SerializableTagState>> GetSerializableTagStateAsync(TagStateId tagStateId)
        {
            throw new NotSupportedException("Not used in these tests");
        }

        public Task<ResultBox<SerializedCommitResult>> CommitSerializableEventsAsync(
            SerializedCommitRequest request,
            CancellationToken cancellationToken = default)
        {
            LastCommitRequest = request;
            if (CommitResultToReturn is null)
            {
                throw new InvalidOperationException("CommitResultToReturn not configured");
            }
            return Task.FromResult(CommitResultToReturn);
        }

        public Task<ResultBox<SerializedCommandExecuteResponse>> ExecuteSerializedCommandAsync(
            SerializedCommandExecuteRequest request,
            CancellationToken cancellationToken = default)
        {
            LastExecuteRequest = request;
            if (ExecuteResponseToReturn is null)
            {
                throw new InvalidOperationException("ExecuteResponseToReturn not configured");
            }
            return Task.FromResult(ExecuteResponseToReturn);
        }
    }
}
