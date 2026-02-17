using System.Text.Json;
using SekibanWasm.Rust.Domain;
using SekibanWasm.Rust.Domain.Weather;
using Sekiban.Dcb;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Orleans;
using Sekiban.Dcb.Postgres;
using Sekiban.Dcb.Runtime;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.WasmRuntime;
using Sekiban.Dcb.WasmRuntime.Wasmtime;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var domainTypes = DomainType.GetDomainTypes();
builder.Services.AddSingleton(domainTypes);

builder.Services.AddSingleton<Sekiban.Dcb.ServiceId.IServiceIdProvider, Sekiban.Dcb.ServiceId.DefaultServiceIdProvider>();
builder.Services.AddSingleton<IEventStore, PostgresEventStore>();
builder.Services.AddSekibanDcbPostgresWithAspire();

builder.Services.AddSekibanDcbNativeRuntime();
builder.Services.AddTransient<Sekiban.Dcb.Orleans.OrleansDcbExecutor>();
builder.Services.AddTransient<ISekibanExecutor>(sp =>
    sp.GetRequiredService<Sekiban.Dcb.Orleans.OrleansDcbExecutor>());
builder.Services.AddTransient<ISerializedSekibanDcbExecutor>(sp =>
    sp.GetRequiredService<Sekiban.Dcb.Orleans.OrleansDcbExecutor>());
builder.Services.AddTransient<ISerializedDcbClient, InProcSerializedDcbClient>();

var wasmModulePath = builder.Configuration["Wasm:DefaultModulePath"]
    ?? throw new InvalidOperationException(
        "Wasm:DefaultModulePath configuration is required. " +
        "Set the Wasm__DefaultModulePath environment variable to the absolute path of the Rust .wasm module.");

var registry = new WasmProjectorRegistry();
registry.Register(new WasmModuleRef(
    ProjectorName: "WeatherForecastProjector",
    ModulePath: wasmModulePath,
    AbiKind: "wasi-preview1",
    ModuleVersion: "1.0.0",
    ProjectorVersion: "v1"));
registry.Register(new WasmModuleRef(
    ProjectorName: "WeatherForecastMultiProjection",
    ModulePath: wasmModulePath,
    AbiKind: "wasi-preview1",
    ModuleVersion: "1.0.0",
    ProjectorVersion: "1.0.0"));

registry.MapQueryToProjector("GetWeatherForecastCountQuery", "WeatherForecastMultiProjection");
registry.MapQueryToProjector("GetWeatherForecastListQuery", "WeatherForecastMultiProjection");
registry.MapQueryToProjector("WeatherForecastListQuery", "WeatherForecastMultiProjection");

builder.Services.AddSingleton(registry);

builder.Services.AddWasmtimeProjectionHost(opt =>
{
    opt.DefaultModulePath = wasmModulePath;
});

builder.Services.AddSingleton<JsonSerializerOptions>(_ => DomainJsonContext.Default.Options);

builder.Services.AddSingleton<IProjectionRuntime, WasmProjectionRuntime>();

builder.Services.AddOpenApi();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapOpenApi();

app.MapPost("/api/weatherforecast", async (HttpContext http, CreateWeatherForecast command) =>
{
    var executor = http.RequestServices.GetRequiredService<ISekibanExecutor>();
    var result = await executor.ExecuteAsync(command);
    return Results.Ok(result);
});

app.MapGet("/api/weatherforecast", () =>
{
    return Results.Ok(new { message = "WeatherForecast API is running" });
});

app.MapPost("/api/weatherforecast/delete", async (HttpContext http, DeleteWeatherForecastRequest request) =>
{
    var executor = http.RequestServices.GetRequiredService<ISekibanExecutor>();
    var command = new DeleteWeatherForecast(request.ForecastId);
    var result = await executor.ExecuteAsync(command);
    return Results.Ok(result);
});

app.MapPost("/api/weatherforecast/update-location", async (HttpContext http, UpdateLocationRequest request) =>
{
    var executor = http.RequestServices.GetRequiredService<ISekibanExecutor>();
    var command = new UpdateWeatherForecastLocation(request.ForecastId, request.NewLocation);
    var result = await executor.ExecuteAsync(command);
    return Results.Ok(result);
});

app.MapPost("/api/sekiban/serialized/tag-state", async (HttpContext http, TagStateRequest request) =>
{
    TagStateId tagStateId;
    try
    {
        tagStateId = TagStateId.Parse(request.TagStateId);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    var executor = http.RequestServices.GetRequiredService<ISerializedSekibanDcbExecutor>();
    var result = await executor.GetSerializableTagStateAsync(tagStateId);
    if (!result.IsSuccess)
    {
        return Results.BadRequest(new { error = result.GetException().Message });
    }
    return Results.Ok(result.GetValue());
});

app.MapPost("/api/sekiban/serialized/commit", async (HttpContext http, SerializedCommitRequest request, CancellationToken ct) =>
{
    var executor = http.RequestServices.GetRequiredService<ISerializedSekibanDcbExecutor>();
    var result = await executor.CommitSerializableEventsAsync(request, ct);
    if (!result.IsSuccess)
    {
        return Results.BadRequest(new { error = result.GetException().Message });
    }
    return Results.Ok(result.GetValue());
});

app.Run();

public record DeleteWeatherForecastRequest(string ForecastId);
public record UpdateLocationRequest(string ForecastId, string NewLocation);
public record TagStateRequest(string TagStateId);
