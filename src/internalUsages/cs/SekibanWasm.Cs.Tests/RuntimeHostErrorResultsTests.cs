using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Sekiban.Dcb.WasmRuntime.Host;
using Xunit;

namespace SekibanWasm.Cs.Tests;

public class RuntimeHostErrorResultsTests
{
    [Fact]
    public async Task ReadTimeout_ShouldReturn504WithTimeoutCode()
    {
        const string message = "projection wait exceeded 20000 ms";
        var (statusCode, body) = await ExecuteResultAsync(RuntimeHostErrorResults.ReadTimeout(message));

        Assert.Equal(StatusCodes.Status504GatewayTimeout, statusCode);
        using var document = JsonDocument.Parse(body);
        Assert.Equal(RuntimeHostErrorResults.TimeoutCode, document.RootElement.GetProperty("code").GetString());
        Assert.Equal(message, document.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task CommitUnknownOutcome_ShouldReturn504WithUnknownOutcomeCode()
    {
        const string message = "commit dispatch exceeded 30000 ms";
        var (statusCode, body) = await ExecuteResultAsync(RuntimeHostErrorResults.CommitUnknownOutcome(message));

        Assert.Equal(StatusCodes.Status504GatewayTimeout, statusCode);
        using var document = JsonDocument.Parse(body);
        Assert.Equal(RuntimeHostErrorResults.UnknownOutcomeCode, document.RootElement.GetProperty("code").GetString());
        Assert.Equal(message, document.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task ProjectionUnavailable_ShouldReturn503WithProjectionUnavailableCode()
    {
        const string message = "MultiProjection disabled via SEKIBAN_PROJECTION_MODE=materialized-view-only.";
        var (statusCode, body) = await ExecuteResultAsync(RuntimeHostErrorResults.ProjectionUnavailable(message));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, statusCode);
        using var document = JsonDocument.Parse(body);
        Assert.Equal(
            RuntimeHostErrorResults.ProjectionUnavailableCode,
            document.RootElement.GetProperty("code").GetString());
        Assert.Equal(message, document.RootElement.GetProperty("error").GetString());
    }

    private static async Task<(int StatusCode, string Body)> ExecuteResultAsync(IResult result)
    {
        using var app = WebApplication.CreateBuilder().Build();
        var httpContext = new DefaultHttpContext();
        httpContext.RequestServices = app.Services;
        httpContext.Response.Body = new MemoryStream();

        await result.ExecuteAsync(httpContext);

        httpContext.Response.Body.Position = 0;
        using var reader = new StreamReader(httpContext.Response.Body);
        return (httpContext.Response.StatusCode, await reader.ReadToEndAsync());
    }
}
