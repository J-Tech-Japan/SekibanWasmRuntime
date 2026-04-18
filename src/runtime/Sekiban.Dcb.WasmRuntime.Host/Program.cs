using System.IO.Compression;
using System.Collections.Concurrent;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Configuration;
using Orleans.Hosting;
using Sekiban.Dcb;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.ColdEvents;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
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
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sekiban.Dcb.WasmRuntime;
using Sekiban.Dcb.WasmRuntime.Host;
using Sekiban.Dcb.WasmRuntime.Host.MaterializedView;
using Sekiban.Dcb.WasmRuntime.Wasmtime;
using Sekiban.Dcb.MaterializedView;
using Sekiban.Dcb.MaterializedView.Postgres;
using Sekiban.Dcb.MaterializedView.Orleans;
using Dapper;

var builder = WebApplication.CreateBuilder(args);

var manifestPath = ManifestPathResolver.Resolve(builder.Configuration);
var manifest = SekibanRuntimeManifest.Load(builder.Configuration, manifestPath);
manifest.Validate();

var jsonOptions = ManifestDomainTypes.CreateJsonOptions();
var domainTypes = ManifestDomainTypes.Create(manifest, jsonOptions);
// Create registry FIRST (before filtering) so all projectors are available for tag-state
var registry = manifest.CreateRegistry();

// Remove tag-only projectors from Projectors list to prevent MultiProjectionGrain activation.
// Tag-only projectors (not mapped to any query) don't need MultiProjectionGrain.
// They waste 36.6MB WASM linear memory each and process events unnecessarily.
// TagStateGrain handles tag-state via Orleans with persistent caching and delta replay.
// The registry still has all projectors for tag-state lookup.
var queryMappedProjectors = new HashSet<string>(
    manifest.QueryProjectors.Values, StringComparer.Ordinal);
var tagOnlyProjectorNames = manifest.Projectors
    .Where(p => !queryMappedProjectors.Contains(p.ProjectorName))
    .Select(p => p.ProjectorName)
    .ToList();
if (tagOnlyProjectorNames.Count > 0 && !string.Equals(Environment.GetEnvironmentVariable("KEEP_TAG_PROJECTORS"), "true", StringComparison.OrdinalIgnoreCase))
{
    manifest.Projectors.RemoveAll(p => tagOnlyProjectorNames.Contains(p.ProjectorName));
    Console.WriteLine($"Removed {tagOnlyProjectorNames.Count} tag-only projectors from MultiProjectionGrain: {string.Join(", ", tagOnlyProjectorNames)}");
}
var storageConfiguration = RuntimeHostStorageConfigurationResolver.Resolve(
    builder.Configuration,
    builder.Environment.ContentRootPath);
var databaseType = storageConfiguration.Provider.ToString().ToLowerInvariant();
var sqliteDatabasePath = storageConfiguration.SqlitePath;
var waitForSortableUniqueIdTimeout = ResolveWaitForSortableUniqueIdTimeout(builder.Configuration);
var queryResponseTimeout = ResolveQueryResponseTimeout(builder.Configuration);
var directSnapshotQueryEnabled = ResolveDirectSnapshotQueryEnabled(builder.Configuration);
var tagStateFastPathEnabled = ResolveTagStateFastPathEnabled(builder.Configuration);
var multiProjectionActorOptions = ResolveGeneralMultiProjectionActorOptions(builder.Configuration);
var directSnapshotQueryCacheOptions = ResolveDirectSnapshotQueryCacheOptions(builder.Configuration);
var staticMemoryMaximumSizeBytes = ResolveWasmtimeStaticMemoryMaximumSizeBytes(builder.Configuration);
var enableProjectionStatusEndpoint = builder.Environment.IsDevelopment()
    || builder.Configuration.GetValue<bool>("KENBAI_WASM_ENABLE_PROJECTION_STATUS_ENDPOINT");

// SEKIBAN_PROJECTION_MODE selects which projection path the runtime host exposes for the
// benchmark matrix: "dual" (default — MultiProjection in-memory grain AND materialized view
// both wired), "memory-only" (skip MV registration), "materialized-view-only" (MV wired, but
// MultiProjection endpoints return 503 so `MultiProjectionGrain` never activates and no
// projection WASM is loaded). See docs/benchmark-results.md for the intent.
var projectionMode = (Environment.GetEnvironmentVariable("SEKIBAN_PROJECTION_MODE") ?? "dual")
    .Trim()
    .ToLowerInvariant();
var projectionModeEnabled = projectionMode is "dual" or "memory-only";
var materializedViewRequested = projectionMode is "dual" or "materialized-view-only";
Console.WriteLine($"SEKIBAN_PROJECTION_MODE={projectionMode} (multiProjection={projectionModeEnabled}, materializedView={materializedViewRequested})");

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
    // Deactivate idle grains faster to release WASM projection hosts sooner.
    // Default is 120 minutes; 5 minutes is sufficient for benchmark workloads.
    silo.Configure<GrainCollectionOptions>(options =>
    {
        options.CollectionAge = TimeSpan.FromMinutes(5);
    });
});

builder.Services.AddSingleton(manifest);
builder.Services.AddSingleton(domainTypes);
builder.Services.AddSingleton<JsonSerializerOptions>(jsonOptions);
builder.Services.AddSingleton(registry);
builder.Services.AddSingleton<ProjectionInstanceStore>();
builder.Services.AddSingleton(directSnapshotQueryCacheOptions);
builder.Services.AddSingleton<DirectSnapshotQueryCache>();
builder.Services.AddSingleton<KnownTagTracker>();
builder.Services.AddSingleton<KnownTagExistenceProbe>();
builder.Services.AddSingleton<TagStateResponseCache>();
builder.Services.AddSingleton<DirectTagStateCache>();
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
builder.Services.AddSekibanDcbSharedRuntime();
builder.Services.AddSingleton<IEventSubscriptionResolver>(_ =>
    new DefaultOrleansEventSubscriptionResolver(
        "EventStreamProvider",
        "AllEvents",
        Guid.Empty));
builder.Services.AddSingleton<IMultiProjectionEventStatistics, NoOpMultiProjectionEventStatistics>();
builder.Services.AddSingleton(multiProjectionActorOptions);
builder.Services.AddSingleton<IActorObjectAccessor, OrleansActorObjectAccessor>();
builder.Services.AddSingleton<IProjectionActorHostFactory, WasmProjectionActorHostFactory>();
builder.Services.AddTransient<OrleansDcbExecutor>();
builder.Services.AddTransient<ISekibanExecutor>(sp =>
    sp.GetRequiredService<OrleansDcbExecutor>());
builder.Services.AddTransient<ISerializedSekibanDcbExecutor>(sp =>
    sp.GetRequiredService<OrleansDcbExecutor>());

builder.Services.AddWasmtimeProjectionHost(options =>
{
    options.DefaultModulePath = manifest.DefaultModulePath;
    // Pool size: configurable via SEKIBAN_WASM_POOL_SIZE env var.
    // Default=1 keeps 1 idle instance per projector for reuse.
    // Set to 0 for Go (TinyGo) WASM to avoid memory corruption from instance reuse.
    var poolSize = int.TryParse(
        Environment.GetEnvironmentVariable("SEKIBAN_WASM_POOL_SIZE"),
        out var ps) ? ps : 1;
    options.MaxPooledInstancesPerProjector = poolSize;
    options.StaticMemoryMaximumSizeBytes = staticMemoryMaximumSizeBytes;
});
builder.Services.AddWasmTagStateRuntime(options =>
{
    options.Mode = WasmRuntimeMode.Wasm;
    options.WasmModulePath = manifest.DefaultModulePath;
});

