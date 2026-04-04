using Projects;
using System.Text.Json;
using System.Text.Json.Nodes;

var builder = DistributedApplication.CreateBuilder(args);

var postgresServer = builder
    .AddPostgres("dcbOrleansPostgres")
    .WithDbGate();
var wasmPostgres = postgresServer.AddDatabase("SekibanCSharpDb");

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
    .WithEnvironment("SEKIBAN_WASM_FORCE_COMPACTING_GC_AFTER_COMPACTION", "true")
    .WithEnvironment("SEKIBAN_WASMTIME_STATIC_MEMORY_MAX_MB", "192")
    .WithEnvironment("WASM_RUNTIME_ALLOWED_TAG_EVENT_TYPES__RoomProjector", "RoomCreated,RoomUpdated,RoomDeactivated,RoomReactivated")
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
