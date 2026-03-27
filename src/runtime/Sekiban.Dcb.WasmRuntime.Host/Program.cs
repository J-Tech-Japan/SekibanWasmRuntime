using System.IO.Compression;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Configuration;
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
var databaseType = ResolveDatabaseType(builder.Configuration);
var connectionString = string.Equals(databaseType, "sqlite", StringComparison.OrdinalIgnoreCase)
    ? null
    : ResolveConnectionString(builder.Configuration);
var sqliteDatabasePath = string.Equals(databaseType, "sqlite", StringComparison.OrdinalIgnoreCase)
    ? ResolveSqliteDatabasePath(builder.Configuration)
    : null;
var waitForSortableUniqueIdTimeout = ResolveWaitForSortableUniqueIdTimeout(builder.Configuration);
var queryResponseTimeout = ResolveQueryResponseTimeout(builder.Configuration);

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
builder.Services.AddSingleton<NativeKanyushaListSnapshotCache>();
builder.Services.AddSingleton(new WaitForSortableUniqueIdTimeoutOptions
{
    Timeout = waitForSortableUniqueIdTimeout
});
builder.Services.Configure<MessagingOptions>(options =>
{
    options.ResponseTimeout = queryResponseTimeout;
});

builder.Services.AddSingleton<IServiceIdProvider, DefaultServiceIdProvider>();
if (string.Equals(databaseType, "sqlite", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSekibanDcbSqlite(sqliteDatabasePath!);
}
else
{
    builder.Services.AddSekibanDcbPostgres(connectionString!);
}
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
if (string.Equals(databaseType, "postgres", StringComparison.OrdinalIgnoreCase))
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
            ResolveDatabaseType(configuration),
            request.WaitForSortableUniqueId!,
            waitTimeout,
            logger,
            ct);
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
    var databaseType = ResolveDatabaseType(http.RequestServices.GetRequiredService<IConfiguration>());
    if (!string.Equals(databaseType, "sqlite", StringComparison.OrdinalIgnoreCase))
    {
        return null;
    }

    if (!string.IsNullOrWhiteSpace(request.WaitForSortableUniqueId))
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
    var nativeKanyushaResult = await TryExecuteNativeKanyushaListSnapshotQueryAsync(
        http,
        projectorName,
        request,
        isListQuery,
        record,
        logger,
        ct);
    if (nativeKanyushaResult is not null)
    {
        return nativeKanyushaResult;
    }

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

static async Task<IResult?> TryExecuteNativeKanyushaListSnapshotQueryAsync(
    HttpContext http,
    string projectorName,
    SerializedQueryRequest request,
    bool isListQuery,
    MultiProjectionStateRecord record,
    ILogger logger,
    CancellationToken ct)
{
    if (!isListQuery
        || !string.Equals(projectorName, "KanyushaListProjection", StringComparison.Ordinal)
        || !string.Equals(request.QueryType, "GetKanyushaListQuery", StringComparison.Ordinal))
    {
        return null;
    }

    if (!TryParseNativeKanyushaListQuery(request.QueryParamsJson, out NativeKanyushaListQueryParameters parameters))
    {
        return null;
    }

    var cache = http.RequestServices.GetRequiredService<NativeKanyushaListSnapshotCache>();
    var store = http.RequestServices.GetRequiredService<IMultiProjectionStateStore>();
    var entry = cache.Entry;
    await entry.Gate.WaitAsync(ct);
    try
    {
        var stopwatch = Stopwatch.StartNew();
        if (!entry.Matches(record.LastSortableUniqueId, record.EventsProcessed))
        {
            var streamResult = await store.OpenStateDataReadStreamAsync(record, ct);
            if (!streamResult.IsSuccess)
            {
                return Results.BadRequest(new { error = streamResult.GetException().Message });
            }

            await using var snapshotStream = streamResult.GetValue();
            JsonDocument document = await LoadNativeKanyushaListStateAsync(snapshotStream, ct);
            entry.Replace(document, record.LastSortableUniqueId, record.EventsProcessed);
            logger.LogInformation(
                "Native Kanyusha snapshot loaded: LastSortableUniqueId={LastSortableUniqueId}, EventsProcessed={EventsProcessed}, ElapsedMs={ElapsedMs}",
                record.LastSortableUniqueId,
                record.EventsProcessed,
                stopwatch.ElapsedMilliseconds);
        }
        else
        {
            logger.LogInformation(
                "Native Kanyusha snapshot cache hit: LastSortableUniqueId={LastSortableUniqueId}, EventsProcessed={EventsProcessed}, ElapsedMs={ElapsedMs}",
                record.LastSortableUniqueId,
                record.EventsProcessed,
                stopwatch.ElapsedMilliseconds);
        }

        SerializedListQueryResponse response = ExecuteNativeKanyushaListQuery(entry.StateDocument!, parameters);
        logger.LogInformation(
            "Native Kanyusha list query completed: TotalCount={TotalCount}, CurrentPage={CurrentPage}, PageSize={PageSize}, ElapsedMs={ElapsedMs}",
            response.TotalCount,
            response.CurrentPage,
            response.PageSize,
            stopwatch.ElapsedMilliseconds);
        return Results.Ok(response);
    }
    finally
    {
        entry.Gate.Release();
    }
}

