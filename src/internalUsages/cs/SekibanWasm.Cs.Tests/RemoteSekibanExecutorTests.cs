using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Sekiban.Dcb;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.WasmRuntime;
using Sekiban.Dcb.WasmRuntime.Remote;
using SekibanWasm.Cs.Domain;
using SekibanWasm.Cs.Domain.Weather;
using Xunit;

namespace SekibanWasm.Cs.Tests;

public class RemoteSekibanExecutorTests
{
    private static readonly JsonSerializerOptions TransportJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly DcbDomainTypes DomainTypes = DomainType.GetDomainTypes();

    [Fact]
    public async Task GetTagStateAsync_ShouldDeserializeSerializableState()
    {
        byte[] payload = DomainTypes.TagStatePayloadTypes.SerializePayload(
            new WeatherForecastState("f-1", "Tokyo", 20, "Cloudy", DateTimeOffset.Parse("2026-03-28T08:00:00+00:00")))
            .GetValue();
        var serializable = new SerializableTagState(
            payload,
            5,
            "063910000000000000000000000001",
            "weather",
            "f-1",
            WeatherForecastProjector.ProjectorName,
            nameof(WeatherForecastState),
            WeatherForecastProjector.ProjectorVersion);
        var handler = new RoutingHttpMessageHandler(request =>
            Task.FromResult(CreateJsonResponse(serializable)));
        var executor = CreateExecutor(handler);

        TagState state = await executor.GetTagStateAsync(TagStateId.Parse("weather:f-1:WeatherForecastProjector"));

        var payloadState = Assert.IsType<WeatherForecastState>(state.Payload);
        Assert.Equal("f-1", payloadState.ForecastId);
        Assert.Equal("Tokyo", payloadState.Location);
        Assert.Equal(5, state.Version);
        Assert.Equal("063910000000000000000000000001", state.LastSortedUniqueId);
    }

