using System.Text.Json;
using SekibanWasm.Cs.Domain;
using SekibanWasm.Cs.Domain.Weather;
using Sekiban.Dcb;
using Sekiban.Dcb.Orleans;
using Sekiban.Dcb.Postgres;
using Sekiban.Dcb.Primitives;
using Sekiban.Dcb.Runtime;
using Sekiban.Dcb.Storage;
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

var wasmModulePath = builder.Configuration["Wasm:DefaultModulePath"]
    ?? throw new InvalidOperationException(
        "Wasm:DefaultModulePath configuration is required. " +
        "Set the Wasm__DefaultModulePath environment variable to the absolute path of the C# .wasm module.");

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

CommandEndpoints.Map(app);
InstanceEndpoints.Map(app);

app.Run();
