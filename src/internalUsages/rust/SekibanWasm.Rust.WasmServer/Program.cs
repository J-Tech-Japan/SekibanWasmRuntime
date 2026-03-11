using System.Text.Json;
using Azure.Storage.Blobs;
using Orleans.Hosting;
using SekibanWasm.Rust.Domain;
using SekibanWasm.Rust.Domain.Weather;
using Sekiban.Dcb;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Orleans;
using Sekiban.Dcb.Postgres;
using Sekiban.Dcb.Runtime;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.WasmRuntime;
using Sekiban.Dcb.WasmRuntime.Wasmtime;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddKeyedAzureTableServiceClient("SekibanRustClusteringTable");
builder.AddKeyedAzureBlobServiceClient("SekibanRustGrainState");
builder.AddKeyedAzureQueueServiceClient("SekibanRustQueue");
builder.UseOrleans(silo =>
{
    silo.UseLocalhostClustering();
    silo.AddAzureBlobGrainStorage(
        "OrleansStorage",
        options =>
        {
            options.Configure<IServiceProvider>((opt, sp) =>
            {
                opt.BlobServiceClient = sp.GetRequiredKeyedService<BlobServiceClient>("SekibanRustGrainState");
                opt.ContainerName = "sekiban-grainstate";
            });
        });
    silo.AddAzureBlobGrainStorage(
        "PubSubStore",
        options =>
        {
            options.Configure<IServiceProvider>((opt, sp) =>
            {
                opt.BlobServiceClient = sp.GetRequiredKeyedService<BlobServiceClient>("SekibanRustGrainState");
                opt.ContainerName = "sekiban-grainstate";
            });
        });
    silo.AddMemoryStreams("SekibanRustQueue");
    silo.AddMemoryStreams("EventStreamProvider");
});

var domainTypes = DomainType.GetDomainTypes();
builder.Services.AddSingleton(domainTypes);

builder.Services.AddSingleton<Sekiban.Dcb.ServiceId.IServiceIdProvider, Sekiban.Dcb.ServiceId.DefaultServiceIdProvider>();
builder.Services.AddSekibanDcbPostgresWithAspire("SekibanRustDb");

builder.Services.AddSekibanDcbNativeRuntime();
builder.Services.AddSingleton<Sekiban.Dcb.Actors.IEventSubscriptionResolver>(_ =>
    new Sekiban.Dcb.Orleans.Streams.DefaultOrleansEventSubscriptionResolver(
        "EventStreamProvider",
        "AllEvents",
        Guid.Empty));
builder.Services.AddSingleton<IMultiProjectionEventStatistics, NoOpMultiProjectionEventStatistics>();
builder.Services.AddSingleton(new Sekiban.Dcb.Actors.GeneralMultiProjectionActorOptions());
builder.Services.AddSingleton<IActorObjectAccessor, OrleansActorObjectAccessor>();
builder.Services.AddTransient<Sekiban.Dcb.Orleans.OrleansDcbExecutor>();
builder.Services.AddTransient<ISekibanExecutor>(sp =>
    sp.GetRequiredService<Sekiban.Dcb.Orleans.OrleansDcbExecutor>());
builder.Services.AddTransient<ISerializedSekibanDcbExecutor>(sp =>
    sp.GetRequiredService<Sekiban.Dcb.Orleans.OrleansDcbExecutor>());

var commandRegistry = SerializedCommandTypeRegistry.FromAssemblies(
    typeof(CreateWeatherForecast).Assembly);
builder.Services.AddSingleton(commandRegistry);

builder.Services.AddTransient<SerializedCommandEndpoints>();
builder.Services.AddTransient<ISerializedCommandExecutor>(sp =>
    sp.GetRequiredService<SerializedCommandEndpoints>());
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
builder.Services.AddWasmTagStateRuntime(opt =>
{
    opt.Mode = WasmRuntimeMode.Wasm;
    opt.WasmModulePath = wasmModulePath;
});

builder.Services.AddOpenApi();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapOpenApi();

SerializedCommandEndpoints.Map(app);
InstanceEndpoints.Map(app);

app.MapGet("/api/weatherforecast", async (HttpContext http) =>
{
    var executor = http.RequestServices.GetRequiredService<ISekibanExecutor>();
    var queryResult = await executor.QueryAsync(new GetWeatherForecastListQuery());
    return Results.Ok(queryResult.Items.ToArray());
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

public record TagStateRequest(string TagStateId);
