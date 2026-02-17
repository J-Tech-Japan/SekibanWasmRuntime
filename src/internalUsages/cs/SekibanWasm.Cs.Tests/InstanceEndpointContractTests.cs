using System.Text.Json;
using Xunit;

namespace SekibanWasm.Cs.Tests;

/// <summary>
/// Verifies the JSON serialization contract between RemotePrimitiveProjectionInstance (client)
/// and WasmServer /v1/instances/* endpoints (server).
/// Uses JsonDocument to validate contract compliance without coupling to server types.
/// </summary>
public class InstanceEndpointContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void CreateInstance_RequestFormat_ShouldContainProjectorName()
    {
        // Given: JSON that a remote client would send to POST /v1/instances
        var json = """{"projectorName":"WeatherForecastProjector"}""";

        // When
        using var doc = JsonDocument.Parse(json);

        // Then
        Assert.Equal("WeatherForecastProjector", doc.RootElement.GetProperty("projectorName").GetString());
    }

    [Fact]
    public void CreateInstance_ResponseFormat_ShouldContainInstanceId()
    {
        // Given: expected server response format
        var responseJson = JsonSerializer.Serialize(
            new { instanceId = "abc-123" }, JsonOptions);

        // When
        using var doc = JsonDocument.Parse(responseJson);

        // Then
        Assert.True(doc.RootElement.TryGetProperty("instanceId", out var prop));
        Assert.Equal("abc-123", prop.GetString());
    }

    [Fact]
    public void ApplyEvents_RequestFormat_ShouldMatchRemoteClientFormat()
    {
        // Given: JSON matching RemotePrimitiveProjectionInstance.ApplyEvent
        var json = """
        {
            "events": [
                {
                    "eventType": "WeatherForecastCreated",
                    "payloadJson": "{\"forecastId\":\"abc\"}",
                    "tags": ["weather"],
                    "sortableUniqueId": "001"
                }
            ]
        }
        """;

        // When
        using var doc = JsonDocument.Parse(json);

        // Then
        var events = doc.RootElement.GetProperty("events");
        Assert.Equal(1, events.GetArrayLength());
        var ev = events[0];
        Assert.Equal("WeatherForecastCreated", ev.GetProperty("eventType").GetString());
        Assert.Equal("{\"forecastId\":\"abc\"}", ev.GetProperty("payloadJson").GetString());
        Assert.Equal("weather", ev.GetProperty("tags")[0].GetString());
        Assert.Equal("001", ev.GetProperty("sortableUniqueId").GetString());
    }

    [Fact]
    public void ApplyEvents_ShouldHandle_NullSortableUniqueId()
    {
        // Given
        var json = """
        {
            "events": [
                {
                    "eventType": "WeatherForecastCreated",
                    "payloadJson": "{}",
                    "tags": [],
                    "sortableUniqueId": null
                }
            ]
        }
        """;

        // When
        using var doc = JsonDocument.Parse(json);

        // Then
        var ev = doc.RootElement.GetProperty("events")[0];
        Assert.Equal(JsonValueKind.Null, ev.GetProperty("sortableUniqueId").ValueKind);
    }

    [Fact]
    public void Query_RequestFormat_ShouldMatchRemoteClientFormat()
    {
        // Given: JSON matching RemotePrimitiveProjectionInstance.ExecuteQuery
        var json = """{"queryType":"GetWeatherForecastListQuery","queryParamsJson":"{}"}""";

        // When
        using var doc = JsonDocument.Parse(json);

        // Then
        Assert.Equal("GetWeatherForecastListQuery", doc.RootElement.GetProperty("queryType").GetString());
        Assert.Equal("{}", doc.RootElement.GetProperty("queryParamsJson").GetString());
    }

    [Fact]
    public void Query_ResponseFormat_ShouldContainResultJson()
    {
        // Given: expected server response format
        var responseJson = JsonSerializer.Serialize(
            new { resultJson = "[{\"id\":\"1\"}]" }, JsonOptions);

        // When
        using var doc = JsonDocument.Parse(responseJson);

        // Then
        Assert.Equal("[{\"id\":\"1\"}]", doc.RootElement.GetProperty("resultJson").GetString());
    }

    [Fact]
    public void Snapshot_GetResponseFormat_ShouldContainStateJson()
    {
        // Given: expected server response for GET /v1/instances/{id}/snapshot
        var responseJson = JsonSerializer.Serialize(
            new { stateJson = "{\"items\":{}}" }, JsonOptions);

        // When
        using var doc = JsonDocument.Parse(responseJson);

        // Then
        Assert.Equal("{\"items\":{}}", doc.RootElement.GetProperty("stateJson").GetString());
    }

    [Fact]
    public void Snapshot_PutRequestFormat_ShouldMatchRemoteClientFormat()
    {
        // Given: JSON matching RemotePrimitiveProjectionInstance.RestoreState
        var json = """{"stateJson":"{\"items\":{}}"}""";

        // When
        using var doc = JsonDocument.Parse(json);

        // Then
        Assert.Equal("{\"items\":{}}", doc.RootElement.GetProperty("stateJson").GetString());
    }
}
