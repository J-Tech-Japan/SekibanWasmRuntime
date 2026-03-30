using Projects;
using SekibanWasm.AppHostShared;

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

var dbGatePort = AppHostInfrastructure.ResolveConfiguredPort(6300, "E2E_DBGATE_PORT", "DBGATE_PORT");
builder.AddDbGateForPostgres(
    name: "dbgate",
    postgresDatabase: postgres,
    label: "Dcb Decider Rust Postgres",
    hostPort: dbGatePort);

var wasmModulePath = ResolveWasmModulePath();

var wasmServerBuilder = builder
    .AddProject<SekibanDcbDecider_WasmServer>("wasmserver")
    .WithReference(postgres)
    .WithReference(orleans)
    .WaitFor(postgres)
    .WithEnvironment("Wasm__DefaultModulePath", wasmModulePath);

var wasmApiPort = AppHostInfrastructure.ResolveConfiguredPort(6199, "E2E_API_PORT");
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

var rustClientApiDir = Path.GetFullPath(Path.Combine(
    builder.AppHostDirectory,
    "..",
    "SekibanDcbDecider.ClientApi"));

var clientApiBuilder = builder
    .AddExecutable(
        "clientapi",
        "cargo",
        rustClientApiDir,
        new[] { "run", "--release" })
    .WithEnvironment("WASM_SERVER_URL", "http://127.0.0.1:" + wasmApiPort);

var clientApiPort = AppHostInfrastructure.ResolveConfiguredPort(6198, "E2E_CLIENT_API_PORT");
clientApiBuilder = clientApiBuilder.WithHttpEndpoint(
    targetPort: clientApiPort,
    port: clientApiPort,
    env: "PORT",
    isProxied: false);

var clientApi = clientApiBuilder
    .WithEnvironment("RUST_LOG", "info")
    .WithReference(wasmServer)
    .WaitFor(wasmServer);

var webFrontend = builder
    .AddProject<SekibanDcbDecider_Web>("webfrontend")
    .WithReference(clientApi.GetEndpoint("http"))
    .WithEnvironment("CLIENT_API_URL", "http://127.0.0.1:" + clientApiPort)
    .WaitFor(clientApi);

var webPort = AppHostInfrastructure.ResolveConfiguredPort(6180, "E2E_WEB_PORT");
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
            "sekiban-dcb-decider-rust.wasm")),
        Path.GetFullPath(Path.Combine(
            Directory.GetCurrentDirectory(),
            "..",
            "target",
            "wasm32-wasip1",
            "release",
            "sekiban-dcb-decider-rust-wasm.wasm")),
        Path.GetFullPath(Path.Combine(
            Directory.GetCurrentDirectory(),
            "..",
            "target",
            "wasm32-wasip1",
            "release",
            "sekiban_dcb_decider_rust_wasm.wasm")),
        Path.GetFullPath(Path.Combine(
            Directory.GetCurrentDirectory(),
            "..",
            "SekibanDcbDecider.Rust.Wasm",
            "target",
            "wasm32-wasip1",
            "release",
            "sekiban_dcb_decider_rust_wasm.wasm")),
        Path.GetFullPath(Path.Combine(
            Directory.GetCurrentDirectory(),
            "..",
            "modules",
            "rust-weather.wasm"))
    ];

    foreach (string candidate in candidates)
    {
        if (File.Exists(candidate))
        {
            return candidate;
        }
    }

    throw new InvalidOperationException(
        "WASM module not found. Set WASM_MODULE_PATH or build SekibanDcbDecider.Rust.Wasm first.");
}
