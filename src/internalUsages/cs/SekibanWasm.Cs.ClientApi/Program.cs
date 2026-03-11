using System.Text.Json;
using System.Net.Http.Json;
using SekibanWasm.Cs.ClientApi;
using SekibanWasm.Cs.Domain;
using SekibanWasm.Cs.Domain.Weather;
using System.Text.Json.Nodes;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var jsonOptions = DomainJsonContext.Default.Options;
builder.Services.AddSingleton(jsonOptions);

var wasmServerBaseUrl = ResolveWasmServerBase(builder.Configuration);
builder.Services.AddHttpClient("wasmserver", client =>
{
    client.BaseAddress = new Uri(wasmServerBaseUrl);
});

builder.Services.AddOpenApi();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapOpenApi();

app.MapGet("/api/weatherforecast", async (HttpContext http, CancellationToken ct) =>
{
    var client = http.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient("wasmserver");
    var jsonOptions = http.RequestServices.GetRequiredService<JsonSerializerOptions>();
    var transportJsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    try
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        var query = new GetWeatherForecastListQuery();
        var request = new SerializedQueryRequest(
            QueryType: nameof(GetWeatherForecastListQuery),
            QueryParamsJson: JsonSerializer.Serialize(query, query.GetType(), jsonOptions),
            WaitForSortableUniqueId: null);
        var response = await client.PostAsJsonAsync(
            "/api/sekiban/serialized/list-query",
            request,
            transportJsonOptions,
            timeout.Token);
        if (!response.IsSuccessStatusCode)
        {
            return Results.Ok(Array.Empty<WeatherForecastItem>());
        }

        var result = await response.Content.ReadFromJsonAsync<SerializedListQueryResponse>(
            transportJsonOptions,
            timeout.Token);
        if (result is null)
        {
            return Results.Ok(Array.Empty<WeatherForecastItem>());
        }

        return Results.Content(result.ItemsJson, "application/json", statusCode: 200);
    }
    catch
    {
        return Results.Ok(Array.Empty<WeatherForecastItem>());
    }
});

app.MapPost("/api/weatherforecast", async (
    HttpContext http,
    CreateWeatherForecast command,
    CancellationToken ct) =>
{
    return await ExecuteAndCommit(http, "CreateWeatherForecast", command, ct);
});

app.MapPost("/api/weatherforecast/delete", async (
    HttpContext http,
    DeleteWeatherForecastRequest request,
    CancellationToken ct) =>
{
    var command = new DeleteWeatherForecast(request.ForecastId);
    return await ExecuteAndCommit(http, "DeleteWeatherForecast", command, ct);
});

app.MapPost("/api/weatherforecast/update-location", async (
    HttpContext http,
    UpdateLocationRequest request,
    CancellationToken ct) =>
{
    var command = new UpdateWeatherForecastLocation(request.ForecastId, request.NewLocation);
    return await ExecuteAndCommit(http, "UpdateWeatherForecastLocation", command, ct);
});

app.Run();

static async Task<IResult> ExecuteAndCommit(
    HttpContext http,
    string commandName,
    object command,
    CancellationToken ct)
{
    var client = http.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient("wasmserver");
    var jsonOptions = http.RequestServices.GetRequiredService<JsonSerializerOptions>();
    var transportJsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeout.CancelAfter(TimeSpan.FromSeconds(10));

    var executeRequest = new
    {
        commandName,
        commandJson = JsonSerializer.Serialize(command, command.GetType(), jsonOptions),
        consistencyTags = (object?)null,
        options = (object?)null
    };

    HttpResponseMessage executeResponse;
    try
    {
        executeResponse = await client.PostAsJsonAsync(
            "/api/sekiban/serialized/command/execute",
            executeRequest,
            transportJsonOptions,
            timeout.Token);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = $"command/execute request failed: {ex.Message}" });
    }

    if (!executeResponse.IsSuccessStatusCode)
    {
        var body = await executeResponse.Content.ReadAsStringAsync(timeout.Token);
        return Results.BadRequest(new { error = body });
    }

    var executeJson = await executeResponse.Content.ReadFromJsonAsync<JsonObject>(transportJsonOptions, timeout.Token);
    if (executeJson is null)
    {
        return Results.BadRequest(new { error = "Failed to parse command/execute response." });
    }

    var eventCandidatesNode = executeJson["eventCandidates"]?.AsArray();
    var consistencyTagsNode = executeJson["consistencyTags"]?.AsArray() ?? new JsonArray();
    if (eventCandidatesNode is null)
    {
        return Results.BadRequest(new { error = "No eventCandidates in command/execute response." });
    }

    var commitCandidates = new JsonArray();
    foreach (var candidateNode in eventCandidatesNode)
    {
        if (candidateNode is not JsonObject candidate)
        {
            continue;
        }
        commitCandidates.Add(new JsonObject
        {
            ["payload"] = candidate["payloadBase64"]?.GetValue<string>(),
            ["eventPayloadName"] = candidate["eventPayloadName"]?.GetValue<string>(),
            ["tags"] = candidate["tags"]?.DeepClone()
        });
    }

    var commitRequest = new JsonObject
    {
        ["eventCandidates"] = commitCandidates,
        ["consistencyTags"] = consistencyTagsNode.DeepClone()
    };

    HttpResponseMessage commitResponse;
    try
    {
        commitResponse = await client.PostAsJsonAsync(
            "/api/sekiban/serialized/commit",
            commitRequest,
            transportJsonOptions,
            timeout.Token);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = $"commit request failed: {ex.Message}" });
    }

    var commitBody = await commitResponse.Content.ReadAsStringAsync(timeout.Token);
    if (!commitResponse.IsSuccessStatusCode)
    {
        return Results.BadRequest(new { error = commitBody });
    }

    return Results.Content(commitBody, "application/json", statusCode: 200);
}

static string ResolveWasmServerBase(IConfiguration configuration)
{
    var candidates = new[]
    {
        Environment.GetEnvironmentVariable("WASM_SERVER_URL"),
        Environment.GetEnvironmentVariable("services__wasmserver__http__0"),
        Environment.GetEnvironmentVariable("services__wasmserver__https__0"),
        configuration["services:wasmserver:http:0"],
        configuration["services:wasmserver:https:0"],
        "http://127.0.0.1:3000"
    };

    foreach (var candidate in candidates)
    {
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            return candidate;
        }
    }

    return "http://127.0.0.1:3000";
}

public record DeleteWeatherForecastRequest(string ForecastId);
public record UpdateLocationRequest(string ForecastId, string NewLocation);
public record SerializedQueryRequest(
    string QueryType,
    string QueryParamsJson,
    string? WaitForSortableUniqueId = null);
public record SerializedListQueryResponse(
    string ItemsJson,
    int? TotalCount,
    int? TotalPages,
    int? CurrentPage,
    int? PageSize);
