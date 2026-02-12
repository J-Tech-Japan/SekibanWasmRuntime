using System.Text.Json;
using SekibanWasm.Domain;
using SekibanWasm.Domain.Weather;
using Sekiban.Dcb;
using Sekiban.Dcb.Orleans;
using Sekiban.Dcb.Postgres;
using Sekiban.Dcb.Primitives;
using Sekiban.Dcb.Runtime;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.WasmRuntime;
using Sekiban.Dcb.WasmRuntime.Remote;
using Sekiban.Dcb.WasmRuntime.Wasmtime;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Domain types
var domainTypes = DomainType.GetDomainTypes();
builder.Services.AddSingleton(domainTypes);

// Sekiban DCB setup
builder.Services.AddSingleton<Sekiban.Dcb.ServiceId.IServiceIdProvider, Sekiban.Dcb.ServiceId.DefaultServiceIdProvider>();
builder.Services.AddSingleton<IEventStore, PostgresEventStore>();
builder.Services.AddSekibanDcbPostgresWithAspire();

// Native runtime (registers IEventRuntime, IProjectionRuntime, ITagProjectionRuntime, etc.)
builder.Services.AddSekibanDcbNativeRuntime();

// WASM projection runtime override
var projectionRuntime = builder.Configuration["SEKIBAN_PROJECTION_RUNTIME"] ?? "native";
if (projectionRuntime.Equals("wasm", StringComparison.OrdinalIgnoreCase))
{
    var wasmModulePath = builder.Configuration["Wasm:DefaultModulePath"]
        ?? throw new InvalidOperationException(
            "Wasm:DefaultModulePath configuration is required when SEKIBAN_PROJECTION_RUNTIME=wasm. " +
            "Set the Wasm__DefaultModulePath environment variable to the absolute path of the .wasm module.");

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

    RegisterWeatherQueryMappings(registry);

    builder.Services.AddSingleton(registry);

    builder.Services.AddWasmtimeProjectionHost(opt =>
    {
        opt.DefaultModulePath = wasmModulePath;
    });

    builder.Services.AddSingleton<JsonSerializerOptions>(_ => DomainJsonContext.Default.Options);

    builder.Services.AddSingleton<IProjectionRuntime, WasmProjectionRuntime>();
}
else if (projectionRuntime.Equals("hybrid", StringComparison.OrdinalIgnoreCase))
{
    var wasmModulePath = builder.Configuration["Wasm:DefaultModulePath"]
        ?? throw new InvalidOperationException(
            "Wasm:DefaultModulePath configuration is required when SEKIBAN_PROJECTION_RUNTIME=hybrid. " +
            "Set the Wasm__DefaultModulePath environment variable to the absolute path of the .wasm module.");

    var registry = new WasmProjectorRegistry();
    registry.Register(new WasmModuleRef(
        ProjectorName: "WeatherForecastMultiProjection",
        ModulePath: wasmModulePath,
        AbiKind: "wasi-preview1",
        ModuleVersion: "1.0.0",
        ProjectorVersion: "1.0.0"));

    RegisterWeatherQueryMappings(registry);

    builder.Services.AddSingleton(registry);

    builder.Services.AddWasmtimeProjectionHost(opt =>
    {
        opt.DefaultModulePath = wasmModulePath;
    });

    builder.Services.AddSingleton<JsonSerializerOptions>(_ => DomainJsonContext.Default.Options);

    // Register concrete NativeProjectionRuntime so the resolver factory can resolve it
    // directly without triggering circular dependency via GetServices<IProjectionRuntime>().
    // (GetServices would try to resolve CompositeProjectionRuntime which needs the resolver.)
    builder.Services.AddSingleton<Sekiban.Dcb.Runtime.Native.NativeProjectionRuntime>();

    builder.Services.AddSingleton<WasmProjectionRuntime>();
    builder.Services.AddSingleton<Sekiban.Dcb.WasmRuntime.IProjectorRuntimeResolver>(sp =>
    {
        return new ProjectorRuntimeResolver(
            defaultRuntime: sp.GetRequiredService<Sekiban.Dcb.Runtime.Native.NativeProjectionRuntime>(),
            runtimeMap: new Dictionary<string, IProjectionRuntime>
            {
                ["WeatherForecastMultiProjection"] = sp.GetRequiredService<WasmProjectionRuntime>()
            });
    });

    builder.Services.AddSingleton<IProjectionRuntime, CompositeProjectionRuntime>();
}
else if (projectionRuntime.Equals("remote", StringComparison.OrdinalIgnoreCase))
{
    var remoteEndpoint = builder.Configuration["RemoteRunner:Endpoint"]
        ?? throw new InvalidOperationException(
            "RemoteRunner:Endpoint configuration is required when SEKIBAN_PROJECTION_RUNTIME=remote. " +
            "Set the RemoteRunner__Endpoint environment variable.");

    var registry = new WasmProjectorRegistry();
    registry.Register(new WasmModuleRef(
        ProjectorName: "WeatherForecastMultiProjection",
        ModulePath: "remote",
        AbiKind: "remote",
        ModuleVersion: "1.0.0",
        ProjectorVersion: "1.0.0"));

    RegisterWeatherQueryMappings(registry);

    builder.Services.AddSingleton(registry);
    builder.Services.AddHttpClient();
    builder.Services.AddSingleton(new RemoteRunnerOptions { Endpoint = remoteEndpoint });
    builder.Services.AddSingleton<IPrimitiveProjectionHost, RemotePrimitiveProjectionHost>();
    builder.Services.AddSingleton<JsonSerializerOptions>(_ => DomainJsonContext.Default.Options);
    builder.Services.AddSingleton<IProjectionRuntime, WasmProjectionRuntime>();
}

// OpenAPI
builder.Services.AddOpenApi();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapOpenApi();

// WeatherForecast API endpoints
// Resolve ISekibanExecutor at request-time so missing wiring doesn't break endpoint-table initialization
// (otherwise all routes can fail with "Failure to infer one or more parameters").
app.MapPost("/api/weatherforecast", async (HttpContext http, CreateWeatherForecast command) =>
{
    var executor = http.RequestServices.GetService<ISekibanExecutor>();
    if (executor is null)
    {
        return Results.Problem(
            title: "Sekiban is not configured",
            detail: "ISekibanExecutor is not registered. Configure Sekiban DCB executor wiring before using this endpoint.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var result = await executor.ExecuteAsync(command);
    return Results.Ok(result);
});

app.MapGet("/api/weatherforecast", () =>
{
    return Results.Ok(new { message = "WeatherForecast API is running" });
});

app.Run();

static void RegisterWeatherQueryMappings(WasmProjectorRegistry registry)
{
    registry.MapQueryToProjector("GetWeatherForecastCountQuery", "WeatherForecastMultiProjection");
    registry.MapQueryToProjector("GetWeatherForecastListQuery", "WeatherForecastMultiProjection");
    registry.MapQueryToProjector("WeatherForecastListQuery", "WeatherForecastMultiProjection");
}
