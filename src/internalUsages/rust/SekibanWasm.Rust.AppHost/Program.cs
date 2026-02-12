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

var wasmModulePath = Environment.GetEnvironmentVariable("WASM_MODULE_PATH")
    ?? throw new InvalidOperationException(
        "WASM_MODULE_PATH environment variable is required. " +
        "Set it to the absolute path of the Rust .wasm module.");

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
