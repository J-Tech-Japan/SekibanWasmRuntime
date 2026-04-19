using Projects;
using SekibanWasm.AppHostShared;
using System.Text.Json;
using System.Text.Json.Nodes;

var benchmarkProfile = Environment.GetEnvironmentVariable("BENCHMARK_PROFILE");
var isStrictBenchmarkProfile = string.Equals(benchmarkProfile, "tagstategrain-memory", StringComparison.OrdinalIgnoreCase);
var projectionMode = (Environment.GetEnvironmentVariable("SEKIBAN_PROJECTION_MODE") ?? "dual").Trim().ToLowerInvariant();
var materializedViewEnabled = projectionMode is "dual" or "materialized-view-only";
var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions
{
    Args = args,
    DisableDashboard = isStrictBenchmarkProfile,
    EnableResourceLogging = isStrictBenchmarkProfile
});

var storage = builder
    .AddAzureStorage("azurestorage")
    .RunAsEmulator();
var apiClusteringTable = storage.AddTables("DcbOrleansClusteringTable");
var apiGrainTable = storage.AddTables("DcbOrleansGrainTable");
var apiGrainStorage = storage.AddBlobs("DcbOrleansGrainState");
var apiQueue = storage.AddQueues("DcbOrleansQueue");
var multiProjectionOffload = storage.AddBlobs("MultiProjectionOffload");

var postgresServer = builder
    .AddPostgres("sekibanTsPostgres");
var postgres = postgresServer.AddDatabase("SekibanTsDb");
var dcbPostgres = postgresServer.AddDatabase("DcbPostgres");
var identityPostgres = postgresServer.AddDatabase("IdentityPostgres");
var dcbMaterializedViewPostgres = materializedViewEnabled
    ? postgresServer.AddDatabase("DcbMaterializedViewPostgres")
    : null;

var apiOrleans = builder
    .AddOrleans("api-orleans")
    .WithClustering(apiClusteringTable)
    .WithGrainStorage("Default", apiGrainStorage)
    .WithGrainStorage("OrleansStorage", apiGrainStorage)
    .WithGrainStorage("dcb-orleans-queue", apiGrainStorage)
    .WithGrainStorage("DcbOrleansGrainTable", apiGrainTable)
    .WithStreaming(apiQueue);

var dbGatePort = AppHostInfrastructure.ResolveConfiguredPort(7310, "E2E_DBGATE_PORT", "DBGATE_PORT");
builder.AddDbGateForPostgres(
    name: "dbgate",
    postgresDatabase: postgres,
    label: "Dcb Decider Ts Postgres",
    hostPort: dbGatePort);

var tsWasmModulePath = ResolveTsWasmModulePath();
var tsManifestPath = ResolveTsManifestPath(tsWasmModulePath);
var apiServicePort = AppHostInfrastructure.ResolveConfiguredPort(7207, "E2E_API_SERVICE_PORT", "API_SERVICE_PORT");

var wasmServerBuilder = builder
    .AddProject<Sekiban_Dcb_WasmRuntime_Host>("wasmserver")
    .WithEnvironment("SEKIBAN_STORAGE_PROVIDER", "postgres")
    .WithEnvironment("WASM_MODULE_PATH", tsWasmModulePath)
    .WithEnvironment("SEKIBAN_MANIFEST_PATH", tsManifestPath)
    .WithEnvironment("SEKIBAN_WASM_CATCHUP_CONCURRENCY", "4")
    .WithEnvironment("SEKIBAN_WASM_MULTIPROJECTION_CATCHUP_BATCH_SIZE", "250")
    .WithEnvironment("SEKIBAN_WASM_AUTO_COMPACTION_INTERVAL_EVENTS", "20000")
    .WithEnvironment("SEKIBAN_WASM_FORCE_COMPACTING_GC_AFTER_COMPACTION", "true")
    .WithEnvironment("SEKIBAN_WASMTIME_STATIC_MEMORY_MAX_MB", "192")
    .WithEnvironment("WASM_RUNTIME_ALLOWED_TAG_EVENT_TYPES__RoomProjector", "RoomCreated,RoomUpdated,RoomDeactivated,RoomReactivated")
    .WithReference(postgres, "SekibanDcb")
    .WaitFor(postgres)
    .WithEnvironment("SEKIBAN_PROJECTION_MODE", projectionMode)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development");

