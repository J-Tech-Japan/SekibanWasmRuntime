using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Collections;
using Aic.Kenbai.EventSource;
using Aic.Kenbai.EventSource.KanyushaNos;
using Aic.Kenbai.EventSource.Wasm;
using Aic.Kenbai.EventSource.Projections.HokenNendoShosais;
using Aic.Kenbai.EventSource.Projections.Kanyushyas;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using ResultBoxes;
using Sekiban.Dcb;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Orleans.Serialization;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.WasmRuntime;
using Sekiban.Dcb.WasmRuntime.Wasmtime;
using Sekiban.Dcb.Primitives;
using Wasmtime;

namespace SekibanWasm.Cs.Tests;

internal static class KenbaiWasmParityTestSupport
{
    private const string DefaultReadOnlyEventsDbPath =
        "/Users/tomohisa/dev/GitHub/aic/Kenbai/src/Aic.Kenbai.Development.Cli/output/cache/prod-masked/events.db";

    private static readonly object BuildLock = new();

    public static string RepoRoot { get; } = FindRepoRoot();

    public static string ReadOnlyEventsDbPath =>
        Environment.GetEnvironmentVariable("KENBAI_READONLY_EVENTS_DB")
        ?? DefaultReadOnlyEventsDbPath;

    public static DcbDomainTypes NativeDomainTypes { get; } = KenbaiDcbDomainType.GetDomainTypes();

    public static DcbDomainTypes WasmDomainTypes { get; } = CreateWasmDomainTypes();

    public static JsonSerializerOptions WasmJsonSerializerOptions => AicDomainJsonContext.Default.Options;

    public static string WasmProjectPath =>
        Path.Combine(RepoRoot, "Kenbai", "src", "Aic.Kenbai.EventSource.Wasm", "Aic.Kenbai.EventSource.Wasm.csproj");

    public static string CliProjectPath =>
        Path.Combine(RepoRoot, "Kenbai", "src", "Aic.Kenbai.Development.Cli", "Aic.Kenbai.Development.Cli.csproj");

    public static string CliAssemblyPath =>
        Path.Combine(RepoRoot, "Kenbai", "src", "Aic.Kenbai.Development.Cli", "bin", "Debug", "net10.0", "Aic.Kenbai.Development.Cli.dll");

    public static string WasmModulePath => ResolveExistingWasmModulePath();

