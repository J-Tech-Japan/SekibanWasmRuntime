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
        new(HttpStatusCode.OK)
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
