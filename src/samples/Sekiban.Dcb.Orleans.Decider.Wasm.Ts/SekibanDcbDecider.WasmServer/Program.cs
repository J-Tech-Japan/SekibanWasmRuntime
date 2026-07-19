using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using Orleans.Hosting;
using SekibanWasm.Rust.Domain;
using Sekiban.Dcb;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Orleans;
using Sekiban.Dcb.Orleans.Grains;
using Sekiban.Dcb.Orleans.Serialization;
using Sekiban.Dcb.Orleans.ServiceId;
using Sekiban.Dcb.Postgres;
using Sekiban.Dcb.Runtime;
using Sekiban.Dcb.ServiceId;
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

builder.Services.AddSekibanDcbSharedRuntime();
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
InstanceEndpoints.Map(app);

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

app.MapPost("/api/sekiban/serialized/tag-latest-sortable", async (
    HttpContext http,
    TagLatestSortableRequest request) =>
{
    var actorAccessor = http.RequestServices.GetRequiredService<IActorObjectAccessor>();
    var actorResult = await actorAccessor.GetActorAsync<ITagConsistentActorCommon>(request.Tag);
    if (!actorResult.IsSuccess)
    {
        return Results.Ok(new TagLatestSortableResponse(false, string.Empty));
    }

    var latestSortableResult = await actorResult.GetValue().GetLatestSortableUniqueIdAsync();
    if (!latestSortableResult.IsSuccess)
    {
        return Results.BadRequest(new { error = latestSortableResult.GetException().Message });
    }

    string lastSortableUniqueId = latestSortableResult.GetValue();
    return Results.Ok(new TagLatestSortableResponse(
        !string.IsNullOrWhiteSpace(lastSortableUniqueId),
        lastSortableUniqueId));
});

app.MapPost("/api/sekiban/serialized/commit", async (HttpContext http, CancellationToken ct) =>
{
    // Two-phase acceptance (SEK-G17): discriminate the raw envelope version before any typed binding, so an
    // off-contract or unsupported envelope fails closed instead of binding optimistically.
    var bind = await SerializedCommitEnvelope.BindAsync(http.Request.Body, ct);
    if (bind.Error is { } envelopeError)
    {
        return Results.BadRequest(new { error = envelopeError.Message, code = envelopeError.Code });
    }

    SerializedCommitRequest request = bind.Request!;
    var executor = http.RequestServices.GetRequiredService<ISerializedSekibanDcbExecutor>();
    var result = await executor.CommitSerializableEventsAsync(request, ct);
    if (!result.IsSuccess)
    {
        return Results.BadRequest(new { error = result.GetException().Message });
    }
    return Results.Ok(result.GetValue());
});

app.MapPost("/api/sekiban/serialized/query", async (
    HttpContext http,
    SerializedQueryRequest request,
    CancellationToken ct) =>
{
    try
    {
        var result = await ExecuteSerializedQueryAsync(http, request, isListQuery: false, ct);
        return result;
    }
    catch (TimeoutException)
    {
        return Results.StatusCode(StatusCodes.Status504GatewayTimeout);
    }
    catch (OperationCanceledException)
    {
        return Results.StatusCode(499);
    }
});

app.MapPost("/api/sekiban/serialized/list-query", async (
    HttpContext http,
    SerializedQueryRequest request,
    CancellationToken ct) =>
{
    try
    {
        var result = await ExecuteSerializedQueryAsync(http, request, isListQuery: true, ct);
        return result;
    }
    catch (TimeoutException)
    {
        return Results.StatusCode(StatusCodes.Status504GatewayTimeout);
    }
    catch (OperationCanceledException)
    {
        return Results.StatusCode(499);
    }
});

app.Run();