if (dcbMaterializedViewPostgres is not null)
{
    wasmServerBuilder = wasmServerBuilder
        .WithReference(dcbMaterializedViewPostgres, "DcbMaterializedViewPostgres")
        .WaitFor(dcbMaterializedViewPostgres);
}

if (isStrictBenchmarkProfile)
{
    wasmServerBuilder = wasmServerBuilder
        .WithEnvironment("SEKIBAN_DIRECT_SNAPSHOT_QUERY_ENABLED", "false")
        .WithEnvironment("SEKIBAN_TAG_STATE_FAST_PATH_ENABLED", "false");
}

var wasmApiPort = AppHostInfrastructure.ResolveConfiguredPort(7209, "E2E_API_PORT");
wasmServerBuilder = wasmServerBuilder
    .WithEndpoint("http", endpoint =>
    {
        endpoint.Port = wasmApiPort;
        endpoint.TargetPort = wasmApiPort;
        endpoint.UriScheme = "http";
        endpoint.IsProxied = false;
    })
    .WithEnvironment("ASPNETCORE_URLS", "http://127.0.0.1:" + wasmApiPort);

var wasmServer = wasmServerBuilder;

var apiServiceBuilder = builder
    .AddProject<SekibanDcbDecider_ApiService>("apiservice")
    .WithReference(dcbPostgres)
    .WithReference(identityPostgres)
    .WithReference(apiOrleans)
    .WithReference(multiProjectionOffload)
    .WaitFor(dcbPostgres)
    .WaitFor(identityPostgres);

if (isStrictBenchmarkProfile)
{
    apiServiceBuilder = apiServiceBuilder
        .WithEnvironment("Orleans__UseInMemoryStreams", "true")
        .WithEnvironment("Orleans__UseInMemoryGrainStorage", "true");
}

apiServiceBuilder = apiServiceBuilder
    .WithEndpoint("http", endpoint =>
    {
        endpoint.Port = apiServicePort;
        endpoint.TargetPort = apiServicePort;
        endpoint.UriScheme = "http";
        endpoint.IsProxied = false;
    })
    .WithEnvironment("ASPNETCORE_URLS", "http://127.0.0.1:" + apiServicePort);

var apiService = apiServiceBuilder;

// TS ClientAPI - run the TypeScript server via npx tsx
var tsClientApiDir = Path.GetFullPath(Path.Combine(
    builder.AppHostDirectory,
    "..",
    "ts-clientapi"));

// The TS ClientApi pulls in pg, @sekiban/ts etc. via npm. Install them before the
// server starts so a fresh checkout / CI run doesn't fail with ERR_MODULE_NOT_FOUND.
var clientApiInstaller = builder
    .AddExecutable(
        "clientapi-installer",
        "sh",
        tsClientApiDir,
        new[] { "-c", "if [ -d node_modules ]; then exit 0; fi; npm ci" });

var clientApiBuilder = builder
    .AddExecutable(
        "clientapi",
        "npx",
        tsClientApiDir,
        new[] { "tsx", "src/server.ts" })
    .WithEnvironment("WASM_SERVER_URL", "http://127.0.0.1:" + wasmApiPort)
    .WaitForCompletion(clientApiInstaller);

var clientApiPort = AppHostInfrastructure.ResolveConfiguredPort(7208, "E2E_CLIENT_API_PORT");
clientApiBuilder = clientApiBuilder.WithHttpEndpoint(
    targetPort: clientApiPort,
    port: clientApiPort,
    env: "PORT",
    isProxied: false);

clientApiBuilder = clientApiBuilder
    .WithEnvironment("SEKIBAN_PROJECTION_MODE", projectionMode)
    .WithReference(wasmServer)
    .WaitFor(wasmServer);

if (dcbMaterializedViewPostgres is not null)
{
    clientApiBuilder = clientApiBuilder
        .WithReference(dcbMaterializedViewPostgres, "DcbMaterializedViewPostgres")
        .WaitFor(dcbMaterializedViewPostgres);
}

var clientApi = clientApiBuilder;

