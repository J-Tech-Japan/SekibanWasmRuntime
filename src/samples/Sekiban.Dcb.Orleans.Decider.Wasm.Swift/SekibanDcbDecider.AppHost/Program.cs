using Projects;
using SekibanWasm.AppHostShared;
using System.Text.Json;
using System.Text.Json.Nodes;

// Swift sample AppHost. Minimal topology compared to the Rust sample's AppHost:
//   * Postgres (event store + MV)
//   * Azure Storage emulator (Orleans clustering/grain/queue)
//   * generic wasmserver (Sekiban.Dcb.WasmRuntime.Host) pointed at the Swift .wasm
//   * Swift Hummingbird ClientApi exposing /api/mv/* against DcbMaterializedViewPostgres
//
// The smoke-test write path is a bash script (`build/scripts/seed-swift-mv.sh`) that POSTs
// SerializableCommitRequest payloads at wasmserver directly — the Swift ClientApi is
// intentionally read-only per the issue's acceptable scope reduction.

var builder = DistributedApplication.CreateBuilder(args);

var storage = builder
    .AddAzureStorage("azurestorage")
    .RunAsEmulator();
var clusteringTable = storage.AddTables("DcbOrleansClusteringTable");
var grainStorage = storage.AddBlobs("DcbOrleansGrainState");

var postgresServer = builder.AddPostgres("sekibanSwiftPostgres");
var postgres = postgresServer.AddDatabase("SekibanSwiftDb");
var dcbMaterializedViewPostgres = postgresServer.AddDatabase("DcbMaterializedViewPostgres");

var dbGatePort = AppHostInfrastructure.ResolveConfiguredPort(6400, "E2E_DBGATE_PORT", "DBGATE_PORT");
builder.AddDbGateForPostgres(
    name: "dbgate",
    postgresDatabase: postgres,
    label: "Dcb Decider Swift Postgres",
    hostPort: dbGatePort);

var swiftWasmModulePath = ResolveSwiftWasmModulePath();
var swiftManifestPath = ResolveSwiftManifestPath(swiftWasmModulePath);

var wasmApiPort = AppHostInfrastructure.ResolveConfiguredPort(6299, "E2E_API_PORT");
var wasmServer = builder
    .AddProject<Sekiban_Dcb_WasmRuntime_Host>("wasmserver")
    .WithEnvironment("SEKIBAN_STORAGE_PROVIDER", "postgres")
    .WithEnvironment("WASM_MODULE_PATH", swiftWasmModulePath)
    .WithEnvironment("SEKIBAN_MANIFEST_PATH", swiftManifestPath)
    .WithEnvironment("SEKIBAN_WASM_CATCHUP_CONCURRENCY", "4")
    .WithEnvironment("SEKIBAN_WASM_MULTIPROJECTION_CATCHUP_BATCH_SIZE", "250")
    // Swift's wasm binary is ~60MB; raise the Wasmtime static-memory limit so instance init
    // doesn't trap on linear-memory growth.
    .WithEnvironment("SEKIBAN_WASMTIME_STATIC_MEMORY_MAX_MB", "256")
    .WithReference(postgres, "SekibanDcb")
    .WithReference(dcbMaterializedViewPostgres, "DcbMaterializedViewPostgres")
    .WaitFor(postgres)
    .WaitFor(dcbMaterializedViewPostgres)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEndpoint("http", endpoint =>
    {
        endpoint.Port = wasmApiPort;
        endpoint.TargetPort = wasmApiPort;
        endpoint.UriScheme = "http";
        endpoint.IsProxied = false;
    })
    .WithEnvironment("ASPNETCORE_URLS", "http://127.0.0.1:" + wasmApiPort);

// Swift ClientApi executable (Hummingbird + PostgresNIO). We shell out to `swift run -c release`
// against the SwiftPM package so Aspire rebuilds it automatically when sources change. For a
// deployment build, swap to a pre-built binary path.
var swiftClientApiDir = Path.GetFullPath(Path.Combine(
    builder.AppHostDirectory,
    "..",
    "SekibanDcbDecider.Swift.ClientApi"));
var swiftToolchain = ResolveSwiftExecutable();

var clientApiPort = AppHostInfrastructure.ResolveConfiguredPort(6298, "E2E_CLIENT_API_PORT");
var clientApiBuilder = builder
    .AddExecutable(
        "clientapi",
        swiftToolchain,
        swiftClientApiDir,
        new[] { "run", "-c", "release" })
    .WithEnvironment("WASM_SERVER_URL", "http://127.0.0.1:" + wasmApiPort)
    .WithHttpEndpoint(
        targetPort: clientApiPort,
        port: clientApiPort,
        env: "PORT",
        isProxied: false)
    .WithReference(wasmServer)
    .WithReference(dcbMaterializedViewPostgres, "DcbMaterializedViewPostgres")
    .WaitFor(wasmServer)
    .WaitFor(dcbMaterializedViewPostgres);

// Ensure the Swift binary can find swiftly's toolchain if Aspire inherits a sanitized PATH.
var parentPath = Environment.GetEnvironmentVariable("PATH");
if (!string.IsNullOrEmpty(parentPath))
{
    clientApiBuilder = clientApiBuilder.WithEnvironment("PATH", parentPath);
}

builder.Build().Run();

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

static string ResolveSwiftWasmModulePath()
{
    string? envPath = Environment.GetEnvironmentVariable("SWIFT_WASM_MODULE_PATH")
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
            "sekiban-dcb-decider-swift.wasm")),
        Path.GetFullPath(Path.Combine(
            Directory.GetCurrentDirectory(),
            "..",
            "SekibanDcbDecider.Swift.Wasm",
            ".build",
            "wasm32-unknown-wasip1",
            "release",
            "SekibanDcbDeciderSwiftWasm.wasm")),
    ];

    foreach (string candidate in candidates)
    {
        if (File.Exists(candidate))
        {
            return candidate;
        }
    }

    throw new InvalidOperationException(
        "Swift WASM module not found. Set SWIFT_WASM_MODULE_PATH or run build/scripts/build-swift-wasm.sh first.");
}

static string ResolveSwiftManifestPath(string wasmModulePath)
{
    string? envPath = Environment.GetEnvironmentVariable("SWIFT_WASM_MANIFEST_PATH")
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
            "Swift WASM manifest not found. Provide modules/sekiban-runtime-manifest.json.");
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

// Aspire spawns the clientapi via a child process. When it inherits a sanitized PATH (common on
// macOS GUI launches), `swift` from swiftly may be missing. Prefer the absolute path if a
// swiftly install is detected.
static string ResolveSwiftExecutable()
{
    string? envSwift = Environment.GetEnvironmentVariable("SWIFT_BIN");
    if (!string.IsNullOrWhiteSpace(envSwift) && File.Exists(envSwift)) return envSwift;
    string home = Environment.GetEnvironmentVariable("HOME") ?? "";
    string swiftly = Path.Combine(home, ".swiftly", "bin", "swift");
    if (File.Exists(swiftly)) return swiftly;
    return "swift";
}
