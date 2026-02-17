using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var storage = builder
    .AddAzureStorage("azurestorage")
    .RunAsEmulator();
var clusteringTable = storage.AddTables("SekibanCsClusteringTable");
var grainStorage = storage.AddBlobs("SekibanCsGrainState");
var queue = storage.AddQueues("SekibanCsQueue");

var postgres = builder
    .AddPostgres("sekibanCsPostgres")
    .AddDatabase("SekibanCsDb");

var orleans = builder
    .AddOrleans("default")
    .WithClustering(clusteringTable)
    .WithGrainStorage("Default", grainStorage)
    .WithStreaming(queue);

// Prefer explicit env var, but allow the default repo-relative path for local dev.
var wasmModulePathRaw = Environment.GetEnvironmentVariable("WASM_MODULE_PATH");
var wasmModulePath = string.IsNullOrWhiteSpace(wasmModulePathRaw)
    ? Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "modules", "csharp-weather.wasm"))
    : (Path.IsPathRooted(wasmModulePathRaw)
        ? wasmModulePathRaw
        : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), wasmModulePathRaw)));

if (!File.Exists(wasmModulePath))
{
    throw new InvalidOperationException(
        $"WASM module not found at '{wasmModulePath}'. " +
        "Set WASM_MODULE_PATH to the path of the C# .wasm module, or build it via ./build/scripts/build-csharp-wasm.sh.");
}

var wasmServerBuilder = builder
    .AddProject<SekibanWasm_Cs_WasmServer>("wasmserver")
    .WithReference(postgres)
    .WithReference(orleans)
    .WaitFor(postgres)
    .WithEnvironment("Wasm__DefaultModulePath", wasmModulePath);

var e2eApiPort = Environment.GetEnvironmentVariable("E2E_API_PORT");
if (!string.IsNullOrWhiteSpace(e2eApiPort))
{
    wasmServerBuilder = wasmServerBuilder.WithHttpEndpoint(port: int.Parse(e2eApiPort), env: "ASPNETCORE_URLS");
}
else
{
    wasmServerBuilder = wasmServerBuilder.WithHttpEndpoint(env: "ASPNETCORE_URLS");
}

var wasmServer = wasmServerBuilder;

var clientApiBuilder = builder
    .AddProject<SekibanWasm_Cs_ClientApi>("clientapi");

var e2eClientApiPort = Environment.GetEnvironmentVariable("E2E_CLIENT_API_PORT");
if (!string.IsNullOrWhiteSpace(e2eClientApiPort))
{
    clientApiBuilder = clientApiBuilder.WithHttpEndpoint(port: int.Parse(e2eClientApiPort), env: "ASPNETCORE_URLS");
}
else
{
    clientApiBuilder = clientApiBuilder.WithHttpEndpoint(env: "ASPNETCORE_URLS");
}

var clientApi = clientApiBuilder
    .WithReference(wasmServer)
    .WaitFor(wasmServer);

var webFrontend = builder
    .AddProject<SekibanWasm_Cs_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithReference(clientApi)
    .WaitFor(clientApi);

var e2eWebPort = Environment.GetEnvironmentVariable("E2E_WEB_PORT");
if (!string.IsNullOrWhiteSpace(e2eWebPort))
{
    webFrontend.WithEnvironment("ASPNETCORE_URLS", $"http://127.0.0.1:{e2eWebPort}");
}

builder.Build().Run();