    public static void EnsureKenbaiWasmBuilt()
    {
        lock (BuildLock)
        {
            if (File.Exists(WasmModulePath))
            {
                return;
            }

            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"build \"{WasmProjectPath}\" --nologo -v minimal",
                WorkingDirectory = RepoRoot,
                UseShellExecute = false
            }) ?? throw new InvalidOperationException("Failed to start dotnet build for aic.wasm.");
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException("Failed to build aic.wasm.");
            }

            if (!File.Exists(WasmModulePath))
            {
                throw new FileNotFoundException("aic.wasm was not produced by build.", WasmModulePath);
            }

        }
    }

    public static IReadOnlyList<EventRow> LoadEventSamplesPerType(int sampleCountPerType)
    {
        if (!File.Exists(ReadOnlyEventsDbPath))
        {
            throw new FileNotFoundException("Read-only events.db was not found.", ReadOnlyEventsDbPath);
        }

        var rows = new List<EventRow>();
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = ReadOnlyEventsDbPath,
            Mode = SqliteOpenMode.ReadOnly
        }.ConnectionString;

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT EventType, PayloadJson, TagsJson, SortableUniqueId
            FROM (
                SELECT
                    EventType,
                    PayloadJson,
                    TagsJson,
                    SortableUniqueId,
                    ROW_NUMBER() OVER (PARTITION BY EventType ORDER BY SortableUniqueId) AS rn
                FROM dcb_events
            )
            WHERE rn <= $sampleCount
            ORDER BY EventType, SortableUniqueId
            """;
        command.Parameters.AddWithValue("$sampleCount", sampleCountPerType);

        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new EventRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? "[]" : reader.GetString(2),
                reader.GetString(3)));
        }

        return rows;
    }

    public static IReadOnlyList<EventRow> LoadOrderedEvents(int takeCount)
    {
        if (!File.Exists(ReadOnlyEventsDbPath))
        {
            throw new FileNotFoundException("Read-only events.db was not found.", ReadOnlyEventsDbPath);
        }

        var rows = new List<EventRow>(takeCount);
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = ReadOnlyEventsDbPath,
            Mode = SqliteOpenMode.ReadOnly
        }.ConnectionString;

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT EventType, PayloadJson, TagsJson, SortableUniqueId
            FROM dcb_events
            ORDER BY SortableUniqueId
            LIMIT $takeCount
            """;
        command.Parameters.AddWithValue("$takeCount", takeCount);

        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new EventRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? "[]" : reader.GetString(2),
                reader.GetString(3)));
        }

        return rows;
    }

    public static IReadOnlyList<EventRow> LoadOrderedEventsByTypes(int takeCount, params string[] eventTypes)
    {
        if (eventTypes.Length == 0)
        {
            throw new ArgumentException("At least one event type must be provided.", nameof(eventTypes));
        }

        if (!File.Exists(ReadOnlyEventsDbPath))
        {
            throw new FileNotFoundException("Read-only events.db was not found.", ReadOnlyEventsDbPath);
        }

        var rows = new List<EventRow>(takeCount);
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = ReadOnlyEventsDbPath,
            Mode = SqliteOpenMode.ReadOnly
        }.ConnectionString;

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        var placeholders = new List<string>(eventTypes.Length);
        for (int index = 0; index < eventTypes.Length; index++)
        {
            string parameterName = $"$eventType{index}";
            placeholders.Add(parameterName);
            command.Parameters.AddWithValue(parameterName, eventTypes[index]);
        }

        command.CommandText =
            $"""
            SELECT EventType, PayloadJson, TagsJson, SortableUniqueId
            FROM dcb_events
            WHERE EventType IN ({string.Join(", ", placeholders)})
            ORDER BY SortableUniqueId
            LIMIT $takeCount
            """;
        command.Parameters.AddWithValue("$takeCount", takeCount);

        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new EventRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? "[]" : reader.GetString(2),
                reader.GetString(3)));
        }

        return rows;
    }

    public static IReadOnlyList<EventRow> LoadOrderedEventsByTag(int takeCount, string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            throw new ArgumentException("A tag must be provided.", nameof(tag));
        }

        if (!File.Exists(ReadOnlyEventsDbPath))
        {
            throw new FileNotFoundException("Read-only events.db was not found.", ReadOnlyEventsDbPath);
        }

        var rows = new List<EventRow>(takeCount);
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = ReadOnlyEventsDbPath,
            Mode = SqliteOpenMode.ReadOnly
        }.ConnectionString;

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT e.EventType, e.PayloadJson, e.TagsJson, e.SortableUniqueId
            FROM dcb_events e
            INNER JOIN dcb_tags t ON t.SortableUniqueId = e.SortableUniqueId
            WHERE t.Tag = $tag
            ORDER BY e.SortableUniqueId
            LIMIT $takeCount
            """;
        command.Parameters.AddWithValue("$tag", tag);
        command.Parameters.AddWithValue("$takeCount", takeCount);

        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new EventRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? "[]" : reader.GetString(2),
                reader.GetString(3)));
        }

        return rows;
    }

    public static EventRow LoadEventBySortableUniqueId(string sortableUniqueId)
    {
        if (!File.Exists(ReadOnlyEventsDbPath))
        {
            throw new FileNotFoundException("Read-only events.db was not found.", ReadOnlyEventsDbPath);
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = ReadOnlyEventsDbPath,
            Mode = SqliteOpenMode.ReadOnly
        }.ConnectionString;

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT EventType, PayloadJson, TagsJson, SortableUniqueId
            FROM dcb_events
            WHERE SortableUniqueId = $sortableUniqueId
            """;
        command.Parameters.AddWithValue("$sortableUniqueId", sortableUniqueId);

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new InvalidOperationException($"Event '{sortableUniqueId}' was not found in read-only events.db.");
        }

        return new EventRow(
            reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? "[]" : reader.GetString(2),
            reader.GetString(3));
    }

    public static IReadOnlyList<EventRow> LoadEventsAfterSortableUniqueId(
        string sortableUniqueId,
        int takeCount,
        string? databasePath = null)
    {
        string resolvedDatabasePath = databasePath ?? ReadOnlyEventsDbPath;
        if (!File.Exists(resolvedDatabasePath))
        {
            throw new FileNotFoundException("events.db was not found.", resolvedDatabasePath);
        }

        var rows = new List<EventRow>(takeCount);
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = resolvedDatabasePath,
            Mode = SqliteOpenMode.ReadOnly
        }.ConnectionString;

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT EventType, PayloadJson, TagsJson, SortableUniqueId
            FROM dcb_events
            WHERE SortableUniqueId > $sortableUniqueId
            ORDER BY SortableUniqueId
            LIMIT $takeCount
            """;
        command.Parameters.AddWithValue("$sortableUniqueId", sortableUniqueId);
        command.Parameters.AddWithValue("$takeCount", takeCount);

        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new EventRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? "[]" : reader.GetString(2),
                reader.GetString(3)));
        }

        return rows;
    }

    public static StoredProjectionSnapshotRow LoadStoredProjectionSnapshotRow(
        string projectorName,
        string databasePath)
    {
        if (!File.Exists(databasePath))
        {
            throw new FileNotFoundException("events.db was not found.", databasePath);
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly
        }.ConnectionString;

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT ProjectorName, ProjectorVersion, LastSortableUniqueId, EventsProcessed, StateData
            FROM dcb_multi_projection_states
            WHERE ProjectorName = $projectorName
            """;
        command.Parameters.AddWithValue("$projectorName", projectorName);

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new InvalidOperationException(
                $"Projection snapshot '{projectorName}' was not found in '{databasePath}'.");
        }

        return new StoredProjectionSnapshotRow(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt32(3),
            (byte[])reader["StateData"]);
    }

    public static IReadOnlyList<string> LoadDistinctEventTypeNamesFromDb()
    {
        if (!File.Exists(ReadOnlyEventsDbPath))
        {
            throw new FileNotFoundException("Read-only events.db was not found.", ReadOnlyEventsDbPath);
        }

        var rows = new List<string>();
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = ReadOnlyEventsDbPath,
            Mode = SqliteOpenMode.ReadOnly
        }.ConnectionString;

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT DISTINCT EventType
            FROM dcb_events
            ORDER BY EventType
            """;

        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    public static IReadOnlyList<Type> LoadAllImmutableEventPayloadTypes() =>
        typeof(Aic.Kenbai.ImmutableModels.ImmutableModelClasses).Assembly
            .GetTypes()
            .Where(type => typeof(IEventPayload).IsAssignableFrom(type))
            .Where(type => type.IsClass && !type.IsAbstract)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToList();

    public static IReadOnlyDictionary<string, Type> GetRegisteredEventTypes(DcbDomainTypes domainTypes)
    {
        var result = new SortedDictionary<string, Type>(StringComparer.Ordinal);
        foreach (Type type in LoadAllImmutableEventPayloadTypes())
        {
            Type? registered = domainTypes.EventTypes.GetEventType(type.Name);
            if (registered is not null)
            {
                result[type.Name] = registered;
            }
        }

        return result;
    }

    public static object? GetWasmJsonTypeInfo(Type type) => AicDomainJsonContext.Default.GetTypeInfo(type);

    public static string CanonicalizeJson(string json)
    {
        JsonNode? node = JsonNode.Parse(json);
        return CanonicalizeNode(node)?.ToJsonString() ?? "null";
    }

    public static string CanonicalizeJsonForRuntimeParity(string json)
    {
        JsonNode? node = JsonNode.Parse(json);
        JsonNode? normalized = CanonicalizeNode(node);
        RemoveRuntimeDiagnosticOnlyProperties(normalized);
        return normalized?.ToJsonString() ?? "null";
    }

    public static KanyushaListProjection ReplayKanyushaListProjectionNative(IEnumerable<EventRow> rows)
        => ReplayProjectionNative(
            rows,
            KanyushaListProjection.GenerateInitialPayload,
            KanyushaListProjection.Project);

    public static HokenNendoShosaiListProjection ReplayHokenNendoShosaiListProjectionNative(IEnumerable<EventRow> rows)
        => ReplayProjectionNative(
            rows,
            HokenNendoShosaiListProjection.GenerateInitialPayload,
            HokenNendoShosaiListProjection.Project);

    public static ITagStatePayload ReplayKanyushaNumberKanriTagStateNative(IEnumerable<EventRow> rows)
        => ReplayTagProjectionNative(nameof(KanyushaNumberKanriTagProjector), rows);

    public static ITagStatePayload ReplayTagStateNative(string projectorName, IEnumerable<EventRow> rows)
        => ReplayTagProjectionNative(projectorName, rows);

    public static SerializableTagState ReplayTagStateWasmPrimitive(
        string projectorName,
        IEnumerable<EventRow> rows,
        SerializableTagState? cachedState = null,
        string? latestSortableUniqueId = null)
    {
        EnsureKenbaiWasmBuilt();

        var runtime = new WasmtimeRuntime();
        var moduleCache = new WasmtimeModuleCache(runtime);
        var primitiveHost = new WasmtimePrimitiveProjectionHost(
            runtime,
            moduleCache,
            new WasmtimeHostOptions
            {
                DefaultModulePath = WasmModulePath
            });

        using IPrimitiveProjectionInstance instance = primitiveHost.CreateInstance(projectorName);
        string projectorVersion = WasmDomainTypes.TagProjectorTypes.GetProjectorVersion(projectorName).GetValue();
        var primitive = new WasmTagStateProjectionPrimitive(
            instance,
            projectorName,
            projectorVersion,
            WasmDomainTypes.EventTypes,
            AicDomainJsonContext.Default.Options);

        if (!primitive.ApplyState(cachedState))
        {
            throw new InvalidOperationException($"Failed to apply cached tag state for '{projectorName}'.");
        }

        List<SerializableEvent> serializableEvents = rows
            .Select(ToSerializableEvent)
            .OrderBy(static row => row.SortableUniqueIdValue, StringComparer.Ordinal)
            .ToList();

        string? effectiveLatestSortableUniqueId = latestSortableUniqueId
            ?? serializableEvents.LastOrDefault()?.SortableUniqueIdValue;

        if (!primitive.ApplyEvents(serializableEvents, effectiveLatestSortableUniqueId))
        {
            throw new InvalidOperationException($"Failed to replay WASM tag state for '{projectorName}'.");
        }

        return primitive.GetSerializedState();
    }

    public static string ReplayProjectionWasm(string projectorName, IEnumerable<EventRow> rows)
        => ReplayProjectionWasmInternal(projectorName, rows, restoreAfterCount: null);

    public static string ReplayProjectionWasmWithRestore(
        string projectorName,
        IEnumerable<EventRow> rows,
        int restoreAfterCount)
        => ReplayProjectionWasmInternal(projectorName, rows, restoreAfterCount);

    public static string ExtractCanonicalStateJsonFromRawSnapshot(string rawSnapshotJson)
        => CanonicalizeJson(ExtractStateJsonFromSnapshotPayload(Encoding.UTF8.GetBytes(rawSnapshotJson)));

    public static void EnsureKenbaiCliBuilt()
    {
        lock (BuildLock)
        {
            if (File.Exists(CliAssemblyPath))
            {
                return;
            }

            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"build \"{CliProjectPath}\" --nologo -v minimal",
                WorkingDirectory = RepoRoot,
                UseShellExecute = false
            }) ?? throw new InvalidOperationException("Failed to start dotnet build for Aic.Kenbai.Development.Cli.");
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException("Failed to build Aic.Kenbai.Development.Cli.");
            }

            if (!File.Exists(CliAssemblyPath))
            {
                throw new FileNotFoundException("Aic.Kenbai.Development.Cli.dll was not produced by build.", CliAssemblyPath);
            }
        }
    }

    public static async Task RunCliSnapshotAsync(
        string outputDirectory,
        string runtime,
        string projectorName,
        int maxEvents,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        EnsureKenbaiCliBuilt();
        Directory.CreateDirectory(outputDirectory);

        string arguments =
            $"exec \"{CliAssemblyPath}\" snapshot " +
            $"--input-db \"{ReadOnlyEventsDbPath}\" " +
            $"--output-dir \"{outputDirectory}\" " +
            $"--runtime {runtime} " +
            $"--projector {projectorName} " +
            $"--max-events {maxEvents} " +
            $"--batch-size {batchSize} " +
            $"--wasm-module-path \"{WasmModulePath}\"";

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = arguments,
                WorkingDirectory = RepoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };

        process.Start();
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        string stdout = await stdoutTask;
        string stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"CLI snapshot failed with exit code {process.ExitCode}.{Environment.NewLine}STDOUT:{Environment.NewLine}{stdout}{Environment.NewLine}STDERR:{Environment.NewLine}{stderr}");
        }
    }

    public static string LoadCanonicalProjectionStateFromSnapshot(string snapshotPath)
    {
        JsonNode? root = JsonNode.Parse(File.ReadAllText(snapshotPath));
        string? payloadBase64 = root?["inlineState"]?["payloadBase64"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(payloadBase64))
        {
            throw new InvalidOperationException($"Snapshot '{snapshotPath}' does not contain inlineState.payloadBase64.");
        }

        byte[] payloadBytes = Convert.FromBase64String(payloadBase64);
        string stateJson = ExtractStateJsonFromSnapshotPayload(payloadBytes);
        return CanonicalizeJson(stateJson);
    }

    private static TProjection ReplayProjectionNative<TProjection>(
        IEnumerable<EventRow> rows,
        Func<TProjection> initialize,
        Func<TProjection, Event, List<ITag>, DcbDomainTypes, SortableUniqueId, TProjection> project)
    {
        DcbDomainTypes domainTypes = KenbaiDcbDomainType.GetDomainTypes();
        TProjection state = initialize();

        foreach (EventRow row in rows)
        {
            IEventPayload payload = domainTypes.EventTypes.DeserializeEventPayload(row.EventType, row.PayloadJson)
                ?? throw new InvalidOperationException($"Native domain could not deserialize '{row.EventType}'.");
            List<string> tagStrings = JsonSerializer.Deserialize<List<string>>(row.TagsJson) ?? [];
            List<ITag> tags = tagStrings.Select(domainTypes.TagTypes.GetTag).ToList();
            var ev = new Event(
                payload,
                row.SortableUniqueId,
                row.EventType,
                Guid.NewGuid(),
                new EventMetadata("test", row.EventType, "kenbai-parity-test"),
                tagStrings);
            state = project(
                state,
                ev,
                tags,
                domainTypes,
                new SortableUniqueId(row.SortableUniqueId));
        }

        return state;
    }

    private static ITagStatePayload ReplayTagProjectionNative(
        string projectorName,
        IEnumerable<EventRow> rows)
    {
        DcbDomainTypes domainTypes = KenbaiDcbDomainType.GetDomainTypes();
        ResultBox<Func<ITagStatePayload, Event, ITagStatePayload>> projectorResult =
            domainTypes.TagProjectorTypes.GetProjectorFunction(projectorName);
        if (!projectorResult.IsSuccess)
        {
            throw projectorResult.GetException();
        }

        Func<ITagStatePayload, Event, ITagStatePayload> projector = projectorResult.GetValue();
        ITagStatePayload state = new EmptyTagStatePayload();

        foreach (EventRow row in rows)
        {
            IEventPayload payload = domainTypes.EventTypes.DeserializeEventPayload(row.EventType, row.PayloadJson)
                ?? throw new InvalidOperationException($"Native domain could not deserialize '{row.EventType}'.");
            List<string> tagStrings = JsonSerializer.Deserialize<List<string>>(row.TagsJson) ?? [];
            var ev = new Event(
                payload,
                row.SortableUniqueId,
                row.EventType,
                Guid.NewGuid(),
                new EventMetadata("test", row.EventType, "kenbai-parity-test"),
                tagStrings);
            state = projector(state, ev);
        }

        return state;
    }

    private static SerializableEvent ToSerializableEvent(EventRow row)
    {
        List<string> tagStrings = JsonSerializer.Deserialize<List<string>>(row.TagsJson) ?? [];

        return new SerializableEvent(
            Encoding.UTF8.GetBytes(row.PayloadJson),
            row.SortableUniqueId,
            Guid.NewGuid(),
            new EventMetadata("test", row.EventType, "kenbai-parity-test"),
            tagStrings,
            row.EventType);
    }

    public sealed record EventRow(
        string EventType,
        string PayloadJson,
        string TagsJson,
        string SortableUniqueId);

    public sealed record StoredProjectionSnapshotRow(
        string ProjectorName,
        string ProjectorVersion,
        string LastSortableUniqueId,
        int EventsProcessed,
        byte[] StateData);

    public static WasmProjectionActorHost CreateKenbaiProjectionActorHost(string projectorName)
    {
        var runtime = new WasmtimeRuntime();
        var moduleCache = new WasmtimeModuleCache(runtime);
        var primitiveHost = new WasmtimePrimitiveProjectionHost(
            runtime,
            moduleCache,
            new WasmtimeHostOptions
            {
                ProjectorModulePaths = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [projectorName] = WasmModulePath
                }
            });
        var registry = new WasmProjectorRegistry();
        registry.Register(new WasmModuleRef(
            ProjectorName: projectorName,
            ModulePath: WasmModulePath,
            AbiKind: "wasi-preview1",
            ModuleVersion: "1.0.0",
            ProjectorVersion: "1.0.0"));

        return new WasmProjectionActorHost(
            primitiveHost,
            registry,
            KenbaiDcbDomainType.GetDomainTypes(),
            KenbaiDcbDomainType.GetDomainTypes().JsonSerializerOptions,
            projectorName,
            NullLogger.Instance);
    }

    public sealed class WasmDiagnosticsClient : IDisposable
    {
        private readonly WasmtimeRuntime _runtime;
        private readonly Store _store;
        private readonly Linker _linker;
        private readonly Wasmtime.Module _module;
        private readonly Instance _instance;
        private readonly Memory _memory;
        private readonly Func<int, int> _alloc;
        private readonly Action<int, int> _free;
        private readonly Func<int, int, int, int, long>? _roundTripEventPayload;
        private readonly Func<int, int, int, int, long>? _roundTripTagStatePayload;
        private readonly Func<int, int, long>? _roundTripTags;
        private readonly Func<int, int, int, int, int, int, int, int, int, int, long>? _debugProjectOnce;
        private readonly Func<int>? _diagnoseDomainTypes;
        private readonly Func<long>? _debugListMultiProjectorNames;
        private readonly Func<int>? _debugEnvironmentFlags;
        private readonly Func<int, int>? _debugSetMultiProjectBypass;
        private readonly Func<int>? _debugGetMultiProjectBypass;
        private readonly Func<int, int>? _debugSetSubmittedTagMode;
        private readonly Func<int>? _debugGetSubmittedTagMode;
        private readonly Func<int>? _debugClearLastMultiProjectError;
        private readonly Func<long>? _debugTakeLastMultiProjectError;
        private readonly Func<int, int, int>? _createInstance;
        private readonly Action<int>? _beginPayloadBuffer;
        private readonly Action<int, int, int>? _appendPayloadChunk;
        private readonly Action<int>? _beginMetadataBuffer;
        private readonly Action<int, int, int>? _appendMetadataChunk;
        private readonly Action<int, int, int, int, int>? _applyBufferedEventWithSortable;
        private readonly Func<int, long>? _serializeState;
        private readonly Action<int, int, int>? _restoreState;
        private readonly Func<int, int>? _debugMetadataBufferLength;
        private readonly Func<int, int, int, int, int, int>? _debugBufferedSortableNoop;
        private readonly Func<int, int, int, int, int, int>? _debugBufferedSortableConsumeMetadata;
        private readonly Func<int, int, int, int, int, int>? _debugBufferedSortableConsumeMetadataAndReadHeaders;
        private readonly Func<int, int, int, int, int, int>? _debugBufferedSortableConsumeMetadataAndPayload;
        private readonly Func<int, int, int, int, int, int>? _debugBufferedSortableDeserializePayload;
        private readonly Func<int, int, int, int, int, int>? _debugBufferedSortableParseTags;
        private readonly Func<int, int, int, int, int, int>? _debugBufferedSortableCreateEvent;
        private readonly Func<int, int, int, int, int, int>? _debugBufferedSortableGetTagProjector;
        private readonly Func<int, int, int, int, int, int>? _debugBufferedSortableApplyTagEvent;
        private readonly Func<int, int, int, int, int, int>? _debugBufferedSortableResolveTagObjects;
        private readonly Func<int, int, int, int, int, int>? _debugBufferedSortableResolveSafeWindow;
        private readonly Func<int, int, int, int, int, int>? _debugBufferedSortableApplyMultiEvent;

        public WasmDiagnosticsClient()
        {
            EnsureKenbaiWasmBuilt();

            _runtime = new WasmtimeRuntime();
            _module = new WasmtimeModuleCache(_runtime).GetOrLoad(WasmModulePath);
            _store = new Store(_runtime.Engine);
            var wasiConfiguration = new WasiConfiguration()
                .WithStandardOutput("/dev/null")
                .WithStandardError("/dev/null")
                .WithEnvironmentVariables(
                    Environment.GetEnvironmentVariables()
                        .Cast<DictionaryEntry>()
                        .Where(entry => entry.Key is string && entry.Value is string)
                        .Select(entry => ((string)entry.Key, (string)entry.Value)));
            _store.SetWasiConfiguration(wasiConfiguration);
            _linker = _runtime.CreateLinker();
            _instance = _linker.Instantiate(_store, _module);
            _instance.GetAction("_initialize")?.Invoke();

            _memory = _instance.GetMemory("memory")
                ?? throw new InvalidOperationException("WASM module does not export memory.");
            _alloc = _instance.GetFunction<int, int>("alloc")
                ?? throw new InvalidOperationException("WASM module does not export alloc.");
            _free = _instance.GetAction<int, int>("dealloc") ?? _instance.GetAction<int, int>("free")
                ?? throw new InvalidOperationException("WASM module does not export dealloc/free.");
            _roundTripEventPayload = _instance.GetFunction<int, int, int, int, long>("debug_roundtrip_event_payload");
            _roundTripTagStatePayload = _instance.GetFunction<int, int, int, int, long>("debug_roundtrip_tag_state_payload");
            _roundTripTags = _instance.GetFunction<int, int, long>("debug_roundtrip_tags");
            _debugProjectOnce = _instance.GetFunction<int, int, int, int, int, int, int, int, int, int, long>("debug_project_once");
            _diagnoseDomainTypes = _instance.GetFunction<int>("diagnose_domain_types");
            _debugListMultiProjectorNames = _instance.GetFunction<long>("debug_list_multi_projector_names");
            _debugEnvironmentFlags = _instance.GetFunction<int>("debug_environment_flags");
            _debugSetMultiProjectBypass = _instance.GetFunction<int, int>("debug_set_multi_project_bypass");
            _debugGetMultiProjectBypass = _instance.GetFunction<int>("debug_get_multi_project_bypass");
            _debugSetSubmittedTagMode =
                _instance.GetFunction<int, int>("debug_set_submitted_tag_mode") ??
                _instance.GetFunction("debug_set_submitted_tag_mode")?.WrapFunc<int, int>();
            _debugGetSubmittedTagMode =
                _instance.GetFunction<int>("debug_get_submitted_tag_mode") ??
                _instance.GetFunction("debug_get_submitted_tag_mode")?.WrapFunc<int>();
            _debugClearLastMultiProjectError =
                _instance.GetFunction<int>("debug_clear_last_multi_project_error") ??
                _instance.GetFunction("debug_clear_last_multi_project_error")?.WrapFunc<int>();
            _debugTakeLastMultiProjectError =
                _instance.GetFunction<long>("debug_take_last_multi_project_error") ??
                _instance.GetFunction("debug_take_last_multi_project_error")?.WrapFunc<long>();
            _createInstance = _instance.GetFunction<int, int, int>("create_instance");
            _beginPayloadBuffer = _instance.GetAction<int>("begin_payload_buffer");
            _appendPayloadChunk = _instance.GetAction<int, int, int>("append_payload_chunk");
            _beginMetadataBuffer = _instance.GetAction<int>("begin_metadata_buffer");
            _appendMetadataChunk = _instance.GetAction<int, int, int>("append_metadata_chunk");
            _applyBufferedEventWithSortable = _instance.GetAction<int, int, int, int, int>("apply_buffered_event_with_sortable");
            _serializeState = _instance.GetFunction<int, long>("serialize_state");
            _restoreState = _instance.GetAction<int, int, int>("restore_state");
            _debugMetadataBufferLength = _instance.GetFunction<int, int>("debug_metadata_buffer_length");
            _debugBufferedSortableNoop = _instance.GetFunction<int, int, int, int, int, int>("debug_buffered_sortable_noop");
            _debugBufferedSortableConsumeMetadata = _instance.GetFunction<int, int, int, int, int, int>("debug_buffered_sortable_consume_metadata");
            _debugBufferedSortableConsumeMetadataAndReadHeaders = _instance.GetFunction<int, int, int, int, int, int>("debug_buffered_sortable_consume_metadata_and_read_headers");
            _debugBufferedSortableConsumeMetadataAndPayload = _instance.GetFunction<int, int, int, int, int, int>("debug_buffered_sortable_consume_metadata_and_payload");
            _debugBufferedSortableDeserializePayload = _instance.GetFunction<int, int, int, int, int, int>("debug_buffered_sortable_deserialize_payload");
            _debugBufferedSortableParseTags = _instance.GetFunction<int, int, int, int, int, int>("debug_buffered_sortable_parse_tags");
            _debugBufferedSortableCreateEvent = _instance.GetFunction<int, int, int, int, int, int>("debug_buffered_sortable_create_event");
            _debugBufferedSortableGetTagProjector = _instance.GetFunction<int, int, int, int, int, int>("debug_buffered_sortable_get_tag_projector");
            _debugBufferedSortableApplyTagEvent = _instance.GetFunction<int, int, int, int, int, int>("debug_buffered_sortable_apply_tag_event");
            _debugBufferedSortableResolveTagObjects = _instance.GetFunction<int, int, int, int, int, int>("debug_buffered_sortable_resolve_tag_objects");
            _debugBufferedSortableResolveSafeWindow = _instance.GetFunction<int, int, int, int, int, int>("debug_buffered_sortable_resolve_safe_window");
            _debugBufferedSortableApplyMultiEvent = _instance.GetFunction<int, int, int, int, int, int>("debug_buffered_sortable_apply_multi_event");
        }

        public bool SupportsDebugRoundTrip =>
            _roundTripEventPayload is not null &&
            _roundTripTagStatePayload is not null &&
            _roundTripTags is not null;

        public int DebugEnvironmentFlags()
        {
            if (_debugEnvironmentFlags is null)
            {
                throw new NotSupportedException("The configured WASM artifact does not export debug_environment_flags.");
            }

            return _debugEnvironmentFlags();
        }

        public int DebugSetMultiProjectBypass(bool enabled)
        {
            if (_debugSetMultiProjectBypass is null)
            {
                throw new NotSupportedException("The configured WASM artifact does not export debug_set_multi_project_bypass.");
            }

            return _debugSetMultiProjectBypass(enabled ? 1 : 0);
        }

        public int DebugGetMultiProjectBypass()
        {
            if (_debugGetMultiProjectBypass is null)
            {
                throw new NotSupportedException("The configured WASM artifact does not export debug_get_multi_project_bypass.");
            }

            return _debugGetMultiProjectBypass();
        }

        public int DebugSetSubmittedTagMode(int mode)
        {
            if (_debugSetSubmittedTagMode is null)
            {
                throw new NotSupportedException("The configured WASM artifact does not export debug_set_submitted_tag_mode.");
            }

            return _debugSetSubmittedTagMode(mode);
        }

        public int DebugGetSubmittedTagMode()
        {
            if (_debugGetSubmittedTagMode is null)
            {
                throw new NotSupportedException("The configured WASM artifact does not export debug_get_submitted_tag_mode.");
            }

            return _debugGetSubmittedTagMode();
        }

        public void DebugClearLastMultiProjectError()
        {
            if (_debugClearLastMultiProjectError is null)
            {
                throw new NotSupportedException("The configured WASM artifact does not export debug_clear_last_multi_project_error.");
            }

            _ = _debugClearLastMultiProjectError();
        }

        public string DebugTakeLastMultiProjectError()
        {
            if (_debugTakeLastMultiProjectError is null)
            {
                throw new NotSupportedException("The configured WASM artifact does not export debug_take_last_multi_project_error.");
            }

            return ReadPackedString(_debugTakeLastMultiProjectError());
        }

        public IReadOnlyList<string> ExportNames => _module.Exports.Select(export => export.Name).ToList();

        public string RoundTripEventPayload(string eventType, string payloadJson)
        {
            if (_roundTripEventPayload is null)
            {
                throw new NotSupportedException("The configured WASM artifact does not export debug_roundtrip_event_payload.");
            }

            var (eventTypePtr, eventTypeLen) = WriteString(eventType);
            var (payloadPtr, payloadLen) = WriteString(payloadJson);
            try
            {
                long packed = _roundTripEventPayload(eventTypePtr, eventTypeLen, payloadPtr, payloadLen);
                return ReadPackedString(packed);
            }
            finally
            {
                Free(eventTypePtr, eventTypeLen);
                Free(payloadPtr, payloadLen);
            }
        }

        public string RoundTripTagStatePayload(string payloadName, string payloadJson)
        {
            if (_roundTripTagStatePayload is null)
            {
                throw new NotSupportedException("The configured WASM artifact does not export debug_roundtrip_tag_state_payload.");
            }

            var (payloadNamePtr, payloadNameLen) = WriteString(payloadName);
            var (payloadPtr, payloadLen) = WriteString(payloadJson);
            try
            {
                long packed = _roundTripTagStatePayload(payloadNamePtr, payloadNameLen, payloadPtr, payloadLen);
                return ReadPackedString(packed);
            }
            finally
            {
                Free(payloadNamePtr, payloadNameLen);
                Free(payloadPtr, payloadLen);
            }
        }

        public string RoundTripTags(string tagsJson)
        {
            if (_roundTripTags is null)
            {
                throw new NotSupportedException("The configured WASM artifact does not export debug_roundtrip_tags.");
            }

            var (tagsPtr, tagsLen) = WriteString(tagsJson);
            try
            {
                long packed = _roundTripTags(tagsPtr, tagsLen);
                return ReadPackedString(packed);
            }
            finally
            {
                Free(tagsPtr, tagsLen);
            }
        }

        public string DebugProjectOnce(
            string projectorName,
            string eventType,
            string payloadJson,
            string tagsJson,
            string sortableUniqueId)
        {
            if (_debugProjectOnce is null)
            {
                throw new NotSupportedException("The configured WASM artifact does not export debug_project_once.");
            }

            var (projectorPtr, projectorLen) = WriteString(projectorName);
            var (eventTypePtr, eventTypeLen) = WriteString(eventType);
            var (payloadPtr, payloadLen) = WriteString(payloadJson);
            var (tagsPtr, tagsLen) = WriteString(tagsJson);
            var (sortablePtr, sortableLen) = WriteString(sortableUniqueId);
            try
            {
                long packed = _debugProjectOnce(
                    projectorPtr,
                    projectorLen,
                    eventTypePtr,
                    eventTypeLen,
                    payloadPtr,
                    payloadLen,
                    tagsPtr,
                    tagsLen,
                    sortablePtr,
                    sortableLen);
                return ReadPackedString(packed);
            }
            finally
            {
                Free(projectorPtr, projectorLen);
                Free(eventTypePtr, eventTypeLen);
                Free(payloadPtr, payloadLen);
                Free(tagsPtr, tagsLen);
                Free(sortablePtr, sortableLen);
            }
        }

        public string ReplayKanyushaListProjection(IEnumerable<EventRow> rows)
            => ReplayProjectionWasm(KanyushaListProjection.MultiProjectorName, rows);

        public int DiagnoseDomainTypes()
        {
            if (_diagnoseDomainTypes is null)
            {
                throw new NotSupportedException("The configured WASM artifact does not export diagnose_domain_types.");
            }

            return _diagnoseDomainTypes();
        }

        public IReadOnlyList<string> DebugListMultiProjectorNames()
        {
            if (_debugListMultiProjectorNames is null)
            {
                throw new NotSupportedException("The configured WASM artifact does not export debug_list_multi_projector_names.");
            }

            string json = ReadPackedString(_debugListMultiProjectorNames());
            return JsonSerializer.Deserialize<string[]>(json)
                ?? throw new InvalidOperationException("Failed to deserialize debug_list_multi_projector_names response.");
        }

        public int TryCreateInstance(string projectorName)
        {
            if (_createInstance is null)
            {
                throw new NotSupportedException("The configured WASM artifact does not export create_instance.");
            }

            var (projectorPtr, projectorLen) = WriteString(projectorName);
            try
            {
                return _createInstance(projectorPtr, projectorLen);
            }
            finally
            {
                Free(projectorPtr, projectorLen);
            }
        }

        public void BeginPayloadBuffer(int instanceId)
        {
            if (_beginPayloadBuffer is null)
            {
                throw new NotSupportedException("The configured WASM artifact does not export begin_payload_buffer.");
            }

            _beginPayloadBuffer(instanceId);
        }

        public void AppendPayloadChunk(int instanceId, string payloadJson)
        {
            if (_appendPayloadChunk is null)
            {
                throw new NotSupportedException("The configured WASM artifact does not export append_payload_chunk.");
            }

            var (payloadPtr, payloadLen) = WriteString(payloadJson);
            try
            {
                _appendPayloadChunk(instanceId, payloadPtr, payloadLen);
            }
            finally
            {
                Free(payloadPtr, payloadLen);
            }
        }

        public void BeginMetadataBuffer(int instanceId)
        {
            if (_beginMetadataBuffer is null)
            {
                throw new NotSupportedException("The configured WASM artifact does not export begin_metadata_buffer.");
            }

            _beginMetadataBuffer(instanceId);
        }

        public void AppendMetadataChunk(int instanceId, string metadataJson)
        {
            if (_appendMetadataChunk is null)
            {
                throw new NotSupportedException("The configured WASM artifact does not export append_metadata_chunk.");
            }

            var (metadataPtr, metadataLen) = WriteString(metadataJson);
            try
            {
                _appendMetadataChunk(instanceId, metadataPtr, metadataLen);
            }
            finally
            {
                Free(metadataPtr, metadataLen);
            }
        }

        public int DebugMetadataBufferLength(int instanceId)
        {
            if (_debugMetadataBufferLength is null)
            {
                throw new NotSupportedException("The configured WASM artifact does not export debug_metadata_buffer_length.");
            }

            return _debugMetadataBufferLength(instanceId);
        }

        public void ApplyBufferedEventWithSortable(int instanceId, string eventType, string sortableUniqueId)
        {
            if (_applyBufferedEventWithSortable is null)
            {
                throw new NotSupportedException("The configured WASM artifact does not export apply_buffered_event_with_sortable.");
            }

            var (eventTypePtr, eventTypeLen) = WriteString(eventType);
            var (sortablePtr, sortableLen) = WriteString(sortableUniqueId);
            try
            {
                _applyBufferedEventWithSortable(instanceId, eventTypePtr, eventTypeLen, sortablePtr, sortableLen);
            }
            finally
            {
                Free(eventTypePtr, eventTypeLen);
                Free(sortablePtr, sortableLen);
            }
        }

        public string SerializeState(int instanceId)
        {
            if (_serializeState is null)
            {
                throw new NotSupportedException("The configured WASM artifact does not export serialize_state.");
            }

            return ReadPackedString(_serializeState(instanceId));
        }

        public void RestoreState(int instanceId, string stateJson)
        {
            if (_restoreState is null)
            {
                throw new NotSupportedException("The configured WASM artifact does not export restore_state.");
            }

            var (statePtr, stateLen) = WriteString(stateJson);
            try
            {
                _restoreState(instanceId, statePtr, stateLen);
            }
            finally
            {
                Free(statePtr, stateLen);
            }
        }

        public int DebugBufferedSortableNoop(int instanceId, string eventType, string sortableUniqueId) =>
            InvokeBufferedSortableDiagnostic(
                _debugBufferedSortableNoop,
                "debug_buffered_sortable_noop",
                instanceId,
                eventType,
                sortableUniqueId);

        public int DebugBufferedSortableConsumeMetadata(int instanceId, string eventType, string sortableUniqueId) =>
            InvokeBufferedSortableDiagnostic(
                _debugBufferedSortableConsumeMetadata,
                "debug_buffered_sortable_consume_metadata",
                instanceId,
                eventType,
                sortableUniqueId);

        public int DebugBufferedSortableConsumeMetadataAndReadHeaders(int instanceId, string eventType, string sortableUniqueId) =>
            InvokeBufferedSortableDiagnostic(
                _debugBufferedSortableConsumeMetadataAndReadHeaders,
                "debug_buffered_sortable_consume_metadata_and_read_headers",
                instanceId,
                eventType,
                sortableUniqueId);

        public int DebugBufferedSortableConsumeMetadataAndPayload(int instanceId, string eventType, string sortableUniqueId) =>
            InvokeBufferedSortableDiagnostic(
                _debugBufferedSortableConsumeMetadataAndPayload,
                "debug_buffered_sortable_consume_metadata_and_payload",
                instanceId,
                eventType,
                sortableUniqueId);

        public int DebugBufferedSortableDeserializePayload(int instanceId, string eventType, string sortableUniqueId) =>
            InvokeBufferedSortableDiagnostic(
                _debugBufferedSortableDeserializePayload,
                "debug_buffered_sortable_deserialize_payload",
                instanceId,
                eventType,
                sortableUniqueId);

        public int DebugBufferedSortableParseTags(int instanceId, string eventType, string sortableUniqueId) =>
            InvokeBufferedSortableDiagnostic(
                _debugBufferedSortableParseTags,
                "debug_buffered_sortable_parse_tags",
                instanceId,
                eventType,
                sortableUniqueId);

        public int DebugBufferedSortableCreateEvent(int instanceId, string eventType, string sortableUniqueId) =>
            InvokeBufferedSortableDiagnostic(
                _debugBufferedSortableCreateEvent,
                "debug_buffered_sortable_create_event",
                instanceId,
                eventType,
                sortableUniqueId);

        public int DebugBufferedSortableGetTagProjector(int instanceId, string eventType, string sortableUniqueId) =>
            InvokeBufferedSortableDiagnostic(
                _debugBufferedSortableGetTagProjector,
                "debug_buffered_sortable_get_tag_projector",
                instanceId,
                eventType,
                sortableUniqueId);

        public int DebugBufferedSortableApplyTagEvent(int instanceId, string eventType, string sortableUniqueId) =>
            InvokeBufferedSortableDiagnostic(
                _debugBufferedSortableApplyTagEvent,
                "debug_buffered_sortable_apply_tag_event",
                instanceId,
                eventType,
                sortableUniqueId);

        public int DebugBufferedSortableResolveTagObjects(int instanceId, string eventType, string sortableUniqueId) =>
            InvokeBufferedSortableDiagnostic(
                _debugBufferedSortableResolveTagObjects,
                "debug_buffered_sortable_resolve_tag_objects",
                instanceId,
                eventType,
                sortableUniqueId);

        public int DebugBufferedSortableResolveSafeWindow(int instanceId, string eventType, string sortableUniqueId) =>
            InvokeBufferedSortableDiagnostic(
                _debugBufferedSortableResolveSafeWindow,
                "debug_buffered_sortable_resolve_safe_window",
                instanceId,
                eventType,
                sortableUniqueId);

        public int DebugBufferedSortableApplyMultiEvent(int instanceId, string eventType, string sortableUniqueId) =>
            InvokeBufferedSortableDiagnostic(
                _debugBufferedSortableApplyMultiEvent,
                "debug_buffered_sortable_apply_multi_event",
                instanceId,
                eventType,
                sortableUniqueId);

        public bool HasRawExport(string exportName)
            => _instance.GetFunction(exportName) is not null;

        public bool CanWrapFiveIntParamsReturningInt(string exportName)
            => _instance.GetFunction(exportName)?.WrapFunc<int, int, int, int, int, int>() is not null;

        public void Dispose()
        {
            _module.Dispose();
            _linker.Dispose();
            _store.Dispose();
            _runtime.Dispose();
        }

        private (int Ptr, int Len) WriteString(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            if (bytes.Length == 0)
            {
                return (0, 0);
            }

            int ptr = _alloc(bytes.Length);
            bytes.CopyTo(_memory.GetSpan(ptr, bytes.Length));
            return (ptr, bytes.Length);
        }

        private string ReadPackedString(long packed)
        {
            if (packed == 0)
            {
                return string.Empty;
            }

            int ptr = unchecked((int)(packed >> 32));
            int len = unchecked((int)(packed & 0xFFFFFFFF));
            if (ptr == 0 || len == 0)
            {
                return string.Empty;
            }

            string value = Encoding.UTF8.GetString(_memory.GetSpan(ptr, len));
            Free(ptr, len);
            return value;
        }

        private void Free(int ptr, int len)
        {
            if (ptr == 0 || len == 0)
            {
                return;
            }

            _free(ptr, len);
        }

        private int InvokeBufferedSortableDiagnostic(
            Func<int, int, int, int, int, int>? export,
            string exportName,
            int instanceId,
            string eventType,
            string sortableUniqueId)
        {
            if (export is null)
            {
                throw new NotSupportedException($"The configured WASM artifact does not export {exportName}.");
            }

            var (eventTypePtr, eventTypeLen) = WriteString(eventType);
            var (sortablePtr, sortableLen) = WriteString(sortableUniqueId);
            try
            {
                return export(instanceId, eventTypePtr, eventTypeLen, sortablePtr, sortableLen);
            }
            finally
            {
                Free(eventTypePtr, eventTypeLen);
                Free(sortablePtr, sortableLen);
            }
        }
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "Kenbai")) &&
                Directory.Exists(Path.Combine(current.FullName, "SekibanWasmRuntime")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root containing Kenbai and SekibanWasmRuntime.");
    }

    private static string ResolveExistingWasmModulePath()
    {
        string? envOverride = Environment.GetEnvironmentVariable("KENBAI_WASM_MODULE_PATH");
        if (!string.IsNullOrWhiteSpace(envOverride) && File.Exists(envOverride))
        {
            return envOverride;
        }

        string[] candidates =
        [
            Path.Combine(RepoRoot, "Kenbai", "artifacts", "aic-wasm-http-no-persist-fresh", "aic.wasm"),
            Path.Combine(RepoRoot, "Kenbai", "src", "Aic.Kenbai.EventSource.Wasm", "bin", "Release", "net10.0", "wasi-wasm", "native", "aic.wasm"),
            Path.Combine(RepoRoot, "Kenbai", "artifacts", "aic-wasm-http-no-persist", "aic.wasm"),
            Path.Combine(RepoRoot, "Kenbai", "artifacts", "aic-wasm", "aic.wasm"),
            Path.Combine(RepoRoot, "Kenbai", "artifacts", "aic-wasm-docker-fresh", "aic.wasm"),
            Path.Combine(RepoRoot, "Kenbai", "src", "Aic.Kenbai.EventSource.Wasm", "bin", "Debug", "net10.0", "wasi-wasm", "publish", "aic.wasm"),
            Path.Combine(RepoRoot, "Kenbai", "src", "Aic.Kenbai.EventSource.Wasm", "bin", "Debug", "net10.0", "wasi-wasm", "native", "aic.wasm"),
            Path.Combine(RepoRoot, "Kenbai", "artifacts", "aic-wasm-docker", "aic.wasm"),
            Path.Combine(RepoRoot, "Kenbai", "src", "Aic.Kenbai.EventSource.Wasm", "bin", "Debug", "net10.0", "wasi-wasm", "native.rebuild.1774312415", "aic.wasm"),
            Path.Combine(RepoRoot, "Kenbai", "src", "Aic.Kenbai.EventSource.Wasm", "bin", "Debug", "net10.0", "wasi-wasm", "native.preprobe.1774311659", "aic.wasm"),
            Path.Combine(RepoRoot, "Kenbai", "src", "Aic.Kenbai.EventSource.Wasm", "bin", "Debug", "net10.0", "wasi-wasm", "native.bak.1659", "aic.wasm"),
        ];

        string? existing = candidates
            .Where(File.Exists)
            .OrderByDescending(static path => File.GetLastWriteTimeUtc(path))
            .FirstOrDefault();
        if (existing is not null)
        {
            return existing;
        }

        return candidates[0];
    }

    private static string ReplayProjectionWasmInternal(
        string projectorName,
        IEnumerable<EventRow> rows,
        int? restoreAfterCount)
    {
        using var runtime = new WasmtimeRuntime();
        var moduleCache = new WasmtimeModuleCache(runtime);
        var host = new WasmtimePrimitiveProjectionHost(
            runtime,
            moduleCache,
            new WasmtimeHostOptions
            {
                ProjectorModulePaths = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [projectorName] = WasmModulePath
                }
            });

        using var initialInstance = host.CreateInstance(projectorName);
        var current = initialInstance;
        int index = 0;

        foreach (EventRow row in rows)
        {
            if (restoreAfterCount.HasValue && index == restoreAfterCount.Value)
            {
                string checkpoint = current.SerializeState();
                current.Dispose();
                current = host.CreateInstance(projectorName);
                current.RestoreState(checkpoint);
            }

            List<string> tagStrings = JsonSerializer.Deserialize<List<string>>(row.TagsJson) ?? [];
            current.ApplyEvent(row.EventType, row.PayloadJson, tagStrings, row.SortableUniqueId);
            index++;
        }

        string result = current.SerializeState();
        if (!ReferenceEquals(current, initialInstance))
        {
            current.Dispose();
        }

        return result;
    }

    private static string ExtractStateJsonFromSnapshotPayload(byte[] payloadBytes)
    {
        if (IsGzip(payloadBytes))
        {
            return DecompressGzipToString(payloadBytes);
        }

        string payloadJson = Encoding.UTF8.GetString(payloadBytes);
        JsonNode? payloadNode = JsonNode.Parse(payloadJson);
        string? compressedStateJson = payloadNode?["compressedStateJson"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(compressedStateJson))
        {
            byte[] compressedBytes = Convert.FromBase64String(compressedStateJson);
            return DecompressGzipToString(compressedBytes);
        }

        string? stateJson = payloadNode?["stateJson"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(stateJson))
        {
            return stateJson;
        }

        return payloadJson;
    }

    private static bool IsGzip(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 2 && bytes[0] == 0x1F && bytes[1] == 0x8B;

    private static string DecompressGzipToString(byte[] payloadBytes)
    {
        using var input = new MemoryStream(payloadBytes);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return Encoding.UTF8.GetString(output.ToArray());
    }

    private static JsonNode? CanonicalizeNode(JsonNode? node) =>
        node switch
        {
            null => null,
            JsonObject obj => CanonicalizeObject(obj),
            JsonArray array => CanonicalizeArray(array),
            _ => node.DeepClone()
        };

    private static JsonObject CanonicalizeObject(JsonObject obj)
    {
        var normalized = new JsonObject();
        foreach (KeyValuePair<string, JsonNode?> kv in obj.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            normalized[kv.Key] = CanonicalizeNode(kv.Value);
        }

        return normalized;
    }

    private static JsonArray CanonicalizeArray(JsonArray array)
    {
        var normalized = new JsonArray();
        foreach (JsonNode? item in array)
        {
            normalized.Add(CanonicalizeNode(item));
        }

        return normalized;
    }

    private static void RemoveRuntimeDiagnosticOnlyProperties(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                obj.Remove("debugView");
                foreach (KeyValuePair<string, JsonNode?> child in obj)
                {
                    RemoveRuntimeDiagnosticOnlyProperties(child.Value);
                }

                break;
            case JsonArray array:
                foreach (JsonNode? child in array)
                {
                    RemoveRuntimeDiagnosticOnlyProperties(child);
                }

                break;
        }
    }

    private static DcbDomainTypes CreateWasmDomainTypes()
    {
        System.Reflection.Assembly wasmAssembly = typeof(AicDomainJsonContext).Assembly;
        Type wasmDomainType = wasmAssembly.GetType("Aic.Kenbai.EventSource.Wasm.AicWasmDomainType")
            ?? throw new InvalidOperationException("Could not locate AicWasmDomainType.");
        System.Reflection.MethodInfo getDomainTypes = wasmDomainType.GetMethod(
            "GetDomainTypes",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not locate AicWasmDomainType.GetDomainTypes.");

        return (DcbDomainTypes)(getDomainTypes.Invoke(null, null)
            ?? throw new InvalidOperationException("AicWasmDomainType.GetDomainTypes returned null."));
    }
}