builder.Services.AddOpenApi();

// ----------------------------------------------------------------------------
// Materialized view runtime (opt-in).
// When (a) the manifest declares at least one MV and (b) the
// `DcbMaterializedViewPostgres` connection string is configured, wire:
//   - Sekiban.Dcb.MaterializedView (base + options)
//   - Sekiban.Dcb.MaterializedView.Postgres (event-store-reading catch-up executor + registry
//     store, WITH the hosted MvCatchUpWorker enabled — see registerHostedWorker:true below).
//   - Sekiban.Dcb.MaterializedView.Orleans (MaterializedViewGrain activation + stream
//     subscription — receives events if any publisher pushes to the Orleans stream).
//   - WasmtimeMaterializedViewExecutor + one WasmBackedMaterializedViewProjector per manifest
//     entry. The shim implements IMaterializedViewProjector so the existing
//     `MaterializedViewGrain` + `PostgresMvExecutor` treat it like a CLR projector.
//
// Catch-up in this wiring is driven by BOTH paths: the Postgres hosted worker polls the event
// store (guaranteeing progress even without an Orleans event publisher), while the Orleans
// grain takes over when events arrive on `EventStreamProvider`. The two paths coordinate via
// MvRegistryStore positions so no double-apply occurs.
// ----------------------------------------------------------------------------
WasmMaterializedViewExtensions.ValidateModulePathAlignment(
    manifest.DefaultModulePath,
    manifest.MaterializedViews.Select(mv => (mv.ViewName, mv.ViewVersion, (string?)mv.ModulePath)));

var wasmMvRegistrations = materializedViewRequested
    ? manifest.MaterializedViews
        .Select(mv => new WasmMvApplyHostRegistration(mv.ViewName, mv.ViewVersion, mv.LogicalTables.ToList()))
        .ToList()
    : new List<WasmMvApplyHostRegistration>();
var materializedViewEnabled = materializedViewRequested
    && builder.Services.AddSekibanWasmMaterializedViewRuntime(
        builder.Configuration,
        manifest.DefaultModulePath,
        wasmMvRegistrations);
if (!materializedViewRequested)
{
    Console.WriteLine("SEKIBAN_PROJECTION_MODE=memory-only: skipping materialized-view runtime registration.");
}

var app = builder.Build();
if (storageConfiguration.RequiresRelationalMigration)
{
    await app.MigrateSekibanDcbDatabaseAsync();
}

app.MapOpenApi();

// The WASM runtime host is intentionally generic: its only contract is (a) run WASM modules and
// (b) expose the serialized Sekiban transport endpoints. Materialized view read APIs belong in
// the caller-owned ClientApi (which can talk to the MV Postgres using whatever native driver
// the host language prefers, e.g. sqlx / tokio-postgres on the Rust side).

app.MapGet("/", () => Results.Ok(new
{
    runtime = "Sekiban WASM Runtime Host",
    databaseType,
    sqliteDatabasePath,
    waitForSortableUniqueIdTimeout,
    queryResponseTimeout,
    directSnapshotQueryEnabled,
    tagStateFastPathEnabled,
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
    directSnapshotQueryEnabled,
    tagStateFastPathEnabled,
    manifest.DefaultModulePath
}));

if (enableProjectionStatusEndpoint && projectionModeEnabled)
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

// Memory diagnostic: report serialized state size per multi-projector.
app.MapGet("/api/sekiban/memory-stats", async (HttpContext http) =>
{
    var clusterClient = http.RequestServices.GetRequiredService<IClusterClient>();
    var serviceIdProvider = http.RequestServices.GetRequiredService<IServiceIdProvider>();
    var serviceId = serviceIdProvider.GetCurrentServiceId();
    var results = new List<object>();

    foreach (var projector in manifest.Projectors)
    {
        try
        {
            var grainKey = ServiceIdGrainKey.Build(serviceId, projector.ProjectorName);
            var grain = clusterClient.GetGrain<IMultiProjectionGrain>(grainKey);
            var status = await grain.GetStatusAsync();
            results.Add(new
            {
                projector.ProjectorName,
                status.EventsProcessed,
            });
        }
        catch (Exception ex)
        {
            results.Add(new { projector.ProjectorName, error = ex.Message });
        }
    }

    // Process memory
    var process = System.Diagnostics.Process.GetCurrentProcess();
    return Results.Ok(new
    {
        processRssMB = process.WorkingSet64 / 1024 / 1024,
        projectors = results
    });
});

InstanceEndpoints.Map(app);

app.MapPost("/api/sekiban/serialized/tag-state", async (HttpContext http, TagStateRequest request) =>
{
    try
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

    // Fast path: if this tag has never had events committed, return empty state immediately.
    // This avoids Orleans grain activation and WASM instance creation for brand-new tags.
    if (ResolveTagStateFastPathEnabled(http.RequestServices.GetRequiredService<IConfiguration>()))
    {
        var tagProbe = http.RequestServices.GetRequiredService<KnownTagExistenceProbe>();
        var tagGroupContent = $"{tagStateId.TagGroup}:{tagStateId.TagContent}";
        if (await tagProbe.ProbeAsync(tagGroupContent) == KnownTagExistence.Missing)
        {
            var projectorVersionResult = domainTypes.TagProjectorTypes.GetProjectorVersion(tagStateId.TagProjectorName);
            var projectorVersion = projectorVersionResult.IsSuccess ? projectorVersionResult.GetValue() : string.Empty;
            return Results.Ok(new SerializableTagState(
                Array.Empty<byte>(),
                0,
                string.Empty,
                tagStateId.TagGroup,
                tagStateId.TagContent,
                tagStateId.TagProjectorName,
                nameof(EmptyTagStatePayload),
                projectorVersion));
        }
    }

    // Delegate to TagStateGrain — Orleans handles caching, delta replay, and concurrency.
    var clusterClient = http.RequestServices.GetRequiredService<IClusterClient>();
    var serviceIdProvider = http.RequestServices.GetRequiredService<IServiceIdProvider>();
    var grainKey = ServiceIdGrainKey.Build(serviceIdProvider.GetCurrentServiceId(), tagStateId.GetTagStateId());
    var grain = clusterClient.GetGrain<ITagStateGrain>(grainKey);
    var tagState = await grain.GetStateAsync();

    // Safety net: WASM projectors don't embed C# type names, so the accumulated
    // TagPayloadName may be missing or "EmptyTagStatePayload" for non-empty payloads.
    // Override with the manifest-inferred name when needed.
    if (tagState.Payload.Length > 0 &&
        (string.IsNullOrEmpty(tagState.TagPayloadName) ||
         tagState.TagPayloadName == nameof(EmptyTagStatePayload)))
    {
        tagState = tagState with
        {
            TagPayloadName = SekibanRuntimeManifest.InferTagPayloadName(tagStateId.TagProjectorName)
        };
    }

    // Projectors whose payload is a discriminated union (e.g. ClassRoomProjector returns either
    // `AvailableClassRoomState` or `FilledClassRoomState`) cannot be represented by a single
    // inferred type name. The WASM side serializes these as `ClassRoomProjectorSnapshot`
    // (`stateKind` + `availableState` + `filledState`). Unwrap here so the payload that crosses
    // the wire matches a real CLR tag-state type name the client can deserialize.
    tagState = UnwrapDiscriminatedTagPayload(tagState);

    return Results.Ok(tagState);
    }
    catch (TimeoutException ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status504GatewayTimeout);
    }
    catch (OperationCanceledException)
    {
        return Results.StatusCode(499);
    }
    catch (Exception ex)
    {
        var logger = http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("TagState");
        logger.LogError(ex, "Unhandled error in tag-state endpoint for {TagStateId}", request.TagStateId);
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapPost("/api/sekiban/serialized/tag-latest-sortable", async (
    HttpContext http,
    TagLatestSortableRequest request) =>
{
    // Fast path: if the tag has never had events committed, it doesn't exist.
    var tagStateFastPathEnabled = ResolveTagStateFastPathEnabled(http.RequestServices.GetRequiredService<IConfiguration>());
    var tagProbe = http.RequestServices.GetRequiredService<KnownTagExistenceProbe>();
    if (tagStateFastPathEnabled &&
        await tagProbe.ProbeAsync(request.Tag) == KnownTagExistence.Missing)
    {
        return Results.Ok(new TagLatestSortableResponse(false, string.Empty));
    }

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
    if (!string.IsNullOrWhiteSpace(lastSortableUniqueId))
    {
        tagProbe.MarkTagsAsWritten([request.Tag]);
    }

    return Results.Ok(new TagLatestSortableResponse(
        !string.IsNullOrWhiteSpace(lastSortableUniqueId),
        lastSortableUniqueId));
});

app.MapPost("/api/sekiban/serialized/commit", async (
    HttpContext http,
    SerializedCommitRequest request,
    CancellationToken ct) =>
{
    try
    {
        var executor = http.RequestServices.GetRequiredService<ISerializedSekibanDcbExecutor>();
        var result = await executor.CommitSerializableEventsAsync(request, ct);
        if (!result.IsSuccess)
        {
            return Results.BadRequest(new { error = result.GetException().Message });
        }

        // Track committed tags so the tag-state fast-path knows which tags have events.
        var committedTags = request.EventCandidates.SelectMany(static e => e.Tags).Distinct().ToList();
        var tagProbe = http.RequestServices.GetRequiredService<KnownTagExistenceProbe>();
        tagProbe.MarkTagsAsWritten(committedTags);

        return Results.Ok(result.GetValue());
    }
    catch (TimeoutException ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status504GatewayTimeout);
    }
    catch (OperationCanceledException)
    {
        return Results.StatusCode(499);
    }
    catch (Exception ex)
    {
        var logger = http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Commit");
        logger.LogError(ex, "Unhandled error in commit endpoint");
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status500InternalServerError);
    }
});