var webFrontend = builder
    .AddProject<SekibanDcbDecider_Web>("webfrontend")
    .WithReference(clientApi.GetEndpoint("http"))
    .WithReference(apiService.GetEndpoint("http"))
    .WithEnvironment("CLIENT_API_URL", "http://127.0.0.1:" + clientApiPort)
    .WithEnvironment("AUTH_API_URL", "http://127.0.0.1:" + apiServicePort)
    .WaitFor(clientApi);

var webPort = AppHostInfrastructure.ResolveConfiguredPort(7190, "E2E_WEB_PORT");
webFrontend
    .WithEndpoint("http", endpoint =>
    {
        endpoint.Port = webPort;
        endpoint.TargetPort = webPort;
        endpoint.UriScheme = "http";
        endpoint.IsProxied = false;
    })
    .WithEnvironment("ASPNETCORE_URLS", "http://127.0.0.1:" + webPort);
webFrontend.WithExternalHttpEndpoints();

var webNextPort = AppHostInfrastructure.ResolveConfiguredPort(3000, "E2E_WEBNEXT_PORT", "WEBNEXT_PORT");
builder
    .AddJavaScriptApp("webnext", "../SekibanDcbDecider.WebNext")
    .WithHttpEndpoint(port: webNextPort, env: "PORT")
    .WithExternalHttpEndpoints()
    .WithEnvironment("NODE_ENV", "development")
    .WithEnvironment("API_BASE_URL", "http://127.0.0.1:" + apiServicePort)
    .WithEnvironment("CLIENT_API_BASE_URL", "http://127.0.0.1:" + clientApiPort)
    .WaitFor(apiService)
    .WaitFor(clientApi);

builder.Build().Run();

static string ResolveTsWasmModulePath()
{
    string? envPath = Environment.GetEnvironmentVariable("TS_WASM_MODULE_PATH")
        ?? Environment.GetEnvironmentVariable("WASM_MODULE_PATH");
    if (!string.IsNullOrWhiteSpace(envPath))
    {
        return Path.IsPathRooted(envPath)
            ? envPath
            : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), envPath));
    }

    string[] candidates =
    [
        Path.GetFullPath(Path.Combine(
            Directory.GetCurrentDirectory(),
            "..",
            "modules",
            "ts-weather.wasm"))
    ];

    foreach (string candidate in candidates)
    {
        if (File.Exists(candidate))
        {
            return candidate;
        }
    }

    throw new InvalidOperationException(
        "TS WASM module not found. Set TS_WASM_MODULE_PATH or build ts-wasm first.");
}

static string ResolveTsManifestPath(string wasmModulePath)
{
    string? envPath = Environment.GetEnvironmentVariable("TS_WASM_MANIFEST_PATH")
        ?? Environment.GetEnvironmentVariable("SEKIBAN_MANIFEST_PATH");
    if (!string.IsNullOrWhiteSpace(envPath))
    {
        string resolved = Path.IsPathRooted(envPath)
            ? envPath
            : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), envPath));
        if (File.Exists(resolved))
        {
            return resolved;
        }
    }

    string candidate = Path.GetFullPath(Path.Combine(
        Directory.GetCurrentDirectory(),
        "..",
        "modules",
        "sekiban-runtime-manifest.json"));
    if (!File.Exists(candidate))
    {
        throw new InvalidOperationException(
            "TS WASM manifest not found. Set TS_WASM_MANIFEST_PATH or provide modules/sekiban-runtime-manifest.json.");
    }

    JsonNode root = JsonNode.Parse(File.ReadAllText(candidate))
        ?? throw new InvalidOperationException($"Failed to parse manifest template at '{candidate}'.");
    root["defaultModulePath"] = wasmModulePath;
    if (root["projectors"] is JsonArray projectors)
    {
        foreach (JsonNode? projector in projectors)
        {
            if (projector is JsonObject projectorObject)
            {
                projectorObject["modulePath"] = wasmModulePath;
            }
        }
    }

    string generatedDirectory = Path.Combine(Directory.GetCurrentDirectory(), ".generated");
    Directory.CreateDirectory(generatedDirectory);
    string generatedPath = Path.Combine(generatedDirectory, "sekiban-runtime-manifest.generated.json");
    File.WriteAllText(generatedPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    return generatedPath;
}
