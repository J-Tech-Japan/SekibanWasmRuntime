using System.IO.Compression;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Configuration;
using Orleans.Hosting;
using Sekiban.Dcb;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.ColdEvents;
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
using Sekiban.Dcb.Sqlite;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.WasmRuntime;
using Sekiban.Dcb.WasmRuntime.Host;
using Sekiban.Dcb.WasmRuntime.Wasmtime;

var builder = WebApplication.CreateBuilder(args);

var manifestPath = ManifestPathResolver.Resolve(builder.Configuration);
var manifest = SekibanRuntimeManifest.Load(builder.Configuration, manifestPath);
manifest.Validate();

var jsonOptions = ManifestDomainTypes.CreateJsonOptions();
var domainTypes = ManifestDomainTypes.Create(manifest, jsonOptions);
var registry = manifest.CreateRegistry();
var storageConfiguration = RuntimeHostStorageConfigurationResolver.Resolve(
    builder.Configuration,
    builder.Environment.ContentRootPath);
var databaseType = storageConfiguration.Provider.ToString().ToLowerInvariant();
var sqliteDatabasePath = storageConfiguration.SqlitePath;
var waitForSortableUniqueIdTimeout = ResolveWaitForSortableUniqueIdTimeout(builder.Configuration);
var queryResponseTimeout = ResolveQueryResponseTimeout(builder.Configuration);
var enableProjectionStatusEndpoint = builder.Environment.IsDevelopment()
    || builder.Configuration.GetValue<bool>("KENBAI_WASM_ENABLE_PROJECTION_STATUS_ENDPOINT");

builder.UseOrleans(silo =>
{
    silo.UseLocalhostClustering();
    silo.Configure<MessagingOptions>(options =>
    {
        options.ResponseTimeout = queryResponseTimeout;
    });
    silo.Configure<SiloMessagingOptions>(options =>
    {
        options.SystemResponseTimeout = TimeSpan.FromMinutes(2);
        options.MaxRequestProcessingTime = queryResponseTimeout + TimeSpan.FromMinutes(1);
    });
    silo.AddMemoryGrainStorage("OrleansStorage");
    silo.AddMemoryGrainStorage("PubSubStore");
    silo.AddMemoryStreams("SekibanQueue");
    silo.AddMemoryStreams("EventStreamProvider");
});

builder.Services.AddSingleton(manifest);
builder.Services.AddSingleton(domainTypes);
builder.Services.AddSingleton<JsonSerializerOptions>(jsonOptions);
builder.Services.AddSingleton(registry);
builder.Services.AddSingleton<ProjectionInstanceStore>();
builder.Services.AddSingleton<DirectSnapshotQueryCache>();
builder.Services.AddSingleton(new WaitForSortableUniqueIdTimeoutOptions
{
    Timeout = waitForSortableUniqueIdTimeout
});
builder.Services.Configure<MessagingOptions>(options =>
{
    options.ResponseTimeout = queryResponseTimeout;
});

builder.Services.AddSingleton<IServiceIdProvider, DefaultServiceIdProvider>();
RuntimeHostStorageConfigurationResolver.ConfigureServices(
    builder.Services,
    builder.Configuration,
    storageConfiguration,
    builder.Environment.ContentRootPath);
builder.Services.AddSekibanDcbNativeRuntime();
builder.Services.AddSingleton<IEventSubscriptionResolver>(_ =>
    new DefaultOrleansEventSubscriptionResolver(
        "EventStreamProvider",
        "AllEvents",
        Guid.Empty));
builder.Services.AddSingleton<IMultiProjectionEventStatistics, NoOpMultiProjectionEventStatistics>();
builder.Services.AddSingleton(new GeneralMultiProjectionActorOptions());
builder.Services.AddSingleton<IActorObjectAccessor, OrleansActorObjectAccessor>();
builder.Services.Replace(ServiceDescriptor.Singleton<IProjectionActorHostFactory, WasmProjectionActorHostFactory>());
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
if (storageConfiguration.RequiresRelationalMigration)
{
    await app.MigrateSekibanDcbDatabaseAsync();
}

