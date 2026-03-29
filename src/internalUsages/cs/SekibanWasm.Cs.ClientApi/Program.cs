using System.Text.Json;
using Sekiban.Dcb;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.WasmRuntime;
using Sekiban.Dcb.WasmRuntime.Remote;
using SekibanWasm.Cs.ClientApi;
using SekibanWasm.Cs.Domain;
using SekibanWasm.Cs.Domain.Weather;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var domainSerializerOptions = new DomainSerializerOptions(DomainJsonContext.Default.Options);
var transportSerializerOptions = new TransportSerializerOptions(
    new JsonSerializerOptions(JsonSerializerDefaults.Web));
builder.Services.AddSingleton(domainSerializerOptions);
builder.Services.AddSingleton(transportSerializerOptions);

var domainTypes = DomainType.GetDomainTypes();
builder.Services.AddSingleton(domainTypes);

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
builder.Services.AddSingleton<IEventPublisher, NoOpEventPublisher>();
builder.Services.AddScoped<ISekibanExecutor>(sp =>
    new RemoteSekibanExecutor(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("wasmserver"),
        sp.GetRequiredService<DcbDomainTypes>(),
        sp.GetRequiredService<IEventPublisher>(),
        sp.GetRequiredService<ILogger<RemoteSekibanExecutor>>(),
        sp.GetRequiredService<ILoggerFactory>()));

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
    var executor = http.RequestServices.GetRequiredService<ISekibanExecutor>();
    var result = await executor.QueryAsync(
        new GetWeatherForecastListQuery
        {
            PageNumber = pageNumber,
            PageSize = pageSize,
            WaitForSortableUniqueId = waitForSortableId
        });

    return Results.Ok(result.Items);
});

app.MapPost("/api/weatherforecast", async (
    HttpContext http,
    CreateWeatherForecast command,
    CancellationToken ct) =>
{
    return await ExecuteRemoteCommandAsync(http, command, ct);
});

app.MapPost("/api/weatherforecast/delete", async (
    HttpContext http,
    DeleteWeatherForecastRequest request,
    CancellationToken ct) =>
{
    var command = new DeleteWeatherForecast(request.ForecastId);
    return await ExecuteSerializedCommandAsync(http, nameof(DeleteWeatherForecast), command, ct);
});

app.MapPost("/api/weatherforecast/update-location", async (
    HttpContext http,
    UpdateLocationRequest request,
    CancellationToken ct) =>
{
    var command = new UpdateWeatherForecastLocation(request.ForecastId, request.NewLocation);
    return await ExecuteSerializedCommandAsync(http, nameof(UpdateWeatherForecastLocation), command, ct);
});

app.Run();

static async Task<IResult> ExecuteRemoteCommandAsync<TCommand>(
    HttpContext http,
    TCommand command,
    CancellationToken ct)
    where TCommand : ICommandWithHandler<TCommand>
{
    var executor = http.RequestServices.GetRequiredService<ISekibanExecutor>();
    try
    {
        var result = await executor.ExecuteAsync(command, ct);
        return Results.Ok(new CommandResponse(true, null, result.SortableUniqueId));
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new CommandResponse(false, ex.Message, null));
    }
}

static async Task<IResult> ExecuteSerializedCommandAsync(
    HttpContext http,
    string commandName,
    object command,
    CancellationToken ct)
{
    var flow = http.RequestServices.GetRequiredService<ClientApiCommandFlow>();
    try
    {
        var result = await flow.ExecuteAsync(commandName, command, ct);
        string? sortableUniqueId = result.WrittenEvents.LastOrDefault()?.SortableUniqueIdValue;
        return Results.Ok(new CommandResponse(true, null, sortableUniqueId));
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new CommandResponse(false, ex.Message, null));
    }
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
public record CommandResponse(bool Success, string? Error, string? SortableUniqueId);

file sealed class NoOpEventPublisher : IEventPublisher
{
    public Task PublishAsync(
        IReadOnlyCollection<(Event Event, IReadOnlyCollection<ITag> Tags)> events,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
