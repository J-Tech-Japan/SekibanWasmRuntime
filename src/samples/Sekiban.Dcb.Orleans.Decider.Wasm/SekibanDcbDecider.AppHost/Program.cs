using Projects;
var builder = DistributedApplication.CreateBuilder(args);

var wasmModulePath = ResolveWasmModulePath();

// Add Azure Storage emulator for Orleans
var storage = builder
    .AddAzureStorage("azurestorage")
    // .RunAsEmulator(opt => opt.WithDataVolume());
    .RunAsEmulator();
var clusteringTable = storage.AddTables("DcbOrleansClusteringTable");
var grainTable = storage.AddTables("DcbOrleansGrainTable");
var grainStorage = storage.AddBlobs("DcbOrleansGrainState");
var queue = storage.AddQueues("DcbOrleansQueue");

// Add dedicated blob storage for MultiProjection snapshot offloading
var multiProjectionOffload = storage.AddBlobs("MultiProjectionOffload");

// Add PostgreSQL for event storage (optional - can use in-memory for development)
var postgresServer = builder
    .AddPostgres("dcbOrleansPostgres")
    // .WithPgAdmin()
    // .WithDataVolume()
    .WithDbGate();

// Sekiban event store database
var postgres = postgresServer.AddDatabase("DcbPostgres");

// Identity database (separate from Sekiban to avoid EnsureCreated conflicts)
var identityPostgres = postgresServer.AddDatabase("IdentityPostgres");

// Configure Orleans
var orleans = builder
    .AddOrleans("default")
    .WithClustering(clusteringTable)
    .WithGrainStorage("Default", grainStorage)
    .WithGrainStorage("OrleansStorage", grainStorage)
    .WithGrainStorage("dcb-orleans-queue", grainStorage)
    .WithGrainStorage("DcbOrleansGrainTable", grainTable)
    .WithStreaming(queue);

// Add the API Service
var apiService = builder
    .AddProject<SekibanDcbDecider_ApiService>("apiservice")
    .WithReference(postgres)
    .WithReference(identityPostgres)
    .WithReference(orleans)
    .WithReference(multiProjectionOffload)
    .WithEnvironment("WASM_MODULE_PATH", wasmModulePath)
    .WaitFor(postgres)
    .WaitFor(identityPostgres);

// Add the Web frontend
builder
    .AddProject<SekibanDcbDecider_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithReference(apiService)
    .WaitFor(apiService);

// Add the Next.js Web frontend (uses tRPC as BFF within Next.js)
builder
    .AddJavaScriptApp("webnext", "../SekibanDcbDecider.WebNext")
    .WithHttpEndpoint(port: 3000, env: "PORT")
    .WithExternalHttpEndpoints()
    .WithEnvironment("API_BASE_URL", apiService.GetEndpoint("http"))
    .WaitFor(apiService);

builder.Build().Run();

static string ResolveWasmModulePath()
{
    string? envPath = Environment.GetEnvironmentVariable("WASM_MODULE_PATH");
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
            "publish",
            "SekibanDcbDecider.Wasm.wasm")),
        Path.GetFullPath(Path.Combine(
            Directory.GetCurrentDirectory(),
            "..",
            "SekibanDcbDecider.Wasm",
            "bin",
            "Release",
            "net10.0",
            "wasi-wasm",
            "native",
            "SekibanDcbDecider.Wasm.wasm")),
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

    foreach (string candidate in candidates)
    {
        if (File.Exists(candidate))
        {
            return candidate;
        }
    }

    throw new InvalidOperationException(
        "WASM module not found. Set WASM_MODULE_PATH or build/publish SekibanDcbDecider.Wasm first.");
}
