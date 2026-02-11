using Sekiban.Dcb.WasmRuntime.Remote;
using Xunit;

namespace SekibanWasm.Tests;

public class RemoteRuntimeErrorTests
{
    [Fact]
    public void CreateInstance_WithInvalidUrl_ShouldThrow()
    {
        // Given: an unreachable endpoint
        var options = new RemoteRunnerOptions { Endpoint = "http://localhost:1" };
        var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
        var host = new RemotePrimitiveProjectionHost(httpClient, options);

        // When/Then
        Assert.ThrowsAny<Exception>(() => host.CreateInstance("WeatherForecastMultiProjection"));
    }

    [Fact]
    public void ApplyEvent_WithInvalidUrl_ShouldThrow()
    {
        // Given: a remote instance pointing to unreachable endpoint
        var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
        var instance = new RemotePrimitiveProjectionInstance(
            httpClient, "http://localhost:1", "fake-instance-id");

        // When/Then
        Assert.ThrowsAny<Exception>(() =>
            instance.ApplyEvent(
                "TestEvent",
                "{}",
                new List<string>(),
                null));
    }

    [Fact]
    public void ExecuteQuery_WithInvalidUrl_ShouldThrow()
    {
        // Given
        var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
        var instance = new RemotePrimitiveProjectionInstance(
            httpClient, "http://localhost:1", "fake-instance-id");

        // When/Then
        Assert.ThrowsAny<Exception>(() =>
            instance.ExecuteQuery("TestQuery", "{}"));
    }

    [Fact]
    public void SerializeState_WithInvalidUrl_ShouldThrow()
    {
        // Given
        var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
        var instance = new RemotePrimitiveProjectionInstance(
            httpClient, "http://localhost:1", "fake-instance-id");

        // When/Then
        Assert.ThrowsAny<Exception>(() => instance.SerializeState());
    }

    [Fact]
    public void RestoreState_WithInvalidUrl_ShouldThrow()
    {
        // Given
        var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
        var instance = new RemotePrimitiveProjectionInstance(
            httpClient, "http://localhost:1", "fake-instance-id");

        // When/Then
        Assert.ThrowsAny<Exception>(() => instance.RestoreState("{}"));
    }

    [Fact]
    public void Dispose_WithInvalidUrl_ShouldNotThrow()
    {
        // Given: Dispose is best-effort cleanup, should not propagate errors
        var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
        var instance = new RemotePrimitiveProjectionInstance(
            httpClient, "http://localhost:1", "fake-instance-id");

        // When/Then: should not throw
        instance.Dispose();
    }
}