// Serialized query endpoints are the only callers that activate the MultiProjectionGrain
// (which, in turn, loads the projection WASM instance and holds its ~36 MB linear memory).
// In materialized-view-only mode we leave them unmapped so activation never happens — the
// benchmark driver treats missing endpoints as an expected signal for that mode.
if (projectionModeEnabled)
{
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
}
else
{
    static IResult QueryDisabled() => Results.Json(
        new { error = "MultiProjection disabled via SEKIBAN_PROJECTION_MODE=materialized-view-only." },
        statusCode: StatusCodes.Status503ServiceUnavailable);

    app.MapPost("/api/sekiban/serialized/query", () => QueryDisabled());
    app.MapPost("/api/sekiban/serialized/list-query", () => QueryDisabled());
}

app.Run();

// WASM projectors that return a discriminated-union payload serialize their state through a
// wrapper JSON with a `stateKind` discriminator (e.g. ClassRoomProjectorSnapshot). Sekiban
// 10.2.0 added `SerializableTagState.ActualPayloadName` specifically for this case — the
// TagStateGrain / GeneralTagStateActor already sets it from `Payload.GetType().Name` on the
// CLR side, but WASM projectors don't embed CLR type names, so we patch it in here. The
// receiver resolves the payload through `ResolvedPayloadName` which prefers ActualPayloadName
// over the manifest-inferred TagPayloadName.
static SerializableTagState UnwrapDiscriminatedTagPayload(SerializableTagState tagState)
{
    if (tagState.Payload.Length == 0 ||
        !string.Equals(tagState.TagProjector, "ClassRoomProjector", StringComparison.Ordinal))
    {
        return tagState;
    }

    try
    {
        using var document = System.Text.Json.JsonDocument.Parse(tagState.Payload);
        if (!document.RootElement.TryGetProperty("stateKind", out var kindElement))
        {
            return tagState;
        }

        var kind = kindElement.GetString();
        var (unwrappedProperty, typeName) = kind switch
        {
            "available" => ("availableState", "AvailableClassRoomState"),
            "filled" => ("filledState", "FilledClassRoomState"),
            _ => (string.Empty, string.Empty)
        };

        if (string.IsNullOrEmpty(unwrappedProperty) ||
            !document.RootElement.TryGetProperty(unwrappedProperty, out var inner) ||
            inner.ValueKind == System.Text.Json.JsonValueKind.Null)
        {
            return tagState;
        }

        var innerBytes = System.Text.Encoding.UTF8.GetBytes(inner.GetRawText());
        return tagState with
        {
            Payload = innerBytes,
            ActualPayloadName = typeName
        };
    }
    catch
    {
        return tagState;
    }
}

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

    var directReplayResult = await TryExecuteDirectReplayQueryAsync(
        http,
        manifest,
        projectorName,
        request,
        isListQuery,
        logger,
        ct);
    if (directReplayResult is not null)
    {
        return directReplayResult;
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

static async Task<IResult?> TryExecuteDirectReplayQueryAsync(
    HttpContext http,
    SekibanRuntimeManifest manifest,
    string projectorName,
    SerializedQueryRequest request,
    bool isListQuery,
    ILogger logger,
    CancellationToken ct)
{
    if (!ResolveDirectSnapshotQueryEnabled(http.RequestServices.GetRequiredService<IConfiguration>()))
    {
        return null;
    }

    var storageProvider = RuntimeHostStorageConfigurationResolver.ResolveProvider(
        http.RequestServices.GetRequiredService<IConfiguration>());
    if (storageProvider is not RuntimeHostStorageProvider.Sqlite
        and not RuntimeHostStorageProvider.Postgres)
    {
        return null;
    }

    if (http.RequestServices.GetService<IEventStore>() is not { } eventStore)
    {
        return null;
    }

    var projector = manifest.Projectors.FirstOrDefault(x =>
        string.Equals(x.ProjectorName, projectorName, StringComparison.Ordinal));
    if (projector is null)
    {
        return null;
    }

    var cache = http.RequestServices.GetRequiredService<DirectSnapshotQueryCache>();
    var cacheEntry = cache.GetOrAdd(projectorName);
    var hostFactory = http.RequestServices.GetRequiredService<IProjectionActorHostFactory>();

    // Direct replay queries duplicate all projection state in-process alongside Orleans grains.
    // This causes WasmServer memory to grow unboundedly (13+ GB at 300K events).
    // Disable direct replay to eliminate the duplication — Orleans grains manage their own state
    // with snapshot persistence and incremental catch-up, which is more memory-efficient.
    // DirectSnapshotQuery (below) is still available for fast reads from persisted snapshots.
    return null;

    // Note: The code below is retained but unreachable. It can be re-enabled by removing
    // the early return above if direct replay is needed for specific scenarios.
    await cacheEntry.Gate.WaitAsync(ct);
    try
    {
        if (!cacheEntry.HasProjectorVersion(projector.ProjectorVersion))
        {
            cacheEntry.Replace(
                hostFactory.Create(projectorName),
                metadata: null,
                projector.ProjectorVersion,
                lastSortableUniqueId: null,
                eventsProcessed: 0);
        }

        string? sinceSortableUniqueId = cacheEntry.Metadata?.UnsafeLastSortableUniqueId;
        SortableUniqueId? since = string.IsNullOrWhiteSpace(sinceSortableUniqueId)
            ? null
            : new SortableUniqueId(sinceSortableUniqueId);
        var eventsResult = await eventStore.ReadAllSerializableEventsAsync(since);
        if (!eventsResult.IsSuccess)
        {
            return Results.BadRequest(new { error = eventsResult.GetException().Message });
        }

        var newEvents = eventsResult.GetValue().ToList();
        logger.LogInformation(
            "Direct replay query sync: QueryType={QueryType}, Projector={ProjectorName}, NewEvents={NewEvents}, Since={SinceSortableUniqueId}",
            request.QueryType,
            projectorName,
            newEvents.Count,
            sinceSortableUniqueId ?? "<beginning>");

        if (newEvents.Count > 0)
        {
            await cacheEntry.Host!.AddSerializableEventsAsync(newEvents, finishedCatchUp: true);
        }

        var metadataResult = await cacheEntry.Host!.GetStateMetadataAsync(includeUnsafe: true);
        if (!metadataResult.IsSuccess)
        {
            return Results.BadRequest(new { error = metadataResult.GetException().Message });
        }

        var metadata = metadataResult.GetValue();
        cacheEntry.UpdateMetadata(
            metadata,
            metadata.UnsafeLastSortableUniqueId,
            metadata.UnsafeVersion);

        var parameter = new SerializableQueryParameter
        {
            QueryTypeName = request.QueryType,
            QueryAssemblyVersion = manifest.QueryAssemblyVersion,
            CompressedQueryJson = await CompressStringAsync(request.QueryParamsJson)
        };

        if (!isListQuery)
        {
            var result = await cacheEntry.Host.ExecuteQueryAsync(
                parameter,
                metadata.SafeVersion,
                metadata.SafeLastSortableUniqueId,
                string.IsNullOrWhiteSpace(metadata.SafeLastSortableUniqueId)
                    ? null
                    : new SortableUniqueId(metadata.SafeLastSortableUniqueId).GetDateTime(),
                metadata.UnsafeVersion);
            if (!result.IsSuccess)
            {
                return Results.BadRequest(new { error = result.GetException().Message });
            }

            var resultJson = await DecompressToStringAsync(result.GetValue().CompressedResultJson);
            return Results.Ok(new SerializedQueryResponse(resultJson));
        }

        var listResult = await cacheEntry.Host.ExecuteListQueryAsync(
            parameter,
            metadata.SafeVersion,
            metadata.SafeLastSortableUniqueId,
            string.IsNullOrWhiteSpace(metadata.SafeLastSortableUniqueId)
                ? null
                : new SortableUniqueId(metadata.SafeLastSortableUniqueId).GetDateTime(),
            metadata.UnsafeVersion);
        if (!listResult.IsSuccess)
        {
            return Results.BadRequest(new { error = listResult.GetException().Message });
        }

        var value = listResult.GetValue();
        var itemsJson = await DecompressToStringAsync(value.CompressedItemsJson);
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

static async Task<IResult?> TryExecuteDirectSnapshotQueryAsync(
    HttpContext http,
    SekibanRuntimeManifest manifest,
    string projectorName,
    SerializedQueryRequest request,
    bool isListQuery,
    ILogger logger,
    CancellationToken ct)
{
    if (!ResolveDirectSnapshotQueryEnabled(http.RequestServices.GetRequiredService<IConfiguration>()))
    {
        return null;
    }

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
    var recordLastSortableUniqueId = record.LastSortableUniqueId ?? string.Empty;
    if (!string.IsNullOrWhiteSpace(request.WaitForSortableUniqueId) &&
        string.Compare(recordLastSortableUniqueId, request.WaitForSortableUniqueId, StringComparison.Ordinal) < 0)
    {
        logger.LogInformation(
            "Direct snapshot query skipped for {ProjectorName}: persisted state {PersistedSortableUniqueId} is older than requested {RequestedSortableUniqueId}",
            projectorName,
            recordLastSortableUniqueId,
            request.WaitForSortableUniqueId);
        return null;
    }

    var cache = http.RequestServices.GetRequiredService<DirectSnapshotQueryCache>();
    cache.Prune(projectorName);
    var cacheEntry = cache.GetOrAdd(projectorName);
    var hostFactory = http.RequestServices.GetRequiredService<IProjectionActorHostFactory>();
    await cacheEntry.Gate.WaitAsync(ct);
    try
    {
        cacheEntry.Touch();
        var stopwatch = Stopwatch.StartNew();
        logger.LogInformation(
            "Direct snapshot query start: QueryType={QueryType}, Projector={ProjectorName}, IsListQuery={IsListQuery}, EventsProcessed={EventsProcessed}, LastSortableUniqueId={LastSortableUniqueId}",
            request.QueryType,
            projectorName,
            isListQuery,
            record.EventsProcessed,
            recordLastSortableUniqueId);

        if (!cacheEntry.Matches(projector.ProjectorVersion, recordLastSortableUniqueId, record.EventsProcessed))
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
            var response = Results.Ok(new SerializedQueryResponse(resultJson));
            if (cache.ShouldResetActiveEntryOnMemoryPressure())
            {
                logger.LogWarning(
                    "Direct snapshot cache reset under RSS pressure: Projector={ProjectorName}, RssMB={RssMB}",
                    projectorName,
                    cache.GetCurrentRssBytes() / 1024 / 1024);
                cacheEntry.DisposeAndReset();
            }

            cache.Prune(projectorName);
            return response;
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
        var listResponse = Results.Ok(new SerializedListQueryResponse(
            ItemsJson: itemsJson,
            TotalCount: value.TotalCount,
            TotalPages: value.TotalPages,
            CurrentPage: value.CurrentPage,
            PageSize: value.PageSize));
        if (cache.ShouldResetActiveEntryOnMemoryPressure())
        {
            logger.LogWarning(
                "Direct snapshot cache reset under RSS pressure: Projector={ProjectorName}, RssMB={RssMB}",
                projectorName,
                cache.GetCurrentRssBytes() / 1024 / 1024);
            cacheEntry.DisposeAndReset();
        }

        cache.Prune(projectorName);
        return listResponse;
    }
    finally
    {
        cacheEntry.Gate.Release();
    }
}

static bool ResolveDirectSnapshotQueryEnabled(IConfiguration configuration)
{
    string? raw = configuration["SEKIBAN_DIRECT_SNAPSHOT_QUERY_ENABLED"];
    if (string.IsNullOrWhiteSpace(raw))
    {
        return true;
    }

    return raw.Trim().ToLowerInvariant() switch
    {
        "0" or "false" or "no" or "off" => false,
        _ => true
    };
}

static bool ResolveTagStateFastPathEnabled(IConfiguration configuration)
{
    string? raw = configuration["SEKIBAN_TAG_STATE_FAST_PATH_ENABLED"];
    if (string.IsNullOrWhiteSpace(raw))
    {
        return true;
    }

    return raw.Trim().ToLowerInvariant() switch
    {
        "0" or "false" or "no" or "off" => false,
        _ => true
    };
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
                        recordOptional.Value.LastSortableUniqueId ?? string.Empty,
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

static GeneralMultiProjectionActorOptions ResolveGeneralMultiProjectionActorOptions(IConfiguration configuration) =>
    new()
    {
        CatchUpBatchSize = ResolvePositiveInt(
            configuration,
            "Sekiban:MultiProjection:CatchUpBatchSize",
            "SEKIBAN_WASM_MULTIPROJECTION_CATCHUP_BATCH_SIZE",
            200),
        MaxPendingStreamEvents = ResolvePositiveInt(
            configuration,
            "Sekiban:MultiProjection:MaxPendingStreamEvents",
            "SEKIBAN_WASM_MULTIPROJECTION_MAX_PENDING_STREAM_EVENTS",
            5_000),
        ProcessedEventIdCacheSize = ResolvePositiveInt(
            configuration,
            "Sekiban:MultiProjection:ProcessedEventIdCacheSize",
            "SEKIBAN_WASM_MULTIPROJECTION_EVENT_ID_CACHE_SIZE",
            50_000),
        ForceGcAfterLargeSnapshotPersist = true,
        LargeSnapshotGcThresholdBytes = ResolvePositiveLong(
            configuration,
            "Sekiban:MultiProjection:LargeSnapshotGcThresholdBytes",
            "SEKIBAN_WASM_MULTIPROJECTION_LARGE_SNAPSHOT_GC_THRESHOLD_BYTES",
            5_000_000)
    };

static DirectSnapshotQueryCacheOptions ResolveDirectSnapshotQueryCacheOptions(IConfiguration configuration) =>
    new()
    {
        MaxEntries = ResolvePositiveInt(
            configuration,
            "Sekiban:DirectSnapshotCache:MaxEntries",
            "SEKIBAN_DIRECT_SNAPSHOT_CACHE_MAX_ENTRIES",
            3),
        IdleEntryLifetime = TimeSpan.FromSeconds(ResolveNonNegativeInt(
            configuration,
            "Sekiban:DirectSnapshotCache:IdleSeconds",
            "SEKIBAN_DIRECT_SNAPSHOT_CACHE_IDLE_SECONDS",
            45)),
        EvictRssThresholdBytes = ResolveNonNegativeLong(
            configuration,
            "Sekiban:DirectSnapshotCache:EvictRssThresholdBytes",
            "SEKIBAN_DIRECT_SNAPSHOT_CACHE_EVICT_RSS_BYTES",
            3L * 1024 * 1024 * 1024),
        ResetActiveEntryRssThresholdBytes = ResolveNonNegativeLong(
            configuration,
            "Sekiban:DirectSnapshotCache:ResetActiveEntryRssThresholdBytes",
            "SEKIBAN_DIRECT_SNAPSHOT_CACHE_RESET_ACTIVE_RSS_BYTES",
            3_500L * 1024 * 1024)
    };

static ulong? ResolveWasmtimeStaticMemoryMaximumSizeBytes(IConfiguration configuration)
{
    var candidate = configuration["Sekiban:Wasmtime:StaticMemoryMaximumSizeMb"]
        ?? Environment.GetEnvironmentVariable("SEKIBAN_WASMTIME_STATIC_MEMORY_MAX_MB");

    return int.TryParse(candidate, out var configuredMegabytes) && configuredMegabytes > 0
        ? (ulong)configuredMegabytes * 1024UL * 1024UL
        : null;
}

static int ResolvePositiveInt(
    IConfiguration configuration,
    string configKey,
    string environmentVariableName,
    int defaultValue)
{
    var candidate = configuration[configKey] ?? Environment.GetEnvironmentVariable(environmentVariableName);
    return int.TryParse(candidate, out var value) && value > 0
        ? value
        : defaultValue;
}

static long ResolvePositiveLong(
    IConfiguration configuration,
    string configKey,
    string environmentVariableName,
    long defaultValue)
{
    var candidate = configuration[configKey] ?? Environment.GetEnvironmentVariable(environmentVariableName);
    return long.TryParse(candidate, out var value) && value > 0
        ? value
        : defaultValue;
}

static int ResolveNonNegativeInt(
    IConfiguration configuration,
    string configKey,
    string environmentVariableName,
    int defaultValue)
{
    var candidate = configuration[configKey] ?? Environment.GetEnvironmentVariable(environmentVariableName);
    return int.TryParse(candidate, out var value) && value >= 0
        ? value
        : defaultValue;
}

static long ResolveNonNegativeLong(
    IConfiguration configuration,
    string configKey,
    string environmentVariableName,
    long defaultValue)
{
    var candidate = configuration[configKey] ?? Environment.GetEnvironmentVariable(environmentVariableName);
    return long.TryParse(candidate, out var value) && value >= 0
        ? value
        : defaultValue;
}

sealed class WaitForSortableUniqueIdTimeoutOptions
{
    public TimeSpan Timeout { get; init; }
}

sealed class DirectSnapshotQueryCacheOptions
{
    public int MaxEntries { get; init; } = 3;
    public TimeSpan IdleEntryLifetime { get; init; } = TimeSpan.FromSeconds(45);
    public long EvictRssThresholdBytes { get; init; } = 3L * 1024 * 1024 * 1024;
    public long ResetActiveEntryRssThresholdBytes { get; init; } = 3_500L * 1024 * 1024;
}

sealed class DirectSnapshotQueryCache : IDisposable
{
    private readonly DirectSnapshotQueryCacheOptions _options;
    private readonly Func<long> _rssProvider;
    private readonly ConcurrentDictionary<string, DirectSnapshotQueryCacheEntry> _entries =
        new(StringComparer.Ordinal);

    public DirectSnapshotQueryCache(DirectSnapshotQueryCacheOptions options)
        : this(options, rssProvider: null)
    {
    }

    internal DirectSnapshotQueryCache(DirectSnapshotQueryCacheOptions options, Func<long>? rssProvider)
    {
        _options = options;
        _rssProvider = rssProvider ?? GetCurrentProcessRss;
    }

    internal int Count => _entries.Count;

    internal IReadOnlyCollection<string> ProjectorNames => _entries.Keys.ToArray();

    public DirectSnapshotQueryCacheEntry GetOrAdd(string projectorName)
    {
        Prune(projectorName);
        var entry = _entries.GetOrAdd(projectorName, static name => new DirectSnapshotQueryCacheEntry(name));
        entry.Touch();
        return entry;
    }

    public void Prune(string? activeProjectorName = null)
    {
        if (_entries.IsEmpty)
        {
            return;
        }

        long now = Environment.TickCount64;
        if (_options.IdleEntryLifetime > TimeSpan.Zero)
        {
            long idleThreshold = now - (long)_options.IdleEntryLifetime.TotalMilliseconds;
            foreach ((string projectorName, DirectSnapshotQueryCacheEntry entry) in _entries.ToArray())
            {
                if (string.Equals(projectorName, activeProjectorName, StringComparison.Ordinal) ||
                    entry.LastAccessedTicks >= idleThreshold)
                {
                    continue;
                }

                TryRemove(projectorName, entry);
            }
        }

        if (_entries.Count > _options.MaxEntries)
        {
            foreach ((string projectorName, DirectSnapshotQueryCacheEntry entry) in _entries
                         .OrderBy(pair => pair.Value.LastAccessedTicks)
                         .ToArray())
            {
                if (_entries.Count <= _options.MaxEntries)
                {
                    break;
                }

                if (string.Equals(projectorName, activeProjectorName, StringComparison.Ordinal))
                {
                    continue;
                }

                TryRemove(projectorName, entry);
            }
        }

        if (_options.EvictRssThresholdBytes > 0 && _rssProvider() >= _options.EvictRssThresholdBytes)
        {
            foreach ((string projectorName, DirectSnapshotQueryCacheEntry entry) in _entries
                         .OrderBy(pair => pair.Value.LastAccessedTicks)
                         .ToArray())
            {
                if (string.Equals(projectorName, activeProjectorName, StringComparison.Ordinal))
                {
                    continue;
                }

                TryRemove(projectorName, entry);
            }

            if (!_entries.IsEmpty)
            {
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            }
        }
    }

    public bool ShouldResetActiveEntryOnMemoryPressure() =>
        _options.ResetActiveEntryRssThresholdBytes > 0 &&
        _rssProvider() >= _options.ResetActiveEntryRssThresholdBytes;

    public long GetCurrentRssBytes() => _rssProvider();

    public void Dispose()
    {
        foreach (DirectSnapshotQueryCacheEntry entry in _entries.Values)
        {
            entry.Dispose();
        }
    }

    private void TryRemove(string projectorName, DirectSnapshotQueryCacheEntry entry)
    {
        if (!entry.Gate.Wait(0))
        {
            return;
        }

        try
        {
            if (_entries.TryRemove(projectorName, out var evictedEntry))
            {
                evictedEntry.DisposeAndReset();
            }
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    private static long GetCurrentProcessRss() => Process.GetCurrentProcess().WorkingSet64;
}

sealed class DirectSnapshotQueryCacheEntry : IDisposable
{
    public DirectSnapshotQueryCacheEntry(string projectorName) => ProjectorName = projectorName;

    public string ProjectorName { get; }
    public SemaphoreSlim Gate { get; } = new(1, 1);
    public IProjectionActorHost? Host { get; private set; }
    public ProjectionStateMetadata? Metadata { get; private set; }
    public long LastAccessedTicks { get; private set; } = Environment.TickCount64;
    private string? ProjectorVersion { get; set; }
    private string? LastSortableUniqueId { get; set; }
    public long EventsProcessed { get; private set; }

    public bool Matches(string projectorVersion, string? lastSortableUniqueId, long eventsProcessed) =>
        Host is not null
        && string.Equals(ProjectorVersion, projectorVersion, StringComparison.Ordinal)
        && string.Equals(LastSortableUniqueId, lastSortableUniqueId, StringComparison.Ordinal)
        && EventsProcessed == eventsProcessed;

    public bool HasProjectorVersion(string projectorVersion) =>
        Host is not null &&
        string.Equals(ProjectorVersion, projectorVersion, StringComparison.Ordinal);

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
        Touch();
    }

    public void UpdateMetadata(
        ProjectionStateMetadata metadata,
        string? lastSortableUniqueId,
        long eventsProcessed)
    {
        Metadata = metadata;
        LastSortableUniqueId = lastSortableUniqueId;
        EventsProcessed = eventsProcessed;
        Touch();
    }

    public void Touch() => LastAccessedTicks = Environment.TickCount64;

    /// <summary>
    /// Dispose the cached host and reset to empty state.
    /// Called when the host exceeds memory thresholds so future queries fall through to Orleans.
    /// </summary>
    public void DisposeAndReset() => DisposeHost();

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

/// <summary>
/// In-memory cache of tag state snapshots for the direct tag-state path.
/// When a tag state is requested again, the cached snapshot is restored
/// and only events after the snapshot's sortable unique ID are replayed.
/// </summary>
sealed class DirectTagStateCache
{
    private readonly ConcurrentDictionary<string, DirectTagStateCacheEntry> _entries = new(StringComparer.Ordinal);

    public DirectTagStateCacheEntry? TryGet(string tagStateKey) =>
        _entries.TryGetValue(tagStateKey, out var entry) ? entry : null;

    public void Set(string tagStateKey, SerializableTagState cachedState, string lastSortableUniqueId)
    {
        _entries[tagStateKey] = new DirectTagStateCacheEntry(cachedState, lastSortableUniqueId);
    }
}

sealed record DirectTagStateCacheEntry(SerializableTagState CachedState, string LastSortableUniqueId);

/// <summary>
/// Caches SerializableTagState responses by tagStateId (format: "tagGroup:tagContent:projectorName").
/// After a tag-state is computed via Orleans, the result is cached here so subsequent requests
/// for the same tag-state return immediately without grain activation.
/// When a commit writes events for tags, all cache entries matching those tags are invalidated.
///
/// Memory management:
/// - MaxEntries limits the total number of cached entries (default 10,000)
/// - When the limit is reached, the oldest half of entries are evicted
/// - Each weather forecast creates a unique tag that is never accessed again after commit,
///   so these are invalidated and not re-cached
/// </summary>
/// <summary>
/// Version-aware tag-state response cache. Instead of invalidating entries on commit,
/// entries are marked as stale and their freshness is checked against the Orleans grain's
/// latest sortable unique ID on each request. This dramatically reduces WASM accumulator
/// creations: only re-compute when the tag actually has new events.
/// </summary>
sealed class TagStateResponseCache
{
    private const int MaxEntries = 10_000;

    private readonly ConcurrentDictionary<string, TagStateResponseCacheEntry> _entries = new(StringComparer.Ordinal);

    /// <summary>
    /// Try to get a cached entry. Returns true if the entry exists (even if stale).
    /// Caller must check <paramref name="isStale"/> and verify freshness via Orleans
    /// if the entry is stale.
    /// </summary>
    public bool TryGet(string tagStateKey, out SerializableTagState value, out bool isStale)
    {
        if (_entries.TryGetValue(tagStateKey, out var entry))
        {
            entry.LastAccessedTicks = Environment.TickCount64;
            value = entry.State;
            isStale = entry.IsStale;
            return true;
        }
        value = default!;
        isStale = false;
        return false;
    }

    /// <summary>
    /// Cache a freshly computed tag-state result with its sortable unique ID for freshness checks.
    /// </summary>
    public void Set(string tagStateKey, SerializableTagState value, string? lastSortableUniqueId = null)
    {
        if (_entries.Count >= MaxEntries)
        {
            EvictOldest();
        }
        _entries[tagStateKey] = new TagStateResponseCacheEntry(value, lastSortableUniqueId);
    }

    /// <summary>
    /// Mark all cached entries for the given tags as stale (not deleted).
    /// Stale entries can still be returned if Orleans confirms the sortable ID hasn't advanced.
    /// This avoids unnecessary WASM accumulator re-creations when concurrent commits
    /// don't actually affect the queried tag.
    /// </summary>
    public void MarkTagsAsStale(IEnumerable<string> tags)
    {
        foreach (var tag in tags)
        {
            var prefix = tag + ":";
            foreach (var kvp in _entries)
            {
                if (kvp.Key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    kvp.Value.IsStale = true;
                }
            }
        }
    }

    /// <summary>Kept for backward compatibility, delegates to MarkTagsAsStale.</summary>
    public void InvalidateForTags(IEnumerable<string> tags) => MarkTagsAsStale(tags);

    private void EvictOldest()
    {
        var entries = _entries.ToArray();
        var sorted = entries.OrderBy(e => e.Value.LastAccessedTicks).ToArray();
        var removeCount = sorted.Length / 2;
        for (var i = 0; i < removeCount; i++)
        {
            _entries.TryRemove(sorted[i].Key, out _);
        }
    }

    private sealed class TagStateResponseCacheEntry
    {
        public SerializableTagState State { get; }
        public string? LastSortableUniqueId { get; }
        public long LastAccessedTicks { get; set; }
        public bool IsStale { get; set; }

        public TagStateResponseCacheEntry(SerializableTagState state, string? lastSortableUniqueId)
        {
            State = state;
            LastSortableUniqueId = lastSortableUniqueId;
            LastAccessedTicks = Environment.TickCount64;
            IsStale = false;
        }
    }
}

/// <summary>
/// Tracks tags that have had events committed through this WasmServer instance.
/// Used as a fast-path in the tag-state endpoint: if a tag is NOT in this tracker,
/// it has never had events written (within this server's lifetime), so we can return
/// an empty state immediately without activating Orleans grains or creating WASM instances.
/// This eliminates ~180k unnecessary grain activations during weather bulk benchmarks.
/// </summary>
sealed class KnownTagTracker
{
    private readonly ConcurrentDictionary<string, byte> _knownTags = new(StringComparer.Ordinal);

    /// <summary>
    /// Returns true if the given tag (format: "tagGroup:tagContent") has had events committed.
    /// </summary>
    public bool HasKnownEvents(string tagGroupContent) => _knownTags.ContainsKey(tagGroupContent);

    /// <summary>
    /// Marks the given tags as having had events committed.
    /// Called after successful commit to track which tags have events.
    /// </summary>
    public void MarkTagsAsWritten(IEnumerable<string> tags)
    {
        foreach (var tag in tags)
        {
            _knownTags.TryAdd(tag, 0);
        }
    }
}

// SharedTagStateProcessor has been removed. Tag-state is now handled by
// TagStateGrain via Orleans, which provides persistent caching, delta replay,
// projector version validation, and distributed concurrency management.
// See: https://github.com/J-Tech-Japan/SekibanWasmRuntime/issues/83
#if false
sealed class SharedTagStateProcessor_REMOVED : IDisposable
{
    private readonly ConcurrentDictionary<string, GateEntry> _gates = new();
    private readonly ConcurrentDictionary<string, SerializableTagState> _stateCache = new();
    private const int MaxCacheEntries = 10_000;

    private readonly ITagStateProjectionPrimitive _primitive;
    private readonly IEventStore _eventStore;
    private readonly IActorObjectAccessor _actorAccessor;
    private readonly DcbDomainTypes _domainTypes;
    private readonly WasmProjectorRegistry _registry;
    private readonly IReadOnlyDictionary<string, HashSet<string>> _allowedTagEventTypesByProjector;
    private readonly ILogger _logger;

    public SharedTagStateProcessor(
        ITagStateProjectionPrimitive primitive,
        IEventStore eventStore,
        IActorObjectAccessor actorAccessor,
        WasmProjectorRegistry registry,
        DcbDomainTypes domainTypes,
        IConfiguration configuration,
        ILoggerFactory loggerFactory)
    {
        _primitive = primitive;
        _eventStore = eventStore;
        _actorAccessor = actorAccessor;
        _domainTypes = domainTypes;
        _registry = registry;
        _allowedTagEventTypesByProjector = LoadAllowedTagEventTypes(configuration);
        _logger = loggerFactory.CreateLogger("SharedTagState");
    }

    public async Task<SerializableTagState?> GetTagStateAsync(TagStateId tagStateId)
    {
        var projectorName = tagStateId.TagProjectorName;
        if (_registry.TryGet(projectorName) is null)
        {
            _logger.LogDebug("Projector {ProjectorName} not in manifest.", projectorName);
            return null;
        }

        var cacheKey = tagStateId.GetTagStateId();
        var gateEntry = AcquireGate(cacheKey);
        await gateEntry.Gate.WaitAsync();
        try
        {
            var cachedState = _stateCache.GetValueOrDefault(cacheKey);

            // Get latest sortable unique ID from Orleans
            string? latestSortableUniqueId = null;
            var tagActorId = $"{tagStateId.TagGroup}:{tagStateId.TagContent}";
            var tagActorResult = await _actorAccessor.GetActorAsync<ITagConsistentActorCommon>(tagActorId);
            if (tagActorResult.IsSuccess)
            {
                var latestResult = await tagActorResult.GetValue().GetLatestSortableUniqueIdAsync();
                if (latestResult.IsSuccess)
                {
                    latestSortableUniqueId = latestResult.GetValue();
                }
            }

            // Cache hit: sortable ID hasn't advanced
            if (cachedState != null &&
                !string.IsNullOrEmpty(cachedState.LastSortedUniqueId) &&
                string.Equals(cachedState.LastSortedUniqueId, latestSortableUniqueId, StringComparison.Ordinal))
            {
                return cachedState;
            }

            // Load delta events (only since last cached state)
            SortableUniqueId? since = null;
            if (cachedState != null &&
                !string.IsNullOrEmpty(cachedState.LastSortedUniqueId) &&
                SortableUniqueId.TryParse(cachedState.LastSortedUniqueId, out var parsedSince))
            {
                since = parsedSince;
            }

            var tag = new FallbackTag(tagStateId.TagGroup, tagStateId.TagContent);
            _logger.LogDebug("SharedTagState: Loading events for {Tag} since={Since}",
                $"{tagStateId.TagGroup}:{tagStateId.TagContent}", since?.ToString() ?? "null");
            var eventsResult = await _eventStore.ReadSerializableEventsByTagAsync(tag, since);
            if (!eventsResult.IsSuccess)
            {
                _logger.LogWarning("Event read failed for {TagStateId}: {Error}",
                    tagStateId, eventsResult.GetException().Message);
                return null;
            }

            var events = eventsResult.GetValue().ToList();
            _logger.LogDebug("SharedTagState: Loaded {EventCount} events for {Tag}, cachedState={HasCached}, latestSortableId={Latest}",
                events.Count,
                $"{tagStateId.TagGroup}:{tagStateId.TagContent}:{tagStateId.TagProjectorName}",
                cachedState != null,
                latestSortableUniqueId ?? "null");

            var deltaPreparation = PrepareDeltaEvents(projectorName, cachedState, events, latestSortableUniqueId);
            if (deltaPreparation.ShortCircuitedState is not null)
            {
                _stateCache[cacheKey] = deltaPreparation.ShortCircuitedState;
                return deltaPreparation.ShortCircuitedState;
            }

            var eventsToApply = deltaPreparation.EventsToApply;

            // Create fresh accumulator (gets pooled instance if available).
            using var accumulator = _primitive.CreateAccumulator(tagStateId);

            // Pure function: RestoreState → ApplyEvents → SerializeState
            var applyStateResult = accumulator.ApplyState(cachedState);
            _logger.LogDebug("SharedTagState: ApplyState(cached={HasCached}) returned {Result}",
                cachedState != null, applyStateResult);
            if (!applyStateResult)
            {
                _logger.LogWarning("ApplyState failed for {TagStateId}", tagStateId);
                return null;
            }

            if (eventsToApply.Count > 0)
            {
                var applyEventsResult = accumulator.ApplyEvents(eventsToApply, latestSortableUniqueId);
                _logger.LogDebug("SharedTagState: ApplyEvents({Count} events) returned {Result}",
                    eventsToApply.Count, applyEventsResult);
                if (!applyEventsResult)
                {
                    _logger.LogWarning("ApplyEvents failed for {TagStateId}", tagStateId);
                    return null;
                }
            }

            var rawState = accumulator.GetSerializedState();

            // Override metadata from tagStateId (accumulator may pick up wrong metadata
            // from multi-tagged events, e.g., Room events tagged with both Room and Reservation).
            var payloadName = rawState.TagPayloadName;
            if (rawState.Payload.Length > 0 &&
                (string.Equals(payloadName, nameof(EmptyTagStatePayload), StringComparison.Ordinal) ||
                 string.IsNullOrWhiteSpace(payloadName)) &&
                projectorName.EndsWith("Projector", StringComparison.Ordinal))
            {
                payloadName = projectorName[..^"Projector".Length] + "State";
            }

            var newState = rawState with
            {
                LastSortedUniqueId = string.IsNullOrWhiteSpace(latestSortableUniqueId)
                    ? rawState.LastSortedUniqueId
                    : latestSortableUniqueId,
                TagGroup = tagStateId.TagGroup,
                TagContent = tagStateId.TagContent,
                TagProjector = projectorName,
                TagPayloadName = payloadName
            };

            // Cache the result for incremental replay on next request
            if (_stateCache.Count >= MaxCacheEntries)
            {
                var keys = _stateCache.Keys.Take(_stateCache.Count / 2).ToList();
                foreach (var k in keys)
                {
                    _stateCache.TryRemove(k, out _);
                    TryRemoveGateIfIdle(k);
                }
            }
            _stateCache[cacheKey] = newState;

            return newState;
        }
        finally
        {
            gateEntry.Gate.Release();
            ReleaseGate(cacheKey, gateEntry);
        }
    }

    /// <summary>
    /// Cached states already validate against the latest sortable unique ID and replay deltas.
    /// Keep warm entries across commits so repeated reads do not fall back to full replays.
    /// </summary>
    public void InvalidateForTags(IEnumerable<string> tags)
    {
        _ = tags;
    }

    public void Dispose()
    {
        foreach (var (_, gateEntry) in _gates)
        {
            gateEntry.Gate.Dispose();
        }
        _gates.Clear();
        _stateCache.Clear();
    }

    private GateEntry AcquireGate(string cacheKey)
    {
        while (true)
        {
            var gateEntry = _gates.GetOrAdd(cacheKey, _ => new GateEntry());
            Interlocked.Increment(ref gateEntry.ReferenceCount);

            if (_gates.TryGetValue(cacheKey, out var currentEntry) && ReferenceEquals(currentEntry, gateEntry))
            {
                return gateEntry;
            }

            ReleaseGate(cacheKey, gateEntry);
        }
    }

    private void ReleaseGate(string cacheKey, GateEntry gateEntry)
    {
        if (Interlocked.Decrement(ref gateEntry.ReferenceCount) != 0)
        {
            return;
        }

        if (_stateCache.ContainsKey(cacheKey))
        {
            return;
        }

        if (_gates.TryRemove(new KeyValuePair<string, GateEntry>(cacheKey, gateEntry)))
        {
            gateEntry.Gate.Dispose();
        }
    }

    private void TryRemoveGateIfIdle(string cacheKey)
    {
        if (!_gates.TryGetValue(cacheKey, out var gateEntry))
        {
            return;
        }

        if (Volatile.Read(ref gateEntry.ReferenceCount) != 0)
        {
            return;
        }

        if (_gates.TryRemove(new KeyValuePair<string, GateEntry>(cacheKey, gateEntry)))
        {
            gateEntry.Gate.Dispose();
        }
    }

    private sealed class GateEntry
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public int ReferenceCount;
    }

    private DeltaPreparation PrepareDeltaEvents(
        string projectorName,
        SerializableTagState? cachedState,
        List<SerializableEvent> events,
        string? latestSortableUniqueId)
    {
        if (cachedState is null || events.Count == 0)
        {
            return new DeltaPreparation(events, null);
        }

        if (_allowedTagEventTypesByProjector.TryGetValue(projectorName, out var allowedEventTypes))
        {
            var allowlistedEvents = events
                .Where(e => allowedEventTypes.Contains(e.EventPayloadName))
                .ToList();
            if (allowlistedEvents.Count == 0)
            {
                var advancedState = string.IsNullOrWhiteSpace(latestSortableUniqueId)
                    ? cachedState
                    : cachedState with { LastSortedUniqueId = latestSortableUniqueId };
                return new DeltaPreparation([], advancedState);
            }

            return new DeltaPreparation(allowlistedEvents, null);
        }

        var projectorResult = _domainTypes.TagProjectorTypes.GetProjectorFunction(projectorName);
        if (!projectorResult.IsSuccess)
        {
            return new DeltaPreparation(events, null);
        }

        ITagStatePayload currentPayload = RestoreCachedPayload(cachedState);
        var relevantEvents = new List<SerializableEvent>(events.Count);
        var projector = projectorResult.GetValue();

        foreach (var serializableEvent in events)
        {
            var eventResult = serializableEvent.ToEvent(_domainTypes.EventTypes);
            if (!eventResult.IsSuccess)
            {
                return new DeltaPreparation(events, null);
            }

            var nextPayload = projector(currentPayload, eventResult.GetValue());
            if (!ReferenceEquals(nextPayload, currentPayload))
            {
                relevantEvents.Add(serializableEvent);
            }

            currentPayload = nextPayload;
        }

        if (relevantEvents.Count == 0)
        {
            var advancedState = string.IsNullOrWhiteSpace(latestSortableUniqueId)
                ? cachedState
                : cachedState with { LastSortedUniqueId = latestSortableUniqueId };
            return new DeltaPreparation([], advancedState);
        }

        return new DeltaPreparation(relevantEvents, null);
    }

    private ITagStatePayload RestoreCachedPayload(SerializableTagState cachedState)
    {
        if (cachedState.Payload.Length == 0 ||
            string.IsNullOrWhiteSpace(cachedState.TagPayloadName) ||
            string.Equals(cachedState.TagPayloadName, nameof(EmptyTagStatePayload), StringComparison.Ordinal))
        {
            return new EmptyTagStatePayload();
        }

        var deserializeResult = _domainTypes.TagStatePayloadTypes.DeserializePayload(
            cachedState.TagPayloadName,
            cachedState.Payload);
        return deserializeResult.IsSuccess
            ? deserializeResult.GetValue()
            : new EmptyTagStatePayload();
    }

    private static IReadOnlyDictionary<string, HashSet<string>> LoadAllowedTagEventTypes(IConfiguration configuration)
    {
        var allowed = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["RoomProjector"] = new HashSet<string>(
                ["RoomCreated", "RoomUpdated", "RoomDeactivated", "RoomReactivated"],
                StringComparer.Ordinal)
        };

        var section = configuration.GetSection("WASM_RUNTIME_ALLOWED_TAG_EVENT_TYPES");
        foreach (var child in section.GetChildren())
        {
            var values = child.Value?
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .ToHashSet(StringComparer.Ordinal);
            if (values is { Count: > 0 })
            {
                allowed[child.Key] = values;
            }
        }

        return allowed;
    }

    private readonly record struct DeltaPreparation(
        List<SerializableEvent> EventsToApply,
        SerializableTagState? ShortCircuitedState);
}
#endif