static async Task<JsonDocument> LoadNativeKanyushaListStateAsync(Stream snapshotStream, CancellationToken ct)
{
    using JsonDocument envelope = await JsonDocument.ParseAsync(snapshotStream, cancellationToken: ct);
    JsonElement inlineState = envelope.RootElement.GetProperty("inlineState");
    string? payloadJson = inlineState.TryGetProperty("payloadJson", out JsonElement payloadJsonElement)
        ? payloadJsonElement.GetString()
        : null;

    JsonDocument payloadDocument;
    if (!string.IsNullOrWhiteSpace(payloadJson))
    {
        payloadDocument = JsonDocument.Parse(payloadJson);
    }
    else
    {
        string payloadBase64 = inlineState.GetProperty("payloadBase64").GetString()
            ?? throw new InvalidOperationException("payloadBase64 was null.");
        payloadDocument = JsonDocument.Parse(Convert.FromBase64String(payloadBase64));
    }

    using (payloadDocument)
    {
        JsonElement payloadRoot = payloadDocument.RootElement;
        string? stateJson = payloadRoot.TryGetProperty("stateJson", out JsonElement stateJsonElement)
            ? stateJsonElement.GetString()
            : null;
        if (!string.IsNullOrWhiteSpace(stateJson))
        {
            return JsonDocument.Parse(stateJson);
        }

        string compressedStateBase64 = payloadRoot.GetProperty("compressedStateJson").GetString()
            ?? throw new InvalidOperationException("compressedStateJson was null.");
        byte[] compressedStateBytes = Convert.FromBase64String(compressedStateBase64);
        await using var compressedStream = new MemoryStream(compressedStateBytes);
        await using var gzip = new GZipStream(compressedStream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        string decompressedStateJson = await reader.ReadToEndAsync(ct);
        return JsonDocument.Parse(decompressedStateJson);
    }
}

static SerializedListQueryResponse ExecuteNativeKanyushaListQuery(
    JsonDocument stateDocument,
    NativeKanyushaListQueryParameters parameters)
{
    JsonElement kanyushaItems = stateDocument.RootElement.GetProperty("kanyushaItems");
    var orderedKanyushaNos = new List<int>();
    foreach (JsonProperty property in kanyushaItems.EnumerateObject())
    {
        if (int.TryParse(property.Name, out int kanyushaNo))
        {
            orderedKanyushaNos.Add(kanyushaNo);
        }
    }

    orderedKanyushaNos.Sort();
    if (parameters.SortDescending)
    {
        orderedKanyushaNos.Reverse();
    }

    int skip = (parameters.PageNumber - 1) * parameters.PageSize;
    int totalCount = 0;
    using var stream = new MemoryStream();
    using (var writer = new Utf8JsonWriter(stream))
    {
        writer.WriteStartArray();
        foreach (int kanyushaNo in orderedKanyushaNos)
        {
            if (parameters.KanyushaNoFilter is not null
                && !MatchesNativeKanyushaNoFilter(kanyushaNo, parameters.KanyushaNoFilter))
            {
                continue;
            }

            JsonElement kanyusha = kanyushaItems.GetProperty(kanyushaNo.ToString());
            JsonElement nendoByNendo = kanyusha.GetProperty("nendoKanyusByNendo");
            var orderedNendoIds = new List<int>();
            foreach (JsonProperty nendoProperty in nendoByNendo.EnumerateObject())
            {
                if (int.TryParse(nendoProperty.Name, out int nendoId))
                {
                    orderedNendoIds.Add(nendoId);
                }
            }

            orderedNendoIds.Sort();
            orderedNendoIds.Reverse();

            foreach (int nendoId in orderedNendoIds)
            {
                if (totalCount >= skip && totalCount < skip + parameters.PageSize)
                {
                    JsonElement nendo = nendoByNendo.GetProperty(nendoId.ToString());
                    writer.WriteStartObject();
                    if (nendoByNendo.TryGetProperty((nendoId - 1).ToString(), out JsonElement previousNendo)
                        && previousNendo.TryGetProperty("reminderMemo", out JsonElement previousReminderMemo))
                    {
                        writer.WritePropertyName("zenNenReminderNote");
                        previousReminderMemo.WriteTo(writer);
                    }

                    if (nendo.TryGetProperty("reminderMemo", out JsonElement currentReminderMemo))
                    {
                        writer.WritePropertyName("honNenReminderNote");
                        currentReminderMemo.WriteTo(writer);
                    }

                    foreach (JsonProperty property in kanyusha.EnumerateObject())
                    {
                        if (property.NameEquals("nendoKanyusByNendo") || property.NameEquals("lastUpdated"))
                        {
                            continue;
                        }

                        property.WriteTo(writer);
                    }

                    foreach (JsonProperty property in nendo.EnumerateObject())
                    {
                        property.WriteTo(writer);
                    }

                    writer.WriteEndObject();
                }

                totalCount++;
            }
        }

        writer.WriteEndArray();
    }

    int totalPages = (totalCount + parameters.PageSize - 1) / parameters.PageSize;
    if (parameters.PageNumber > totalPages && totalPages != 0)
    {
        stream.SetLength(0);
        using var emptyWriter = new Utf8JsonWriter(stream);
        emptyWriter.WriteStartArray();
        emptyWriter.WriteEndArray();
        emptyWriter.Flush();
    }

    return new SerializedListQueryResponse(
        Encoding.UTF8.GetString(stream.ToArray()),
        totalCount,
        totalPages,
        parameters.PageNumber,
        parameters.PageSize);
}

static bool TryParseNativeKanyushaListQuery(
    string queryParamsJson,
    out NativeKanyushaListQueryParameters parameters)
{
    parameters = default;
    using JsonDocument document = JsonDocument.Parse(string.IsNullOrWhiteSpace(queryParamsJson) ? "{}" : queryParamsJson);
    JsonElement root = document.RootElement;

    int? pageNumber = TryGetInt32(root, "pageNumber");
    int? pageSize = TryGetInt32(root, "pageSize");
    if (pageNumber is not > 0 || pageSize is not > 0)
    {
        return false;
    }

    string? sortField = TryGetString(root, "sortField");
    string? sortOrder = TryGetString(root, "sortOrder");
    bool sortDescending;
    if (string.IsNullOrWhiteSpace(sortField))
    {
        sortDescending = true;
    }
    else if (string.Equals(sortField, "kanyushano", StringComparison.OrdinalIgnoreCase))
    {
        sortDescending = string.Equals(sortOrder, "desc", StringComparison.OrdinalIgnoreCase);
    }
    else
    {
        return false;
    }

    foreach (JsonProperty property in root.EnumerateObject())
    {
        if (property.NameEquals("pageNumber")
            || property.NameEquals("pageSize")
            || property.NameEquals("kanyushaNos")
            || property.NameEquals("sortField")
            || property.NameEquals("sortOrder"))
        {
            continue;
        }

        if (property.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            continue;
        }

        return false;
    }

    parameters = new NativeKanyushaListQueryParameters(
        pageNumber.Value,
        pageSize.Value,
        sortDescending,
        ParseNativeKanyushaNoFilter(TryGetString(root, "kanyushaNos")));
    return true;
}

static int? TryGetInt32(JsonElement root, string propertyName)
{
    if (!root.TryGetProperty(propertyName, out JsonElement property))
    {
        return null;
    }

    return property.ValueKind switch
    {
        JsonValueKind.Number when property.TryGetInt32(out int value) => value,
        JsonValueKind.String when int.TryParse(property.GetString(), out int value) => value,
        _ => null
    };
}

static string? TryGetString(JsonElement root, string propertyName) =>
    root.TryGetProperty(propertyName, out JsonElement property) ? property.GetString() : null;

static NativeKanyushaNoFilterSpec? ParseNativeKanyushaNoFilter(string? rawKanyushaNos)
{
    if (string.IsNullOrWhiteSpace(rawKanyushaNos))
    {
        return null;
    }

    var individualNos = new HashSet<int>();
    var ranges = new List<(int From, int To)>();
    foreach (string token in rawKanyushaNos.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        int dashIndex = token.IndexOf('-');
        if (dashIndex > 0
            && dashIndex < token.Length - 1
            && int.TryParse(token[..dashIndex], out int from)
            && int.TryParse(token[(dashIndex + 1)..], out int to)
            && from <= to)
        {
            ranges.Add((from, to));
            continue;
        }

        if (int.TryParse(token, out int no))
        {
            individualNos.Add(no);
        }
    }

    return individualNos.Count == 0 && ranges.Count == 0
        ? null
        : new NativeKanyushaNoFilterSpec(individualNos, ranges);
}

static bool MatchesNativeKanyushaNoFilter(int kanyushaNo, NativeKanyushaNoFilterSpec filter)
{
    if (filter.IndividualNos.Contains(kanyushaNo))
    {
        return true;
    }

    foreach ((int from, int to) in filter.Ranges)
    {
        if (kanyushaNo >= from && kanyushaNo <= to)
        {
            return true;
        }
    }

    return false;
}

static async Task WaitForSortableUniqueIdAsync(
    IMultiProjectionGrain grain,
    IMultiProjectionStateStore? stateStore,
    string projectorName,
    string projectorVersion,
    string databaseType,
    string sortableUniqueId,
    TimeSpan timeout,
    ILogger logger,
    CancellationToken ct)
{
    var started = Stopwatch.StartNew();

    if (string.Equals(databaseType, "sqlite", StringComparison.OrdinalIgnoreCase) && stateStore is not null)
    {
        try
        {
            // Trigger grain activation once. After that, poll persisted state directly so the
            // wait loop does not compete with catch-up work on the same Orleans activation.
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

static string ResolveDatabaseType(IConfiguration configuration)
{
    var candidates = new[]
    {
        configuration["Sekiban:Database"],
        Environment.GetEnvironmentVariable("SEKIBAN_DATABASE"),
        "postgres"
    };

    return candidates.First(static candidate => !string.IsNullOrWhiteSpace(candidate))!
        .Trim()
        .ToLowerInvariant();
}

static string ResolveSqliteDatabasePath(IConfiguration configuration)
{
    var directPathCandidates = new[]
    {
        configuration["Sekiban:SqlitePath"],
        Environment.GetEnvironmentVariable("SEKIBAN_SQLITE_PATH")
    };

    foreach (var candidate in directPathCandidates)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            continue;
        }

        var resolved = ResolveSqlitePath(candidate);
        if (File.Exists(resolved))
        {
            return resolved;
        }
    }

    var cacheDirCandidates = new[]
    {
        configuration["Sekiban:SqliteCachePath"],
        Environment.GetEnvironmentVariable("SEKIBAN_SQLITE_CACHE_PATH")
    };

    foreach (var candidate in cacheDirCandidates)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            continue;
        }

        var resolved = ResolveSqlitePath(candidate);
        if (File.Exists(resolved))
        {
            return resolved;
        }
    }

    throw new InvalidOperationException(
        "SQLite database path is required when Sekiban:Database=sqlite. " +
        "Set Sekiban:SqlitePath, SEKIBAN_SQLITE_PATH, Sekiban:SqliteCachePath, or SEKIBAN_SQLITE_CACHE_PATH.");
}

