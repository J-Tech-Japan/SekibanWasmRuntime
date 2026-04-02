using Projects;
using System.Text.Json;
using System.Text.Json.Nodes;

var builder = DistributedApplication.CreateBuilder(args);

var apiServicePort = ResolveConfiguredPort(5141, "E2E_API_SERVICE_PORT", "API_SERVICE_PORT");

var storage = builder
    .AddAzureStorage("azurestorage")
    .RunAsEmulator();
var clusteringTable = storage.AddTables("DcbOrleansClusteringTable");
var grainTable = storage.AddTables("DcbOrleansGrainTable");
var grainStorage = storage.AddBlobs("DcbOrleansGrainState");
var queue = storage.AddQueues("DcbOrleansQueue");
var multiProjectionOffload = storage.AddBlobs("MultiProjectionOffload");

var postgresServer = builder
    .AddPostgres("dcbOrleansPostgres")
    .WithDbGate();
var postgres = postgresServer.AddDatabase("DcbPostgres");
var identityPostgres = postgresServer.AddDatabase("IdentityPostgres");
var wasmPostgres = postgresServer.AddDatabase("SekibanCSharpDb");

var orleans = builder
    .AddOrleans("default")
    .WithClustering(clusteringTable)
    .WithGrainStorage("Default", grainStorage)
    .WithGrainStorage("OrleansStorage", grainStorage)
    .WithGrainStorage("dcb-orleans-queue", grainStorage)
    .WithGrainStorage("DcbOrleansGrainTable", grainTable)
    .WithStreaming(queue);

var csharpWasmModulePath = ResolveCSharpWasmModulePath();
var csharpManifestPath = ResolveCSharpManifestPath(csharpWasmModulePath);
var wasmApiPort = ResolveConfiguredPort(5199, "E2E_WASM_PORT");
var wasmServerBuilder = builder
    .AddProject<Sekiban_Dcb_WasmRuntime_Host>("wasmserver")
    .WithEnvironment("SEKIBAN_STORAGE_PROVIDER", "postgres")
    .WithEnvironment("WASM_MODULE_PATH", csharpWasmModulePath)
    .WithEnvironment("SEKIBAN_MANIFEST_PATH", csharpManifestPath)
    .WithEnvironment("SEKIBAN_WASM_CATCHUP_CONCURRENCY", "4")
    .WithEnvironment("SEKIBAN_WASM_MULTIPROJECTION_CATCHUP_BATCH_SIZE", "250")
    .WithEnvironment("SEKIBAN_WASM_AUTO_COMPACTION_INTERVAL_EVENTS", "20000")
    .WithEnvironment("SEKIBAN_WASMTIME_STATIC_MEMORY_MAX_MB", "192")
    .WithReference(wasmPostgres, "SekibanDcb")
    .WaitFor(wasmPostgres)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development");

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
    .WithReference(postgres)
    .WithReference(identityPostgres)
    .WithReference(orleans)
    .WithReference(multiProjectionOffload)
    .WaitFor(postgres)
    .WaitFor(identityPostgres);

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

var clientApiPort = ResolveConfiguredPort(5198, "E2E_CLIENT_API_PORT");
var clientApiBuilder = builder
    .AddProject<SekibanDcbDecider_ClientApi>("clientapi")
    .WithReference(wasmServer)
    .WaitFor(wasmServer);

clientApiBuilder = clientApiBuilder
    .WithEndpoint("http", endpoint =>
    {
        endpoint.Port = clientApiPort;
        endpoint.TargetPort = clientApiPort;
        endpoint.UriScheme = "http";
        endpoint.IsProxied = false;
    })
    .WithEnvironment("ASPNETCORE_URLS", "http://127.0.0.1:" + clientApiPort)
    .WithEnvironment("WASM_SERVER_URL", "http://127.0.0.1:" + wasmApiPort);

var clientApi = clientApiBuilder;

var webPort = ResolveConfiguredPort(5180, "E2E_WEB_PORT");
var webFrontend = builder
    .AddProject<SekibanDcbDecider_Web>("webfrontend")
    .WithReference(clientApi.GetEndpoint("http"))
    .WithReference(apiService.GetEndpoint("http"))
    .WithEnvironment("CLIENT_API_URL", "http://127.0.0.1:" + clientApiPort)
    .WithEnvironment("AUTH_API_URL", "http://127.0.0.1:" + apiServicePort)
    .WaitFor(clientApi)
    .WaitFor(apiService);

webFrontend
    .WithEndpoint("http", endpoint =>
    {
        endpoint.Port = webPort;
        endpoint.TargetPort = webPort;
        endpoint.UriScheme = "http";
        endpoint.IsProxied = false;
    })
    .WithExternalHttpEndpoints()
    .WithEnvironment("ASPNETCORE_URLS", "http://127.0.0.1:" + webPort);

var webNextPort = ResolveConfiguredPort(3000, "E2E_WEBNEXT_PORT", "WEBNEXT_PORT");
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

static int ResolveConfiguredPort(int defaultPort, params string[] envNames)
{
    foreach (string envName in envNames)
    {
        string? value = Environment.GetEnvironmentVariable(envName);
        if (int.TryParse(value, out int port))
        {
            return port;
        }
    }

    return defaultPort;
}

static string ResolveCSharpWasmModulePath()
{
    string? envPath = Environment.GetEnvironmentVariable("CS_WASM_MODULE_PATH")
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
            "sekiban-dcb-decider.wasm")),
        Path.GetFullPath(Path.Combine(
            Directory.GetCurrentDirectory(),
            "..",
            "SekibanDcbDecider.Wasm",
            "bin",
            "Release",
            "net10.0",
            "wasi-wasm",
            "native",
            "SekibanWasm.Cs.Wasm.wasm"))
    ];

    foreach (var candidate in candidates)
    {
        if (File.Exists(candidate))
        {
            return candidate;
        }
    }

    throw new InvalidOperationException(
        "C# WASM module not found. Set CS_WASM_MODULE_PATH or build SekibanDcbDecider.Wasm first.");
}

static string ResolveCSharpManifestPath(string wasmModulePath)
{
    string? envPath = Environment.GetEnvironmentVariable("CS_WASM_MANIFEST_PATH")
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
            "C# WASM manifest not found. Set CS_WASM_MANIFEST_PATH or provide modules/sekiban-runtime-manifest.json.");
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
