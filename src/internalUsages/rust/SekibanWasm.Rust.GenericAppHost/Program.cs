using Projects;
using SekibanWasm.AppHostShared;

var builder = DistributedApplication.CreateBuilder(args);

var postgresPort = AppHostInfrastructure.ResolveConfiguredPort(
    5434,
    "RUST_GENERIC_POSTGRES_PORT",
    "POSTGRES_PORT");

var postgresServer = builder
    .AddPostgres("sekibanRustGenericPostgres")
    .WithHostPort(postgresPort);

var postgres = postgresServer.AddDatabase("SekibanRustGenericDb");

var dbGatePort = AppHostInfrastructure.ResolveConfiguredPort(
    6301,
    "RUST_GENERIC_DBGATE_PORT",
    "DBGATE_PORT");

builder.AddDbGateForPostgres(
    name: "dbgate",
    postgresDatabase: postgres,
    label: "Rust Generic Weather Postgres",
    hostPort: dbGatePort);

var wasmModulePathRaw = Environment.GetEnvironmentVariable("WASM_MODULE_PATH");
var wasmModulePath = string.IsNullOrWhiteSpace(wasmModulePathRaw)
    ? Path.GetFullPath(Path.Combine(
        builder.AppHostDirectory,
        "..",
        "modules",
        "rust-weather.wasm"))
    : (Path.IsPathRooted(wasmModulePathRaw)
        ? wasmModulePathRaw
        : Path.GetFullPath(Path.Combine(builder.AppHostDirectory, wasmModulePathRaw)));

if (!File.Exists(wasmModulePath))
{
    throw new InvalidOperationException(
        $"WASM module not found at '{wasmModulePath}'. " +
        "Set WASM_MODULE_PATH to the path of the Rust .wasm module, or build it via ./build/scripts/build-rust-wasm.sh.");
}

var runtimeBuilder = builder
    .AddProject<Sekiban_Dcb_WasmRuntime_Host>("runtime")
    .WithReference(postgres, "SekibanDcb")
    .WaitFor(postgres)
    .WithEnvironment("WASM_MODULE_PATH", wasmModulePath);

var manifestPathRaw = Environment.GetEnvironmentVariable("SEKIBAN_MANIFEST_PATH");
if (!string.IsNullOrWhiteSpace(manifestPathRaw))
{
    var manifestPath = Path.IsPathRooted(manifestPathRaw)
        ? manifestPathRaw
        : Path.GetFullPath(Path.Combine(builder.AppHostDirectory, manifestPathRaw));

    if (!File.Exists(manifestPath))
    {
        throw new InvalidOperationException(
            $"Manifest file not found at '{manifestPath}'. " +
            "Set SEKIBAN_MANIFEST_PATH to an existing manifest file.");
    }

    runtimeBuilder = runtimeBuilder.WithEnvironment("SEKIBAN_MANIFEST_PATH", manifestPath);
}

var runtimePort = AppHostInfrastructure.ResolveConfiguredPort(
    6299,
    "RUST_GENERIC_RUNTIME_PORT",
    "RUNTIME_HOST_PORT");

runtimeBuilder = runtimeBuilder
    .WithEndpoint("http", endpoint =>
    {
        endpoint.Port = runtimePort;
        endpoint.TargetPort = runtimePort;
        endpoint.UriScheme = "http";
        endpoint.IsProxied = false;
    })
    .WithEnvironment("ASPNETCORE_URLS", "http://127.0.0.1:" + runtimePort);
runtimeBuilder.WithExternalHttpEndpoints();

var runtime = runtimeBuilder;

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
    .WithEnvironment("WASM_SERVER_URL", "http://127.0.0.1:" + runtimePort);

var clientApiPort = AppHostInfrastructure.ResolveConfiguredPort(
    6298,
    "RUST_GENERIC_CLIENT_API_PORT");

clientApiBuilder = clientApiBuilder.WithHttpEndpoint(
    targetPort: clientApiPort,
    port: clientApiPort,
    env: "PORT",
    isProxied: false);

var clientApi = clientApiBuilder
    .WithEnvironment("RUST_LOG", "info")
    .WithReference(runtime)
    .WaitFor(runtime);

var webPort = AppHostInfrastructure.ResolveConfiguredPort(
    6280,
    "RUST_GENERIC_WEB_PORT");

var webFrontend = builder
    .AddProject<SekibanWasm_Rust_Web>("webfrontend")
    .WithReference(clientApi.GetEndpoint("http"))
    .WithEnvironment("CLIENT_API_URL", "http://127.0.0.1:" + clientApiPort)
    .WaitFor(clientApi);

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