    [Fact]
    public async Task QueryAsync_ListQuery_ShouldRoundTripThroughSerializedEndpoint()
    {
        var items = new List<WeatherForecastItem>
        {
            new("f-1", "Tokyo", 20, "Cloudy", DateTimeOffset.Parse("2026-03-28T08:00:00+00:00"))
        };
        var response = new SerializedListQueryResponse(
            JsonSerializer.Serialize(items, DomainTypes.JsonSerializerOptions),
            1,
            1,
            1,
            10);
        var handler = new RoutingHttpMessageHandler(async request =>
        {
            string body = await ReadBodyAsync(request);
            Assert.Contains(nameof(GetWeatherForecastListQuery), body);
            return CreateJsonResponse(response);
        });
        var executor = CreateExecutor(handler);

        ListQueryResult<WeatherForecastItem> result = await executor.QueryAsync(
            new GetWeatherForecastListQuery
            {
                ForecastId = "f-1",
                PageNumber = 1,
                PageSize = 10,
                WaitForSortableUniqueId = "063910000000000000000000000010"
            });

        var item = Assert.Single(result.Items);
        Assert.Equal("f-1", item.ForecastId);
        Assert.Equal(1, result.TotalCount);
        string queryBody = handler.GetSingleRequestBody("/api/sekiban/serialized/list-query");
        Assert.Contains("063910000000000000000000000010", queryBody);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldUseRemoteTagStateAndCommit_AndRepublishWrittenEvents()
    {
        var tagState = new SerializableTagState(
            Array.Empty<byte>(),
            0,
            string.Empty,
            "weather",
            "f-1",
            WeatherForecastProjector.ProjectorName,
            nameof(EmptyTagStatePayload),
            WeatherForecastProjector.ProjectorVersion);
        var writtenEvent = new Event(
            new WeatherForecastCreated(
                "f-1",
                "Tokyo",
                20,
                "Cloudy",
                DateTimeOffset.Parse("2026-03-28T08:00:00+00:00")),
            "063910000000000000000000000111",
            nameof(WeatherForecastCreated),
            Guid.NewGuid(),
            new EventMetadata(string.Empty, string.Empty, string.Empty),
            [new WeatherForecastTag("f-1").GetTag()])
            .ToSerializableEvent(DomainTypes.EventTypes);
        var commitResult = new SerializedCommitResult(
            [writtenEvent],
            [],
            TimeSpan.FromMilliseconds(12));
        var handler = new RoutingHttpMessageHandler(async request =>
        {
            return request.RequestUri?.AbsolutePath switch
            {
                "/api/sekiban/serialized/tag-state" => CreateJsonResponse(tagState),
                "/api/sekiban/serialized/commit" => CreateJsonResponse(commitResult),
                _ => throw new InvalidOperationException($"Unexpected path {request.RequestUri?.AbsolutePath}")
            };
        });
        var publisher = new RecordingEventPublisher();
        var executor = CreateExecutor(handler, publisher);

        ExecutionResult result = await executor.ExecuteAsync(
            new CreateWeatherForecast("f-1", "Tokyo", 20, "Cloudy"));

        Assert.Single(result.Events);
        Assert.Equal(nameof(WeatherForecastCreated), result.Events.Single().EventType);
        Assert.Equal("063910000000000000000000000111", result.SortableUniqueId);
        var published = Assert.Single(publisher.Published);
        Assert.Equal(nameof(WeatherForecastCreated), published.Event.EventType);

        string commitBody = handler.GetSingleRequestBody("/api/sekiban/serialized/commit");
        Assert.Contains(nameof(WeatherForecastCreated), commitBody);
        Assert.Contains("weather:f-1", commitBody);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRetryTransientCommitReservationConflicts()
    {
        var tagState = new SerializableTagState(
            Array.Empty<byte>(),
            0,
            string.Empty,
            "weather",
            "f-1",
            WeatherForecastProjector.ProjectorName,
            nameof(EmptyTagStatePayload),
            WeatherForecastProjector.ProjectorVersion);
        var writtenEvent = new Event(
            new WeatherForecastCreated(
                "f-1",
                "Tokyo",
                20,
                "Cloudy",
                DateTimeOffset.Parse("2026-03-28T08:00:00+00:00")),
            "063910000000000000000000000111",
            nameof(WeatherForecastCreated),
            Guid.NewGuid(),
            new EventMetadata(string.Empty, string.Empty, string.Empty),
            [new WeatherForecastTag("f-1").GetTag()])
            .ToSerializableEvent(DomainTypes.EventTypes);
        var commitResult = new SerializedCommitResult(
            [writtenEvent],
            [],
            TimeSpan.FromMilliseconds(12));
        int commitAttempts = 0;
        var handler = new RoutingHttpMessageHandler(async request =>
        {
            return request.RequestUri?.AbsolutePath switch
            {
                "/api/sekiban/serialized/tag-state" => CreateJsonResponse(tagState),
                "/api/sekiban/serialized/commit" when Interlocked.Increment(ref commitAttempts) == 1 =>
                    CreateJsonResponse(
                        HttpStatusCode.BadRequest,
                        new
                        {
                            error =
                                "Failed to reserve tags: Tag weather:f-1: Tag weather:f-1 is currently reserved"
                        }),
                "/api/sekiban/serialized/commit" => CreateJsonResponse(commitResult),
                _ => throw new InvalidOperationException($"Unexpected path {request.RequestUri?.AbsolutePath}")
            };
        });
        var publisher = new RecordingEventPublisher();
        var executor = CreateExecutor(handler, publisher);

        ExecutionResult result = await executor.ExecuteAsync(
            new CreateWeatherForecast("f-1", "Tokyo", 20, "Cloudy"));

        Assert.Equal(2, handler.GetRequestCount("/api/sekiban/serialized/commit"));
        Assert.Single(result.Events);
        Assert.True(result.Metadata.TryGetValue("CommitAttempts", out object? commitAttemptsMetadata));
        Assert.Equal(2, Assert.IsType<int>(commitAttemptsMetadata));
        Assert.Single(publisher.Published);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldNotRetryNonTransientCommitFailures()
    {
        var tagState = new SerializableTagState(
            Array.Empty<byte>(),
            0,
            string.Empty,
            "weather",
            "f-1",
            WeatherForecastProjector.ProjectorName,
            nameof(EmptyTagStatePayload),
            WeatherForecastProjector.ProjectorVersion);
        var handler = new RoutingHttpMessageHandler(async request =>
        {
            return request.RequestUri?.AbsolutePath switch
            {
                "/api/sekiban/serialized/tag-state" => CreateJsonResponse(tagState),
                "/api/sekiban/serialized/commit" => CreateJsonResponse(
                    HttpStatusCode.BadRequest,
                    new { error = "Validation failed" }),
                _ => throw new InvalidOperationException($"Unexpected path {request.RequestUri?.AbsolutePath}")
            };
        });
        var executor = CreateExecutor(handler);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            executor.ExecuteAsync(new CreateWeatherForecast("f-1", "Tokyo", 20, "Cloudy")));

        Assert.Contains("Validation failed", ex.Message);
        Assert.Equal(1, handler.GetRequestCount("/api/sekiban/serialized/commit"));
    }

    [Fact]
    public async Task RemoteCommandContext_ShouldUseTagLatestSortableEndpoint()
    {
        var handler = new RoutingHttpMessageHandler(request =>
        {
            object response = request.RequestUri?.AbsolutePath switch
            {
                "/api/sekiban/serialized/tag-latest-sortable" => new
                {
                    exists = true,
                    lastSortableUniqueId = "063910000000000000000000000222"
                },
                _ => throw new InvalidOperationException($"Unexpected path {request.RequestUri?.AbsolutePath}")
            };
            return Task.FromResult(CreateJsonResponse(response));
        });
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://localhost:5001")
        };
        var context = new RemoteCommandContext(
            httpClient,
            DomainTypes,
            NullLogger<RemoteCommandContext>.Instance);

        bool exists = await context.TagExistsAsync(new WeatherForecastTag("f-1"));
        string latestSortable = await context.GetTagLatestSortableUniqueIdAsync(new WeatherForecastTag("f-1"));

        Assert.True(exists);
        Assert.Equal("063910000000000000000000000222", latestSortable);
        Assert.Equal(1, handler.GetRequestCount("/api/sekiban/serialized/tag-latest-sortable"));
    }

    [Fact]
    public async Task RemoteCommandContext_ShouldCacheTagStateByTagStateId()
    {
        byte[] payload = DomainTypes.TagStatePayloadTypes.SerializePayload(
            new WeatherForecastState("f-1", "Tokyo", 20, "Cloudy", DateTimeOffset.Parse("2026-03-28T08:00:00+00:00")))
            .GetValue();
        var serializable = new SerializableTagState(
            payload,
            5,
            "063910000000000000000000000001",
            "weather",
            "f-1",
            WeatherForecastProjector.ProjectorName,
            nameof(WeatherForecastState),
            WeatherForecastProjector.ProjectorVersion);
        var handler = new RoutingHttpMessageHandler(request =>
            Task.FromResult(CreateJsonResponse(serializable)));
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://localhost:5001")
        };
        var context = new RemoteCommandContext(
            httpClient,
            DomainTypes,
            NullLogger<RemoteCommandContext>.Instance);

        TagState first = await context.GetStateAsync<WeatherForecastProjector>(new WeatherForecastTag("f-1"));
        TagState second = await context.GetStateAsync<WeatherForecastProjector>(new WeatherForecastTag("f-1"));

        Assert.IsType<WeatherForecastState>(first.Payload);
        Assert.IsType<WeatherForecastState>(second.Payload);
        Assert.Equal(1, handler.GetRequestCount("/api/sekiban/serialized/tag-state"));
    }

