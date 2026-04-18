using Projects;
using SekibanWasm.AppHostShared;
using System.Text.Json;
using System.Text.Json.Nodes;

var benchmarkProfile = Environment.GetEnvironmentVariable("BENCHMARK_PROFILE");
var isStrictBenchmarkProfile = string.Equals(benchmarkProfile, "tagstategrain-memory", StringComparison.OrdinalIgnoreCase);
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
    .AddPostgres("sekibanGoPostgres");
var postgres = postgresServer.AddDatabase("SekibanGoDb");
var dcbPostgres = postgresServer.AddDatabase("DcbPostgres");
var identityPostgres = postgresServer.AddDatabase("IdentityPostgres");
// Materialized view Postgres — Go WASM module emits SQL through mv_initialize / mv_apply_event,
// Sekiban's PostgresMvExecutor inside wasmserver runs it here. Go ClientApi reads the projected
// tables directly from this DB; the generic runtime host never learns application schema.
var dcbMaterializedViewPostgres = postgresServer.AddDatabase("DcbMaterializedViewPostgres");

var apiOrleans = builder
    .AddOrleans("api-orleans")
    .WithClustering(apiClusteringTable)
    .WithGrainStorage("Default", apiGrainStorage)
    .WithGrainStorage("OrleansStorage", apiGrainStorage)
    .WithGrainStorage("dcb-orleans-queue", apiGrainStorage)
    .WithGrainStorage("DcbOrleansGrainTable", apiGrainTable)
    .WithStreaming(apiQueue);

var dbGatePort = AppHostInfrastructure.ResolveConfiguredPort(7300, "E2E_DBGATE_PORT", "DBGATE_PORT");
builder.AddDbGateForPostgres(
    name: "dbgate",
    postgresDatabase: postgres,
    label: "Dcb Decider Go Postgres",
    hostPort: dbGatePort);

var goWasmModulePath = ResolveGoWasmModulePath();
var goManifestPath = ResolveGoManifestPath(goWasmModulePath);
var apiServicePort = AppHostInfrastructure.ResolveConfiguredPort(7197, "E2E_API_SERVICE_PORT", "API_SERVICE_PORT");

var wasmServerBuilder = builder
    .AddProject<Sekiban_Dcb_WasmRuntime_Host>("wasmserver")
    .WithEnvironment("SEKIBAN_STORAGE_PROVIDER", "postgres")
    .WithEnvironment("WASM_MODULE_PATH", goWasmModulePath)
    .WithEnvironment("SEKIBAN_MANIFEST_PATH", goManifestPath)
    .WithEnvironment("SEKIBAN_WASM_CATCHUP_CONCURRENCY", "4")
    .WithEnvironment("SEKIBAN_WASM_MULTIPROJECTION_CATCHUP_BATCH_SIZE", "250")
    .WithEnvironment("SEKIBAN_WASM_AUTO_COMPACTION_INTERVAL_EVENTS", "20000")
    .WithEnvironment("SEKIBAN_WASM_FORCE_COMPACTING_GC_AFTER_COMPACTION", "true")
    .WithEnvironment("SEKIBAN_WASMTIME_STATIC_MEMORY_MAX_MB", "512")
    .WithEnvironment("SEKIBAN_WASM_POOL_SIZE", "0")
    .WithReference(postgres, "SekibanDcb")
    .WithReference(dcbMaterializedViewPostgres, "DcbMaterializedViewPostgres")
    .WaitFor(postgres)
    .WaitFor(dcbMaterializedViewPostgres)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development");

if (isStrictBenchmarkProfile)
{
    wasmServerBuilder = wasmServerBuilder
        .WithEnvironment("SEKIBAN_DIRECT_SNAPSHOT_QUERY_ENABLED", "false")
        .WithEnvironment("SEKIBAN_TAG_STATE_FAST_PATH_ENABLED", "false");
}

var wasmApiPort = AppHostInfrastructure.ResolveConfiguredPort(7199, "E2E_API_PORT");
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

// Go ClientAPI - build and run the Go executable
var goClientApiDir = Path.GetFullPath(Path.Combine(
    builder.AppHostDirectory,
    "..",
    "go-clientapi"));

var clientApiBuilder = builder
    .AddExecutable(
        "clientapi",
        "go",
        goClientApiDir,
        new[] { "run", "." })
    .WithEnvironment("WASM_SERVER_URL", "http://127.0.0.1:" + wasmApiPort);

var clientApiPort = AppHostInfrastructure.ResolveConfiguredPort(7198, "E2E_CLIENT_API_PORT");
clientApiBuilder = clientApiBuilder.WithHttpEndpoint(
    targetPort: clientApiPort,
    port: clientApiPort,
    env: "PORT",
    isProxied: false);

var clientApi = clientApiBuilder
    .WithReference(wasmServer)
    .WithReference(dcbMaterializedViewPostgres, "DcbMaterializedViewPostgres")
    .WaitFor(wasmServer)
    .WaitFor(dcbMaterializedViewPostgres);

var webFrontend = builder
    .AddProject<SekibanDcbDecider_Web>("webfrontend")
    .WithReference(clientApi.GetEndpoint("http"))
    .WithReference(apiService.GetEndpoint("http"))
    .WithEnvironment("CLIENT_API_URL", "http://127.0.0.1:" + clientApiPort)
    .WithEnvironment("AUTH_API_URL", "http://127.0.0.1:" + apiServicePort)
    .WaitFor(clientApi);

var webPort = AppHostInfrastructure.ResolveConfiguredPort(7180, "E2E_WEB_PORT");
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

builder.Build().Run();

static string ResolveGoWasmModulePath()
{
    string? envPath = Environment.GetEnvironmentVariable("GO_WASM_MODULE_PATH")
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
            "go-weather.wasm"))
    ];

    foreach (string candidate in candidates)
    {
        if (File.Exists(candidate))
        {
            return candidate;
        }
    }

    throw new InvalidOperationException(
        "Go WASM module not found. Set GO_WASM_MODULE_PATH or build go-wasm first.");
}

static string ResolveGoManifestPath(string wasmModulePath)
{
    string? envPath = Environment.GetEnvironmentVariable("GO_WASM_MANIFEST_PATH")
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
            "Go WASM manifest not found. Set GO_WASM_MANIFEST_PATH or provide modules/sekiban-runtime-manifest.json.");
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
