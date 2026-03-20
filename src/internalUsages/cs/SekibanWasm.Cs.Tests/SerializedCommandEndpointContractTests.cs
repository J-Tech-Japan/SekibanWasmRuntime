using System.Text;
using System.Text.Json;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.WasmRuntime;
using Xunit;

namespace SekibanWasm.Cs.Tests;

/// <summary>
///     Contract tests verifying the JSON schema of command/execute request and response.
/// </summary>
public class SerializedCommandEndpointContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public void Request_ShouldSerializeWithCamelCase()
    {
        // Given
        var request = new SerializedCommandExecuteRequest(
            CommandName: "CreateWeatherForecast",
            CommandJson: "{\"forecastId\":\"f-1\",\"location\":\"Tokyo\",\"temperatureC\":22,\"summary\":\"Warm\"}",
            ConsistencyTags: new List<ConsistencyTagEntry>
            {
                new(Tag: "weather:f-1", LastSortableUniqueId: "uid-001")
            },
            Options: new SerializedCommandOptions(DryRun: false, WaitForSortableUniqueId: null));

        // When
        var json = JsonSerializer.Serialize(request, JsonOptions);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Then
        Assert.True(root.TryGetProperty("commandName", out var commandName));
        Assert.Equal("CreateWeatherForecast", commandName.GetString());

        Assert.True(root.TryGetProperty("commandJson", out var commandJson));
        Assert.Contains("forecastId", commandJson.GetString());

        Assert.True(root.TryGetProperty("consistencyTags", out var tags));
        Assert.Equal(1, tags.GetArrayLength());
        Assert.Equal("weather:f-1", tags[0].GetProperty("tag").GetString());
        Assert.Equal("uid-001", tags[0].GetProperty("lastSortableUniqueId").GetString());

        Assert.True(root.TryGetProperty("options", out var options));
        Assert.False(options.GetProperty("dryRun").GetBoolean());
    }

    [Fact]
    public void Request_WithNullOptionals_ShouldSerializeAsNull()
    {
        // Given
        var request = new SerializedCommandExecuteRequest(
            CommandName: "DeleteWeatherForecast",
            CommandJson: "{\"forecastId\":\"f-1\"}",
            ConsistencyTags: null,
            Options: null);

        // When
        var json = JsonSerializer.Serialize(request, JsonOptions);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Then
        Assert.Equal("DeleteWeatherForecast", root.GetProperty("commandName").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("consistencyTags").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("options").ValueKind);
    }

    [Fact]
    public void Response_ShouldDeserializeFromCamelCaseJson()
    {
        // Given
        var payloadJson = "{\"forecastId\":\"f-1\"}";
        var payloadBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(payloadJson));

        var json = $$"""
        {
            "eventCandidates": [
                {
                    "eventPayloadName": "WeatherForecastCreated",
                    "payloadBase64": "{{payloadBase64}}",
                    "tags": ["weather:f-1"]
                }
            ],
            "consistencyTags": [
                {
                    "tag": "weather:f-1",
                    "lastSortableUniqueId": "uid-001"
                }
            ],
            "commandResultJson": "{\"forecastId\":\"f-1\"}",
            "firstEventId": "11111111-1111-1111-1111-111111111111",
            "lastSortableUniqueId": "uid-001"
        }
        """;

        // When
        var response = JsonSerializer.Deserialize<SerializedCommandExecuteResponse>(json, JsonOptions);

        // Then
        Assert.NotNull(response);
        Assert.Single(response.EventCandidates);
        Assert.Equal("WeatherForecastCreated", response.EventCandidates[0].EventPayloadName);
        Assert.Equal(payloadBase64, response.EventCandidates[0].PayloadBase64);
        Assert.Single(response.EventCandidates[0].Tags);
        Assert.Equal("weather:f-1", response.EventCandidates[0].Tags[0]);
        Assert.Single(response.ConsistencyTags);
        Assert.Equal("weather:f-1", response.ConsistencyTags[0].Tag);
        Assert.Equal("{\"forecastId\":\"f-1\"}", response.CommandResultJson);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), response.FirstEventId);
        Assert.Equal("uid-001", response.LastSortableUniqueId);
    }

    [Fact]
    public void PayloadBase64_ShouldRoundTripCorrectly()
    {
        // Given
        var originalPayload = "{\"forecastId\":\"f-1\",\"location\":\"Tokyo\",\"temperatureC\":22}";
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(originalPayload));

        var candidate = new SerializedCommandEventCandidate(
            EventPayloadName: "WeatherForecastCreated",
            PayloadBase64: base64,
            Tags: new List<string> { "weather:f-1" });

        // When
        var serialized = JsonSerializer.Serialize(candidate, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<SerializedCommandEventCandidate>(serialized, JsonOptions);

        // Then
        Assert.NotNull(deserialized);
        var decodedPayload = Encoding.UTF8.GetString(Convert.FromBase64String(deserialized.PayloadBase64));
        Assert.Equal(originalPayload, decodedPayload);
    }

    [Fact]
    public void Response_WithEmptyEventCandidates_ShouldDeserialize()
    {
        // Given — command that produces no events (e.g. idempotent update with same value)
        var json = """
        {
            "eventCandidates": [],
            "consistencyTags": [],
            "commandResultJson": null,
            "firstEventId": null,
            "lastSortableUniqueId": null
        }
        """;

        // When
        var response = JsonSerializer.Deserialize<SerializedCommandExecuteResponse>(json, JsonOptions);

        // Then
        Assert.NotNull(response);
        Assert.Empty(response.EventCandidates);
        Assert.Empty(response.ConsistencyTags);
        Assert.Null(response.CommandResultJson);
        Assert.Null(response.FirstEventId);
        Assert.Null(response.LastSortableUniqueId);
    }

    [Fact]
    public void Response_ShouldSerializeWithCamelCase()
    {
        // Given
        var payloadBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"forecastId\":\"f-1\"}"));
        var response = new SerializedCommandExecuteResponse(
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

        // When
        var json = JsonSerializer.Serialize(response, JsonOptions);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Then
        Assert.True(root.TryGetProperty("eventCandidates", out var candidates));
        Assert.Equal(1, candidates.GetArrayLength());
        Assert.Equal("WeatherForecastCreated",
            candidates[0].GetProperty("eventPayloadName").GetString());
        Assert.Equal(payloadBase64,
            candidates[0].GetProperty("payloadBase64").GetString());
        Assert.True(root.TryGetProperty("consistencyTags", out _));
        Assert.Equal("11111111-1111-1111-1111-111111111111", root.GetProperty("firstEventId").GetString());
        Assert.Equal("uid-001", root.GetProperty("lastSortableUniqueId").GetString());
    }

    [Fact]
    public void CommandTypeRegistry_ShouldResolveKnownType()
    {
        // Given
        var registry = new SerializedCommandTypeRegistry(
            new[] { typeof(SekibanWasm.Cs.Domain.Weather.CreateWeatherForecast) });

        // When
        var type = registry.GetCommandType("CreateWeatherForecast");

        // Then
        Assert.Equal(typeof(SekibanWasm.Cs.Domain.Weather.CreateWeatherForecast), type);
    }

    [Fact]
    public void CommandTypeRegistry_ShouldThrowForUnknownType()
    {
        // Given
        var registry = new SerializedCommandTypeRegistry(
            new[] { typeof(SekibanWasm.Cs.Domain.Weather.CreateWeatherForecast) });

        // When / Then
        var ex = Assert.Throws<ArgumentException>(() => registry.GetCommandType("NonExistentCommand"));
        Assert.Contains("Unknown command type", ex.Message);
    }

    [Fact]
    public void CommandTypeRegistry_FromAssemblies_ShouldFindAllCommands()
    {
        // Given / When
        var registry = SerializedCommandTypeRegistry.FromAssemblies(
            typeof(SekibanWasm.Cs.Domain.Weather.CreateWeatherForecast).Assembly);

        // Then — should find all 3 weather commands
        Assert.NotNull(registry.GetCommandType("CreateWeatherForecast"));
        Assert.NotNull(registry.GetCommandType("DeleteWeatherForecast"));
        Assert.NotNull(registry.GetCommandType("UpdateWeatherForecastLocation"));
    }

    [Fact]
    public void CommandTypeRegistry_FromAssemblies_ShouldCloseSingleCandidateGenericCommands()
    {
        // Given / When
        var registry = SerializedCommandTypeRegistry.FromAssemblies(typeof(GenericCommandWithSingleConstraint<>).Assembly);

        // Then
        Assert.Equal(
            typeof(GenericCommandWithSingleConstraint<TestModuleFactory>),
            registry.GetCommandType("GenericCommandWithSingleConstraint`1"));
    }

    private interface ITestModuleFactory;

    private sealed class TestModuleFactory : ITestModuleFactory;

    private sealed record GenericCommandWithSingleConstraint<TModuleFactory>()
        : ICommandWithHandler<GenericCommandWithSingleConstraint<TModuleFactory>>
        where TModuleFactory : ITestModuleFactory
    {
        public static Task<EventOrNone> HandleAsync(
            GenericCommandWithSingleConstraint<TModuleFactory> command,
            ICommandContext context) => Task.FromResult(EventOrNone.Empty);
    }

}