    /// <summary>
    ///     Pins the serialized commit envelope the executor puts on the wire against the Sekiban.Dcb 10.7.0 (SEK-G17)
    ///     contract: the V1 envelope is the legacy official shape plus an explicit <c>version</c> discriminator. The
    ///     negative half of this test is the point — J-Tech-Japan/SekibanWasmRuntime#248 assumed a rename to
    ///     events / payloadJson / eventType, and reflection over the shipped 10.7.0 assembly showed no such shape
    ///     exists. If a future bump really does rename these, this test fails loudly instead of silently drifting.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_ShouldEmitVersion1SerializedCommitEnvelope()
    {
        var tagState = new SerializableTagState(
            Array.Empty<byte>(),
            0,
            string.Empty,
            "weather",
            "f-1",
            WeatherForecastProjector.ProjectorName,
            nameof(EmptyTagStatePayload),
            WeatherForecastProjector.ProjectorVersion);
        var commitResult = new SerializedCommitResult([], [], TimeSpan.Zero);
        var handler = new RoutingHttpMessageHandler(request => Task.FromResult(
            request.RequestUri?.AbsolutePath switch
            {
                "/api/sekiban/serialized/tag-state" => CreateJsonResponse(tagState),
                "/api/sekiban/serialized/commit" => CreateJsonResponse(commitResult),
                _ => throw new InvalidOperationException($"Unexpected path {request.RequestUri?.AbsolutePath}")
            }));
        var executor = CreateExecutor(handler);

        await executor.ExecuteAsync(new CreateWeatherForecast("f-1", "Tokyo", 20, "Cloudy"));

        using JsonDocument commit = JsonDocument.Parse(
            handler.GetSingleRequestBody("/api/sekiban/serialized/commit"));
        JsonElement root = commit.RootElement;

        Assert.Equal(VersionedSerializedCommitRequest.CurrentVersion, root.GetProperty("version").GetInt32());
        JsonElement candidate = Assert.Single(root.GetProperty("eventCandidates").EnumerateArray().ToList());

        // payload stays a base64-encoded byte[], NOT a raw JSON string under payloadJson.
        byte[] payload = Convert.FromBase64String(candidate.GetProperty("payload").GetString()!);
        using JsonDocument payloadJson = JsonDocument.Parse(payload);
        Assert.Equal("f-1", payloadJson.RootElement.GetProperty("forecastId").GetString());

        Assert.Equal(nameof(WeatherForecastCreated), candidate.GetProperty("eventPayloadName").GetString());

        // tags stay per-candidate; they are NOT collapsed into a single per-commit list.
        Assert.Equal(
            ["weather:f-1"],
            candidate.GetProperty("tags").EnumerateArray().Select(static t => t.GetString()!).ToArray());
        Assert.True(root.TryGetProperty("consistencyTags", out _));

        foreach (string offContractProperty in new[] { "events", "payloadJson", "eventType", "tags" })
        {
            Assert.False(
                root.TryGetProperty(offContractProperty, out _),
                $"Commit envelope must not carry off-contract property '{offContractProperty}'.");
        }
    }

