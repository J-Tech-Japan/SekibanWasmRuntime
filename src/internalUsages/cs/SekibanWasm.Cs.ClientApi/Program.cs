using System.Text.Json;
using System.Net.Http.Json;
using SekibanWasm.Cs.ClientApi;
using SekibanWasm.Cs.Domain;
using SekibanWasm.Cs.Domain.Weather;
using System.Text.Json.Nodes;
using Sekiban.Dcb.WasmRuntime;
using Sekiban.Dcb.WasmRuntime.Remote;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var jsonOptions = DomainJsonContext.Default.Options;
var transportJsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
builder.Services.AddSingleton(jsonOptions);
builder.Services.AddSingleton(transportJsonOptions);

var wasmServerBaseUrl = ResolveWasmServerBase(builder.Configuration);
builder.Services.AddHttpClient("wasmserver", client =>
{
    client.BaseAddress = new Uri(wasmServerBaseUrl);
});
builder.Services.AddHttpClient("serialized-dcb");
builder.Services.AddScoped<ISerializedDcbClient>(sp =>
    new HttpSerializedDcbClient(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("serialized-dcb"),
        new SerializedDcbClientOptions { BaseUrl = wasmServerBaseUrl },
        transportJsonOptions));
builder.Services.AddScoped<IWeatherQueryClient, WeatherQueryClient>();
builder.Services.AddScoped<ClientApiCommandFlow>();

builder.Services.AddOpenApi();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapOpenApi();

app.MapGet("/api/weatherforecast", async Task<IResult> (
    HttpContext http,
    string? waitForSortableId,
    int? pageNumber,
    int? pageSize,
    CancellationToken ct) =>
{
    var client = http.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient("wasmserver");
    var jsonOptions = http.RequestServices.GetRequiredService<JsonSerializerOptions>();
    try
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        var listQuery = new GetWeatherForecastListQuery
        {
            PageNumber = pageNumber,
            PageSize = pageSize
        };
        var request = new SerializedQueryRequest(
            QueryType: nameof(GetWeatherForecastListQuery),
            QueryParamsJson: JsonSerializer.Serialize(listQuery, listQuery.GetType(), jsonOptions),
            WaitForSortableUniqueId: waitForSortableId);
        var response = await client.PostAsJsonAsync(
            "/api/sekiban/serialized/list-query",
            request,
            transportJsonOptions,
            timeout.Token);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            return Results.Json(new { error = body }, statusCode: (int)response.StatusCode);
        }

        var result = await response.Content.ReadFromJsonAsync<SerializedListQueryResponse>(
            transportJsonOptions,
            timeout.Token);
        if (result is null)
        {
            return Results.BadRequest(new { error = "Failed to parse serialized list-query response." });
        }

        return Results.Content(result.ItemsJson, "application/json", statusCode: 200);
    }
    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
    {
        return Results.StatusCode(StatusCodes.Status504GatewayTimeout);
    }
    catch (HttpRequestException ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapPost("/api/weatherforecast", async (
    HttpContext http,
    CreateWeatherForecast command,
    CancellationToken ct) =>
{
    var flow = http.RequestServices.GetRequiredService<ClientApiCommandFlow>();
    return await flow.ExecuteAndCommit("CreateWeatherForecast", command, ct);
});

app.MapPost("/api/weatherforecast/delete", async (
    HttpContext http,
    DeleteWeatherForecastRequest request,
    CancellationToken ct) =>
{
    var command = new DeleteWeatherForecast(request.ForecastId);
    var flow = http.RequestServices.GetRequiredService<ClientApiCommandFlow>();
    return await flow.ExecuteAndCommit("DeleteWeatherForecast", command, ct);
});

app.MapPost("/api/weatherforecast/update-location", async (
    HttpContext http,
    UpdateLocationRequest request,
    CancellationToken ct) =>
{
    var command = new UpdateWeatherForecastLocation(request.ForecastId, request.NewLocation);
    var flow = http.RequestServices.GetRequiredService<ClientApiCommandFlow>();
    return await flow.ExecuteAndCommit("UpdateWeatherForecastLocation", command, ct);
});

app.Run();

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
