using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var storage = builder
    .AddAzureStorage("azurestorage")
    .RunAsEmulator();
var clusteringTable = storage.AddTables("SekibanRustClusteringTable");
var grainStorage = storage.AddBlobs("SekibanRustGrainState");
var queue = storage.AddQueues("SekibanRustQueue");

var postgres = builder
    .AddPostgres("sekibanRustPostgres")
    .AddDatabase("SekibanRustDb");

var orleans = builder
    .AddOrleans("default")
    .WithClustering(clusteringTable)
    .WithGrainStorage("Default", grainStorage)
    .WithStreaming(queue);

// Prefer explicit env var, but allow the default repo-relative path for local dev.
var wasmModulePathRaw = Environment.GetEnvironmentVariable("WASM_MODULE_PATH");
var wasmModulePath = string.IsNullOrWhiteSpace(wasmModulePathRaw)
    ? Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "modules", "rust-weather.wasm"))
    : (Path.IsPathRooted(wasmModulePathRaw)
        ? wasmModulePathRaw
        : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), wasmModulePathRaw)));

if (!File.Exists(wasmModulePath))
{
    throw new InvalidOperationException(
        $"WASM module not found at '{wasmModulePath}'. " +
        "Set WASM_MODULE_PATH to the path of the Rust .wasm module, or build it via ./build/scripts/build-rust-wasm.sh.");
}

var apiService = builder
    .AddProject<SekibanWasm_Rust_ApiService>("apiservice")
    .WithReference(postgres)
    .WithReference(orleans)
    .WaitFor(postgres)
    .WithEnvironment("Wasm__DefaultModulePath", wasmModulePath);

var e2eApiPort = Environment.GetEnvironmentVariable("E2E_API_PORT");
if (!string.IsNullOrWhiteSpace(e2eApiPort))
{
    // Used by scripts/e2e-aspire-smoke.sh (curl expects http://127.0.0.1:${E2E_API_PORT})
    apiService.WithEnvironment("ASPNETCORE_URLS", $"http://127.0.0.1:{e2eApiPort}");
}

builder
    .AddProject<SekibanWasm_Rust_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithReference(apiService)
    .WaitFor(apiService);

builder.Build().Run();