static async Task<IResult> ExecuteSerializedQueryAsync(
    HttpContext http,
    SerializedQueryRequest request,
    bool isListQuery,
    CancellationToken ct)
{
    var registry = http.RequestServices.GetRequiredService<WasmProjectorRegistry>();
    var projectorName = registry.ResolveProjectorForQuery(request.QueryType);
    if (string.IsNullOrWhiteSpace(projectorName))
    {
        return Results.BadRequest(new { error = $"Query type '{request.QueryType}' is not mapped." });
    }

    var clusterClient = http.RequestServices.GetRequiredService<IClusterClient>();
    var serviceIdProvider = http.RequestServices.GetRequiredService<IServiceIdProvider>();
    var grainKey = ServiceIdGrainKey.Build(serviceIdProvider.GetCurrentServiceId(), projectorName);
    var grain = clusterClient.GetGrain<IMultiProjectionGrain>(grainKey);

    if (!string.IsNullOrWhiteSpace(request.WaitForSortableUniqueId))
    {
        await WaitForSortableUniqueIdAsync(grain, request.WaitForSortableUniqueId!, ct);
    }

    var parameter = new SerializableQueryParameter
    {
        QueryTypeName = request.QueryType,
        QueryAssemblyVersion = "rust",
        CompressedQueryJson = await CompressStringAsync(request.QueryParamsJson)
    };

    if (!isListQuery)
    {
        var result = await grain.ExecuteQueryAsync(parameter);
        var resultJson = await DecompressToStringAsync(result.CompressedResultJson);
        return Results.Ok(new SerializedQueryResponse(resultJson));
    }

    var listResult = await grain.ExecuteListQueryAsync(parameter);
    var itemsJson = await DecompressToStringAsync(listResult.CompressedItemsJson);
    return Results.Ok(new SerializedListQueryResponse(
        ItemsJson: itemsJson,
        TotalCount: listResult.TotalCount,
        TotalPages: listResult.TotalPages,
        CurrentPage: listResult.CurrentPage,
        PageSize: listResult.PageSize));
}

static async Task WaitForSortableUniqueIdAsync(
    IMultiProjectionGrain grain,
    string sortableUniqueId,
    CancellationToken ct)
{
    await grain.StartSubscriptionAsync();
    await grain.RefreshAsync();

    var timeout = TimeSpan.FromSeconds(30);
    var started = DateTime.UtcNow;
    while (DateTime.UtcNow - started < timeout && !ct.IsCancellationRequested)
    {
        if (await grain.IsSortableUniqueIdReceived(sortableUniqueId))
        {
            return;
        }

        await Task.Delay(TimeSpan.FromMilliseconds(200), ct);
    }

    if (ct.IsCancellationRequested)
    {
        throw new OperationCanceledException(ct);
    }

    throw new TimeoutException(
        $"Timed out after {timeout.TotalSeconds} seconds waiting for sortable unique id '{sortableUniqueId}'.");
}

static async Task<byte[]> CompressStringAsync(string value)
{
    if (string.IsNullOrEmpty(value))
    {
        return Array.Empty<byte>();
    }

    var bytes = Encoding.UTF8.GetBytes(value);
    await using var output = new MemoryStream();
    await using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
    {
        await gzip.WriteAsync(bytes);
    }
    return output.ToArray();
}

static async Task<string> DecompressToStringAsync(byte[] value)
{
    if (value.Length == 0)
    {
        return string.Empty;
    }

    await using var input = new MemoryStream(value);
    await using var gzip = new GZipStream(input, CompressionMode.Decompress);
    using var output = new MemoryStream();
    await gzip.CopyToAsync(output);
    return Encoding.UTF8.GetString(output.ToArray());
}

public record TagStateRequest(string TagStateId);
public record TagLatestSortableRequest(string Tag);
public record TagLatestSortableResponse(bool Exists, string LastSortableUniqueId);
public record SerializedQueryRequest(
    string QueryType,
    string QueryParamsJson,
    string? WaitForSortableUniqueId = null);
public record SerializedQueryResponse(string ResultJson);
public record SerializedListQueryResponse(
    string ItemsJson,
    int? TotalCount,
    int? TotalPages,
    int? CurrentPage,
    int? PageSize);
