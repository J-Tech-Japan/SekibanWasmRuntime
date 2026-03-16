using Projects;
using SekibanWasm.AppHostShared;

var builder = DistributedApplication.CreateBuilder(args);

var postgresPort = AppHostInfrastructure.ResolveConfiguredPort(
    5433,
    "CS_GENERIC_POSTGRES_PORT",
    "POSTGRES_PORT");

var postgresServer = builder
    .AddPostgres("sekibanCsGenericPostgres")
    .WithHostPort(postgresPort);

var postgres = postgresServer.AddDatabase("SekibanCsGenericDb");

var dbGatePort = AppHostInfrastructure.ResolveConfiguredPort(
    5301,
    "CS_GENERIC_DBGATE_PORT",
    "DBGATE_PORT");

builder.AddDbGateForPostgres(
    name: "dbgate",
    postgresDatabase: postgres,
    label: "C# Generic Weather Postgres",
    hostPort: dbGatePort);

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

var runtimeBuilder = builder
    .AddProject<Sekiban_Dcb_WasmRuntime_Host>("runtime")
    .WithReference(postgres)
    .WaitFor(postgres)
    .WithEnvironment("WASM_MODULE_PATH", wasmModulePath);

var manifestPathRaw = Environment.GetEnvironmentVariable("SEKIBAN_MANIFEST_PATH");
if (!string.IsNullOrWhiteSpace(manifestPathRaw))
{
    var manifestPath = Path.IsPathRooted(manifestPathRaw)
        ? manifestPathRaw
        : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), manifestPathRaw));

    if (!File.Exists(manifestPath))
    {
        throw new InvalidOperationException(
            $"Manifest file not found at '{manifestPath}'. " +
            "Set SEKIBAN_MANIFEST_PATH to an existing manifest file.");
    }

    runtimeBuilder = runtimeBuilder.WithEnvironment("SEKIBAN_MANIFEST_PATH", manifestPath);
}

var runtimePort = AppHostInfrastructure.ResolveConfiguredPort(
    5299,
    "CS_GENERIC_RUNTIME_PORT",
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

var clientApiPort = AppHostInfrastructure.ResolveConfiguredPort(
    5298,
    "CS_GENERIC_CLIENT_API_PORT");

var clientApiBuilder = builder
    .AddProject<SekibanWasm_Cs_ClientApi>("clientapi")
    .WithEnvironment("WASM_SERVER_URL", "http://127.0.0.1:" + runtimePort);

clientApiBuilder = clientApiBuilder
    .WithEndpoint("http", endpoint =>
    {
        endpoint.Port = clientApiPort;
        endpoint.TargetPort = clientApiPort;
        endpoint.UriScheme = "http";
        endpoint.IsProxied = false;
    })
    .WithEnvironment("ASPNETCORE_URLS", "http://127.0.0.1:" + clientApiPort);

var clientApi = clientApiBuilder
    .WithReference(runtime)
    .WaitFor(runtime);

var webPort = AppHostInfrastructure.ResolveConfiguredPort(
    5280,
    "CS_GENERIC_WEB_PORT");

var webFrontend = builder
    .AddProject<SekibanWasm_Cs_Web>("webfrontend")
    .WithReference(clientApi)
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