    private static RemoteSekibanExecutor CreateExecutor(
        RoutingHttpMessageHandler handler,
        IEventPublisher? publisher = null)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://localhost:5001")
        };

        return new RemoteSekibanExecutor(
            httpClient,
            DomainTypes,
            publisher ?? new RecordingEventPublisher(),
            NullLogger<RemoteSekibanExecutor>.Instance,
            NullLoggerFactory.Instance);
    }

    private static HttpResponseMessage CreateJsonResponse(object body) =>
        CreateJsonResponse(HttpStatusCode.OK, body);

    private static HttpResponseMessage CreateJsonResponse(HttpStatusCode statusCode, object body) =>
        new(statusCode)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body, TransportJsonOptions),
                Encoding.UTF8,
                "application/json")
        };

    private static async Task<string> ReadBodyAsync(HttpRequestMessage request) =>
        request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync();

    private sealed class RoutingHttpMessageHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _responder = responder;
        private readonly Dictionary<string, List<string>> _requestBodiesByPath = new(StringComparer.Ordinal);

        public string GetSingleRequestBody(string path) => Assert.Single(_requestBodiesByPath[path]);

        public int GetRequestCount(string path) =>
            _requestBodiesByPath.TryGetValue(path, out List<string>? bodies) ? bodies.Count : 0;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string path = request.RequestUri?.AbsolutePath ?? string.Empty;
            string body = await ReadBodyAsync(request);
            if (!_requestBodiesByPath.TryGetValue(path, out List<string>? bodies))
            {
                bodies = [];
                _requestBodiesByPath[path] = bodies;
            }

            bodies.Add(body);
            return await _responder(request);
        }
    }

    private sealed class RecordingEventPublisher : IEventPublisher
    {
        public List<(Event Event, IReadOnlyCollection<ITag> Tags)> Published { get; } = [];

        public Task PublishAsync(
            IReadOnlyCollection<(Event Event, IReadOnlyCollection<ITag> Tags)> events,
            CancellationToken cancellationToken = default)
        {
            Published.AddRange(events);
            return Task.CompletedTask;
        }
    }
}