static string ResolveSqlitePath(string pathOrDirectory)
{
    var resolved = Path.IsPathRooted(pathOrDirectory)
        ? pathOrDirectory
        : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), pathOrDirectory));

    if (resolved.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
    {
        return resolved;
    }

    return Path.Combine(resolved, "events.db");
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

sealed record NativeKanyushaListQueryParameters(
    int PageNumber,
    int PageSize,
    bool SortDescending,
    NativeKanyushaNoFilterSpec? KanyushaNoFilter);

sealed record NativeKanyushaNoFilterSpec(
    HashSet<int> IndividualNos,
    List<(int From, int To)> Ranges);

sealed class NativeKanyushaListSnapshotCache : IDisposable
{
    public NativeKanyushaListSnapshotCacheEntry Entry { get; } = new();

    public void Dispose() => Entry.Dispose();
}

sealed class NativeKanyushaListSnapshotCacheEntry : IDisposable
{
    public SemaphoreSlim Gate { get; } = new(1, 1);
    public JsonDocument? StateDocument { get; private set; }
    private string? LastSortableUniqueId { get; set; }
    private long EventsProcessed { get; set; }

    public bool Matches(string? lastSortableUniqueId, long eventsProcessed) =>
        StateDocument is not null
        && string.Equals(LastSortableUniqueId, lastSortableUniqueId, StringComparison.Ordinal)
        && EventsProcessed == eventsProcessed;

    public void Replace(JsonDocument stateDocument, string? lastSortableUniqueId, long eventsProcessed)
    {
        StateDocument?.Dispose();
        StateDocument = stateDocument;
        LastSortableUniqueId = lastSortableUniqueId;
        EventsProcessed = eventsProcessed;
    }

    public void Dispose()
    {
        StateDocument?.Dispose();
        Gate.Dispose();
    }
}
