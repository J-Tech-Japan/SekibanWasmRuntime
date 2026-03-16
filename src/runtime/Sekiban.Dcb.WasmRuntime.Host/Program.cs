using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Orleans.Hosting;
using Sekiban.Dcb;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Orleans;
using Sekiban.Dcb.Orleans.Grains;
using Sekiban.Dcb.Orleans.Serialization;
using Sekiban.Dcb.Orleans.ServiceId;
using Sekiban.Dcb.Orleans.Streams;
using Sekiban.Dcb.Postgres;
using Sekiban.Dcb.Runtime;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.WasmRuntime;
using Sekiban.Dcb.WasmRuntime.Host;
using Sekiban.Dcb.WasmRuntime.Wasmtime;

var builder = WebApplication.CreateBuilder(args);

var manifestPath = ResolveManifestPath(builder.Configuration);
var manifest = SekibanRuntimeManifest.Load(builder.Configuration, manifestPath);
manifest.Validate();

var jsonOptions = ManifestDomainTypes.CreateJsonOptions();
var domainTypes = ManifestDomainTypes.Create(manifest, jsonOptions);
var registry = manifest.CreateRegistry();
var connectionString = ResolveConnectionString(builder.Configuration);

builder.UseOrleans(silo =>
{
    silo.UseLocalhostClustering();
    silo.AddMemoryGrainStorage("OrleansStorage");
    silo.AddMemoryGrainStorage("PubSubStore");
    silo.AddMemoryStreams("SekibanQueue");
    silo.AddMemoryStreams("EventStreamProvider");
});

builder.Services.AddSingleton(manifest);
builder.Services.AddSingleton(domainTypes);
builder.Services.AddSingleton<JsonSerializerOptions>(jsonOptions);
builder.Services.AddSingleton(registry);

builder.Services.AddSingleton<IServiceIdProvider, DefaultServiceIdProvider>();
builder.Services.AddSekibanDcbPostgres(connectionString);
builder.Services.AddSekibanDcbNativeRuntime();
builder.Services.AddSingleton<IEventSubscriptionResolver>(_ =>
    new DefaultOrleansEventSubscriptionResolver(
        "EventStreamProvider",
        "AllEvents",
        Guid.Empty));
builder.Services.AddSingleton<IMultiProjectionEventStatistics, NoOpMultiProjectionEventStatistics>();
builder.Services.AddSingleton(new GeneralMultiProjectionActorOptions());
builder.Services.AddSingleton<IActorObjectAccessor, OrleansActorObjectAccessor>();
builder.Services.AddTransient<OrleansDcbExecutor>();
builder.Services.AddTransient<ISekibanExecutor>(sp =>
    sp.GetRequiredService<OrleansDcbExecutor>());
builder.Services.AddTransient<ISerializedSekibanDcbExecutor>(sp =>
    sp.GetRequiredService<OrleansDcbExecutor>());

builder.Services.AddWasmtimeProjectionHost(options =>
{
    options.DefaultModulePath = manifest.DefaultModulePath;
});
builder.Services.AddWasmTagStateRuntime(options =>
{
    options.Mode = WasmRuntimeMode.Wasm;
    options.WasmModulePath = manifest.DefaultModulePath;
});

builder.Services.AddOpenApi();

var app = builder.Build();

app.MapOpenApi();
app.MapGet("/", () => Results.Ok(new
{
    runtime = "Sekiban WASM Runtime Host",
    manifest.DefaultModulePath,
    projectors = manifest.Projectors.Select(static projector => new
    {
        projector.ProjectorName,
        projector.ModulePath,
        projector.ProjectorVersion
    }),
    queryMappings = manifest.QueryProjectors
}));
app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    manifest.DefaultModulePath
}));

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

app.MapPost("/api/sekiban/serialized/commit", async (
    HttpContext http,
    SerializedCommitRequest request,
    CancellationToken ct) =>
{
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
        return await ExecuteSerializedQueryAsync(http, request, isListQuery: false, ct);
    }
    catch (TimeoutException ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status504GatewayTimeout);
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
        return await ExecuteSerializedQueryAsync(http, request, isListQuery: true, ct);
    }
    catch (TimeoutException ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status504GatewayTimeout);
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
    var manifest = http.RequestServices.GetRequiredService<SekibanRuntimeManifest>();
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
        QueryAssemblyVersion = manifest.QueryAssemblyVersion,
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

    throw new TimeoutException($"Timed out waiting for SortableUniqueId '{sortableUniqueId}'.");
}

static async Task<byte[]> CompressStringAsync(string value)
{
    var bytes = Encoding.UTF8.GetBytes(value);
    await using var output = new MemoryStream();
    await using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
    {
        await gzip.WriteAsync(bytes);
    }

    return output.ToArray();
}

static async Task<string> DecompressToStringAsync(byte[] compressedData)
{
    if (compressedData.Length == 0)
    {
        return string.Empty;
    }

    await using var input = new MemoryStream(compressedData);
    await using var gzip = new GZipStream(input, CompressionMode.Decompress);
    using var reader = new StreamReader(gzip, Encoding.UTF8);
    return await reader.ReadToEndAsync();
}

static string ResolveManifestPath(IConfiguration configuration)
{
    var candidates = new[]
    {
        Environment.GetEnvironmentVariable("SEKIBAN_MANIFEST_PATH"),
        configuration["Sekiban:ManifestPath"],
        Path.Combine(Directory.GetCurrentDirectory(), "sekiban-manifest.json"),
        Path.GetFullPath(Path.Combine(
            Directory.GetCurrentDirectory(),
            "..",
            "..",
            "..",
            "docker",
            "sekiban-wasm-runtime",
            "config",
            "sekiban-manifest.json"))
    };

    return candidates.First(static path => !string.IsNullOrWhiteSpace(path))!;
}

static string ResolveConnectionString(IConfiguration configuration)
{
    var candidates = new[]
    {
        configuration.GetConnectionString("SekibanDcb"),
        Environment.GetEnvironmentVariable("ConnectionStrings__SekibanDcb"),
        Environment.GetEnvironmentVariable("SEKIBAN_DCB_CONNECTION"),
        "Host=127.0.0.1;Port=5432;Database=sekiban;Username=postgres;Password=postgres"
    };

    return candidates.First(static candidate => !string.IsNullOrWhiteSpace(candidate))!;
}