app.MapOpenApi();
app.MapGet("/", () => Results.Ok(new
{
    runtime = "Sekiban WASM Runtime Host",
    databaseType,
    sqliteDatabasePath,
    waitForSortableUniqueIdTimeout,
    queryResponseTimeout,
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
    databaseType,
    sqliteDatabasePath,
    waitForSortableUniqueIdTimeout,
    queryResponseTimeout,
    manifest.DefaultModulePath
}));

if (enableProjectionStatusEndpoint)
{
    app.MapGet("/api/sekiban/projections/{projectorName}/status", async (
        HttpContext http,
        string projectorName) =>
    {
        var clusterClient = http.RequestServices.GetRequiredService<IClusterClient>();
        var serviceIdProvider = http.RequestServices.GetRequiredService<IServiceIdProvider>();
        var grainKey = ServiceIdGrainKey.Build(serviceIdProvider.GetCurrentServiceId(), projectorName);
        var grain = clusterClient.GetGrain<IMultiProjectionGrain>(grainKey);

        var status = await grain.GetStatusAsync();
        var catchUpStatus = await grain.GetCatchUpStatusAsync();
        var health = await grain.GetHealthStatusAsync();

        return Results.Ok(new
        {
            projectorName,
            grainKey,
            status,
            catchUpStatus,
            health
        });
    });
}

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
    var logger = http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("SerializedQuery");
    var manifest = http.RequestServices.GetRequiredService<SekibanRuntimeManifest>();
    var registry = http.RequestServices.GetRequiredService<WasmProjectorRegistry>();
    var configuration = http.RequestServices.GetRequiredService<IConfiguration>();
    var waitTimeout = http.RequestServices.GetRequiredService<WaitForSortableUniqueIdTimeoutOptions>().Timeout;
    var projectorName = registry.ResolveProjectorForQuery(request.QueryType);
    if (string.IsNullOrWhiteSpace(projectorName))
    {
        return Results.BadRequest(new { error = $"Query type '{request.QueryType}' is not mapped." });
    }

    var projector = manifest.Projectors.FirstOrDefault(x =>
        string.Equals(x.ProjectorName, projectorName, StringComparison.Ordinal));
    if (projector is null)
    {
        return Results.BadRequest(new { error = $"Projector '{projectorName}' is not registered." });
    }

    var clusterClient = http.RequestServices.GetRequiredService<IClusterClient>();
    var serviceIdProvider = http.RequestServices.GetRequiredService<IServiceIdProvider>();
    var grainKey = ServiceIdGrainKey.Build(serviceIdProvider.GetCurrentServiceId(), projectorName);
    var grain = clusterClient.GetGrain<IMultiProjectionGrain>(grainKey);

    if (!string.IsNullOrWhiteSpace(request.WaitForSortableUniqueId))
    {
        await WaitForSortableUniqueIdAsync(
            grain,
            http.RequestServices.GetService<IMultiProjectionStateStore>(),
            projectorName,
            projector.ProjectorVersion,
            request.WaitForSortableUniqueId!,
            waitTimeout,
            logger,
            ct);
    }

    var directResult = await TryExecuteDirectSnapshotQueryAsync(
        http,
        manifest,
        projectorName,
        request,
        isListQuery,
        logger,
        ct);
    if (directResult is not null)
    {
        return directResult;
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

static async Task<IResult?> TryExecuteDirectSnapshotQueryAsync(
    HttpContext http,
    SekibanRuntimeManifest manifest,
    string projectorName,
    SerializedQueryRequest request,
    bool isListQuery,
    ILogger logger,
    CancellationToken ct)
{
    var storageProvider = RuntimeHostStorageConfigurationResolver.ResolveProvider(
        http.RequestServices.GetRequiredService<IConfiguration>());
    if (storageProvider is not RuntimeHostStorageProvider.Sqlite
        and not RuntimeHostStorageProvider.Postgres)
    {
        return null;
    }

    var projector = manifest.Projectors.FirstOrDefault(x =>
        string.Equals(x.ProjectorName, projectorName, StringComparison.Ordinal));
    if (projector is null)
    {
        return null;
    }

    var store = http.RequestServices.GetService<IMultiProjectionStateStore>();
    if (store is null)
    {
        return null;
    }

    var recordResult = await store.GetLatestForVersionAsync(projectorName, projector.ProjectorVersion, ct);
    if (!recordResult.IsSuccess)
    {
        return Results.BadRequest(new { error = recordResult.GetException().Message });
    }

    var recordOptional = recordResult.GetValue();
    if (!recordOptional.HasValue)
    {
        return null;
    }

    var record = recordOptional.Value;

    var cache = http.RequestServices.GetRequiredService<DirectSnapshotQueryCache>();
    var cacheEntry = cache.GetOrAdd(projectorName);
    var hostFactory = http.RequestServices.GetRequiredService<IProjectionActorHostFactory>();
    await cacheEntry.Gate.WaitAsync(ct);
    try
    {
        var stopwatch = Stopwatch.StartNew();
        logger.LogInformation(
            "Direct snapshot query start: QueryType={QueryType}, Projector={ProjectorName}, IsListQuery={IsListQuery}, EventsProcessed={EventsProcessed}, LastSortableUniqueId={LastSortableUniqueId}",
            request.QueryType,
            projectorName,
            isListQuery,
            record.EventsProcessed,
            record.LastSortableUniqueId);

        if (!cacheEntry.Matches(projector.ProjectorVersion, record.LastSortableUniqueId, record.EventsProcessed))
        {
            var streamResult = await store.OpenStateDataReadStreamAsync(record, ct);
            if (!streamResult.IsSuccess)
            {
                return Results.BadRequest(new { error = streamResult.GetException().Message });
            }

            await using var snapshotStream = streamResult.GetValue();
            var restoredHost = hostFactory.Create(projectorName);
            var restoreResult = await restoredHost.RestoreSnapshotFromStreamAsync(snapshotStream, ct);
            if (!restoreResult.IsSuccess)
            {
                if (restoredHost is IDisposable restoredDisposable)
                {
                    restoredDisposable.Dispose();
                }

                return Results.BadRequest(new { error = restoreResult.GetException().Message });
            }

            var metadataResult = await restoredHost.GetStateMetadataAsync(includeUnsafe: true);
            ProjectionStateMetadata? restoredMetadata = metadataResult.IsSuccess ? metadataResult.GetValue() : null;
            cacheEntry.Replace(
                restoredHost,
                restoredMetadata,
                projector.ProjectorVersion,
                record.LastSortableUniqueId,
                record.EventsProcessed);

            logger.LogInformation(
                "Direct snapshot query restored: QueryType={QueryType}, Projector={ProjectorName}, ElapsedMs={ElapsedMs}",
                request.QueryType,
                projectorName,
                stopwatch.ElapsedMilliseconds);
        }
        else
        {
            logger.LogInformation(
                "Direct snapshot query cache hit: QueryType={QueryType}, Projector={ProjectorName}, ElapsedMs={ElapsedMs}",
                request.QueryType,
                projectorName,
                stopwatch.ElapsedMilliseconds);
        }

        var parameter = new SerializableQueryParameter
        {
            QueryTypeName = request.QueryType,
            QueryAssemblyVersion = manifest.QueryAssemblyVersion,
            CompressedQueryJson = await CompressStringAsync(request.QueryParamsJson)
        };

        var host = cacheEntry.Host!;
        var metadata = cacheEntry.Metadata;

        if (!isListQuery)
        {
            var result = await host.ExecuteQueryAsync(
                parameter,
                metadata?.SafeVersion,
                null,
                null,
                metadata?.UnsafeVersion);
            if (!result.IsSuccess)
            {
                return Results.BadRequest(new { error = result.GetException().Message });
            }

            var resultJson = await DecompressToStringAsync(result.GetValue().CompressedResultJson);
            logger.LogInformation(
                "Direct snapshot query completed: QueryType={QueryType}, Projector={ProjectorName}, ElapsedMs={ElapsedMs}",
                request.QueryType,
                projectorName,
                stopwatch.ElapsedMilliseconds);
            return Results.Ok(new SerializedQueryResponse(resultJson));
        }

        var listResult = await host.ExecuteListQueryAsync(
            parameter,
            metadata?.SafeVersion,
            null,
            null,
            metadata?.UnsafeVersion);
        if (!listResult.IsSuccess)
        {
            return Results.BadRequest(new { error = listResult.GetException().Message });
        }

        var value = listResult.GetValue();
        var itemsJson = await DecompressToStringAsync(value.CompressedItemsJson);
        logger.LogInformation(
            "Direct snapshot list query completed: QueryType={QueryType}, Projector={ProjectorName}, ElapsedMs={ElapsedMs}, TotalCount={TotalCount}, CurrentPage={CurrentPage}, PageSize={PageSize}",
            request.QueryType,
            projectorName,
            stopwatch.ElapsedMilliseconds,
            value.TotalCount,
            value.CurrentPage,
            value.PageSize);
        return Results.Ok(new SerializedListQueryResponse(
            ItemsJson: itemsJson,
            TotalCount: value.TotalCount,
            TotalPages: value.TotalPages,
            CurrentPage: value.CurrentPage,
            PageSize: value.PageSize));
    }
    finally
    {
        cacheEntry.Gate.Release();
    }
}

static async Task WaitForSortableUniqueIdAsync(
    IMultiProjectionGrain grain,
    IMultiProjectionStateStore? stateStore,
    string projectorName,
    string projectorVersion,
    string sortableUniqueId,
    TimeSpan timeout,
    ILogger logger,
    CancellationToken ct)
{
    var started = Stopwatch.StartNew();

    // Match the proven Orleans/internal host behavior: the wait path must trigger
    // subscription/catch-up work first, otherwise polling can wait indefinitely on
    // a stale persisted snapshot without anything advancing the projector.
    await grain.StartSubscriptionAsync();
    await grain.RefreshAsync();

    if (stateStore is not null)
    {
        try
        {
            // After catch-up is kicked off, prefer polling persisted state so the wait loop
            // does not compete with query execution on the same Orleans activation.
            await grain.GetStatusAsync();
        }
        catch (TimeoutException)
        {
            logger.LogWarning(
                "Timed out while activating projector {ProjectorName}; falling back to store polling for SortableUniqueId {SortableUniqueId}",
                projectorName,
                sortableUniqueId);
        }

        while (started.Elapsed < timeout && !ct.IsCancellationRequested)
        {
            try
            {
                if (await grain.IsSortableUniqueIdReceived(sortableUniqueId))
                {
                    return;
                }
            }
            catch (TimeoutException)
            {
                // Fall through to persisted-state polling below.
            }

            var recordResult = await stateStore.GetLatestForVersionAsync(projectorName, projectorVersion, ct);
            if (recordResult.IsSuccess)
            {
                var recordOptional = recordResult.GetValue();
                if (recordOptional.HasValue &&
                    string.Compare(
                        recordOptional.Value.LastSortableUniqueId,
                        sortableUniqueId,
                        StringComparison.Ordinal) >= 0)
                {
                    return;
                }
            }
            else
            {
                logger.LogWarning(
                    recordResult.GetException(),
                    "Failed to read persisted state while waiting for projector {ProjectorName}; falling back to grain polling",
                    projectorName);
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }

        if (started.Elapsed >= timeout)
        {
            throw new TimeoutException($"Timed out waiting for SortableUniqueId '{sortableUniqueId}'.");
        }
    }

    while (started.Elapsed < timeout && !ct.IsCancellationRequested)
    {
        try
        {
            if (await grain.IsSortableUniqueIdReceived(sortableUniqueId))
            {
                return;
            }
        }
        catch (TimeoutException)
        {
            // Catch-up can legitimately keep the grain busy for a while.
            // Keep polling instead of queueing more refresh/subscription work.
        }

        await Task.Delay(TimeSpan.FromSeconds(2), ct);
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
static TimeSpan ResolveWaitForSortableUniqueIdTimeout(IConfiguration configuration)
{
    var candidates = new[]
    {
        configuration["Sekiban:WaitForSortableUniqueIdTimeoutSeconds"],
        Environment.GetEnvironmentVariable("SEKIBAN_WAIT_FOR_SORTABLE_TIMEOUT_SECONDS"),
        Environment.GetEnvironmentVariable("SEKIBAN_QUERY_WAIT_TIMEOUT_SECONDS")
    };

    foreach (var candidate in candidates)
    {
        if (int.TryParse(candidate, out var seconds) && seconds > 0)
        {
            return TimeSpan.FromSeconds(seconds);
        }
    }

    return TimeSpan.FromMinutes(30);
}

static TimeSpan ResolveQueryResponseTimeout(IConfiguration configuration)
{
    var candidates = new[]
    {
        configuration["Sekiban:QueryResponseTimeoutSeconds"],
        Environment.GetEnvironmentVariable("SEKIBAN_QUERY_RESPONSE_TIMEOUT_SECONDS")
    };

    foreach (var candidate in candidates)
    {
        if (int.TryParse(candidate, out var seconds) && seconds > 0)
        {
            return TimeSpan.FromSeconds(seconds);
        }
    }

    return TimeSpan.FromMinutes(5);
}

sealed class WaitForSortableUniqueIdTimeoutOptions
{
    public TimeSpan Timeout { get; init; }
}

sealed class DirectSnapshotQueryCache : IDisposable
{
    private readonly ConcurrentDictionary<string, DirectSnapshotQueryCacheEntry> _entries =
        new(StringComparer.Ordinal);

    public DirectSnapshotQueryCacheEntry GetOrAdd(string projectorName) =>
        _entries.GetOrAdd(projectorName, static _ => new DirectSnapshotQueryCacheEntry());

    public void Dispose()
    {
        foreach (DirectSnapshotQueryCacheEntry entry in _entries.Values)
        {
            entry.Dispose();
        }
    }
}

sealed class DirectSnapshotQueryCacheEntry : IDisposable
{
    public SemaphoreSlim Gate { get; } = new(1, 1);
    public IProjectionActorHost? Host { get; private set; }
    public ProjectionStateMetadata? Metadata { get; private set; }
    private string? ProjectorVersion { get; set; }
    private string? LastSortableUniqueId { get; set; }
    private long EventsProcessed { get; set; }

    public bool Matches(string projectorVersion, string? lastSortableUniqueId, long eventsProcessed) =>
        Host is not null
        && string.Equals(ProjectorVersion, projectorVersion, StringComparison.Ordinal)
        && string.Equals(LastSortableUniqueId, lastSortableUniqueId, StringComparison.Ordinal)
        && EventsProcessed == eventsProcessed;

    public void Replace(
        IProjectionActorHost host,
        ProjectionStateMetadata? metadata,
        string projectorVersion,
        string? lastSortableUniqueId,
        long eventsProcessed)
    {
        DisposeHost();
        Host = host;
        Metadata = metadata;
        ProjectorVersion = projectorVersion;
        LastSortableUniqueId = lastSortableUniqueId;
        EventsProcessed = eventsProcessed;
    }

    public void Dispose()
    {
        DisposeHost();
        Gate.Dispose();
    }

    private void DisposeHost()
    {
        if (Host is IDisposable disposable)
        {
            disposable.Dispose();
        }

        Host = null;
        Metadata = null;
        ProjectorVersion = null;
        LastSortableUniqueId = null;
        EventsProcessed = 0;
    }
}
