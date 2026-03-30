using Projects;
using SekibanWasm.AppHostShared;

var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder
    .AddPostgres("sekibanRustPostgres")
    .AddDatabase("SekibanRustDb");

var dbGatePort = AppHostInfrastructure.ResolveConfiguredPort(6300, "E2E_DBGATE_PORT", "DBGATE_PORT");
builder.AddDbGateForPostgres(
    name: "dbgate",
    postgresDatabase: postgres,
    label: "Rust Weather Postgres",
    hostPort: dbGatePort);

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

var wasmServerBuilder = builder
    .AddProject<Sekiban_Dcb_WasmRuntime_Host>("wasmserver")
    .WithEnvironment("SEKIBAN_STORAGE_PROVIDER", "postgres")
    .WithEnvironment("WASM_MODULE_PATH", wasmModulePath)
    .WithReference(postgres, "SekibanDcb")
    .WaitFor(postgres)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development");

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
    "SekibanWasm.Rust.ClientApi"));

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
    .AddProject<SekibanWasm_Rust_Web>("webfrontend")
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
