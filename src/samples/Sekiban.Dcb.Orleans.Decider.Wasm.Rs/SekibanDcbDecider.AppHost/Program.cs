using Projects;
using SekibanWasm.AppHostShared;

var builder = DistributedApplication.CreateBuilder(args);

var storage = builder
    .AddAzureStorage("azurestorage")
    .RunAsEmulator();
var clusteringTable = storage.AddTables("SekibanRustClusteringTable");
var grainStorage = storage.AddBlobs("SekibanRustGrainState");
var queue = storage.AddQueues("SekibanRustQueue");
var apiClusteringTable = storage.AddTables("DcbOrleansClusteringTable");
var apiGrainTable = storage.AddTables("DcbOrleansGrainTable");
var apiGrainStorage = storage.AddBlobs("DcbOrleansGrainState");
var apiQueue = storage.AddQueues("DcbOrleansQueue");
var multiProjectionOffload = storage.AddBlobs("MultiProjectionOffload");

var postgresServer = builder
    .AddPostgres("sekibanRustPostgres");
var postgres = postgresServer.AddDatabase("SekibanRustDb");
var dcbPostgres = postgresServer.AddDatabase("DcbPostgres");
var identityPostgres = postgresServer.AddDatabase("IdentityPostgres");

var orleans = builder
    .AddOrleans("default")
    .WithClustering(clusteringTable)
    .WithGrainStorage("Default", grainStorage)
    .WithStreaming(queue);
var apiOrleans = builder
    .AddOrleans("api-orleans")
    .WithClustering(apiClusteringTable)
    .WithGrainStorage("Default", apiGrainStorage)
    .WithGrainStorage("OrleansStorage", apiGrainStorage)
    .WithGrainStorage("dcb-orleans-queue", apiGrainStorage)
    .WithGrainStorage("DcbOrleansGrainTable", apiGrainTable)
    .WithStreaming(apiQueue);

var dbGatePort = AppHostInfrastructure.ResolveConfiguredPort(6300, "E2E_DBGATE_PORT", "DBGATE_PORT");
builder.AddDbGateForPostgres(
    name: "dbgate",
    postgresDatabase: postgres,
    label: "Dcb Decider Rust Postgres",
    hostPort: dbGatePort);

var rustWasmModulePath = ResolveRustWasmModulePath();
var apiServiceWasmModulePath = ResolveApiServiceWasmModulePath();
var apiServicePort = AppHostInfrastructure.ResolveConfiguredPort(6197, "E2E_API_SERVICE_PORT", "API_SERVICE_PORT");

var wasmServerBuilder = builder
    .AddProject<SekibanDcbDecider_WasmServer>("wasmserver")
    .WithReference(postgres)
    .WithReference(orleans)
    .WaitFor(postgres)
    .WithEnvironment("Wasm__DefaultModulePath", rustWasmModulePath);

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

var apiServiceBuilder = builder
    .AddProject<SekibanDcbDecider_ApiService>("apiservice")
    .WithReference(dcbPostgres)
    .WithReference(identityPostgres)
    .WithReference(apiOrleans)
    .WithReference(multiProjectionOffload)
    .WithEnvironment("WASM_MODULE_PATH", apiServiceWasmModulePath)
    .WaitFor(dcbPostgres)
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

static string ResolveRustWasmModulePath()
{
    string? envPath = Environment.GetEnvironmentVariable("RUST_WASM_MODULE_PATH")
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
        "Rust WASM module not found. Set RUST_WASM_MODULE_PATH or build SekibanDcbDecider.Rust.Wasm first.");
}

static string ResolveApiServiceWasmModulePath()
{
    string? envPath = Environment.GetEnvironmentVariable("API_WASM_MODULE_PATH");
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
        "ApiService WASM module not found. Set API_WASM_MODULE_PATH or build SekibanDcbDecider.Wasm first.");
}
