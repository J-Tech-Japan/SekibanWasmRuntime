using System.Text.Json;
using System.Net.Http.Json;
using SekibanWasm.Cs.ClientApi;
using SekibanWasm.Cs.Domain;
using SekibanWasm.Cs.Domain.Weather;
using Sekiban.Dcb.WasmRuntime;
using Sekiban.Dcb.WasmRuntime.Remote;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var domainSerializerOptions = new DomainSerializerOptions(DomainJsonContext.Default.Options);
var transportSerializerOptions = new TransportSerializerOptions(
    new JsonSerializerOptions(JsonSerializerDefaults.Web));
builder.Services.AddSingleton(domainSerializerOptions);
builder.Services.AddSingleton(transportSerializerOptions);

var wasmServerBaseUrl = ResolveWasmServerBase(builder.Configuration);
builder.Services.AddHttpClient("wasmserver", client =>
{
    client.BaseAddress = new Uri(wasmServerBaseUrl);
});
builder.Services.AddHttpClient("serialized-dcb");
builder.Services.AddHttpClient("serialized-query");
builder.Services.AddScoped<ISerializedDcbClient>(sp =>
    new HttpSerializedDcbClient(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("serialized-dcb"),
        new SerializedDcbClientOptions { BaseUrl = wasmServerBaseUrl },
        sp.GetRequiredService<TransportSerializerOptions>().Value));
builder.Services.AddScoped<ISerializedQueryClient>(sp =>
    new HttpSerializedQueryClient(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("serialized-query"),
        new SerializedDcbClientOptions { BaseUrl = wasmServerBaseUrl },
        sp.GetRequiredService<TransportSerializerOptions>().Value,
        sp.GetRequiredService<DomainSerializerOptions>().Value));
builder.Services.AddScoped<IWeatherQueryClient, WeatherQueryClient>();
builder.Services.AddScoped<ClientApiCommandFlow>();
builder.Services.AddScoped<ISekibanCommandCommitRequestBuilder>(sp =>
    sp.GetRequiredService<ClientApiCommandFlow>());
builder.Services.AddScoped<ISekibanWasmExecutor>(sp =>
    new SekibanWasmExecutor(
        sp.GetRequiredService<ISerializedDcbClient>(),
        sp.GetRequiredService<ISerializedQueryClient>(),
        sp.GetRequiredService<ISekibanCommandCommitRequestBuilder>(),
        sp.GetRequiredService<DomainSerializerOptions>().Value));

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
    var executor = http.RequestServices.GetRequiredService<ISekibanWasmExecutor>();
    var result = await executor.ExecuteListQueryAsync<List<WeatherForecastItem>>(
        nameof(GetWeatherForecastListQuery),
        new GetWeatherForecastListQuery
        {
            PageNumber = pageNumber,
            PageSize = pageSize
        },
        waitForSortableId,
        ct);

    if (!result.IsSuccess)
    {
        return Results.BadRequest(new { error = result.GetException().Message });
    }

    return Results.Ok(result.GetValue());
});

app.MapPost("/api/weatherforecast", async (
    HttpContext http,
    CreateWeatherForecast command,
    CancellationToken ct) =>
{
    return await ExecuteCommandAsync(http, "CreateWeatherForecast", command, ct);
});

app.MapPost("/api/weatherforecast/delete", async (
    HttpContext http,
    DeleteWeatherForecastRequest request,
    CancellationToken ct) =>
{
    var command = new DeleteWeatherForecast(request.ForecastId);
    return await ExecuteCommandAsync(http, "DeleteWeatherForecast", command, ct);
});

app.MapPost("/api/weatherforecast/update-location", async (
    HttpContext http,
    UpdateLocationRequest request,
    CancellationToken ct) =>
{
    var command = new UpdateWeatherForecastLocation(request.ForecastId, request.NewLocation);
    return await ExecuteCommandAsync(http, "UpdateWeatherForecastLocation", command, ct);
});

app.Run();

static async Task<IResult> ExecuteCommandAsync(
    HttpContext http,
    string commandName,
    object command,
    CancellationToken ct)
{
    var executor = http.RequestServices.GetRequiredService<ISekibanWasmExecutor>();
    var result = await executor.ExecuteCommandAsync(commandName, command, ct);
    if (!result.IsSuccess)
    {
        return Results.BadRequest(new { error = result.GetException().Message });
    }

    return Results.Ok(result.GetValue());
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
