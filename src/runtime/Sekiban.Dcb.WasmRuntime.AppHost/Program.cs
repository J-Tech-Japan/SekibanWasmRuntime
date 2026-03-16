using Projects;
using SekibanWasm.AppHostShared;

var builder = DistributedApplication.CreateBuilder(args);

var postgresPort = AppHostInfrastructure.ResolveConfiguredPort(
    5432,
    "SEKIBAN_RUNTIME_POSTGRES_PORT",
    "POSTGRES_PORT");

var postgresServer = builder
    .AddPostgres("sekibanRuntimePostgres")
    .WithHostPort(postgresPort);

var postgres = postgresServer.AddDatabase("SekibanRuntimeDb");

var dbGatePort = AppHostInfrastructure.ResolveConfiguredPort(
    3001,
    "SEKIBAN_RUNTIME_DBGATE_PORT",
    "DBGATE_PORT");

builder.AddDbGateForPostgres(
    name: "dbgate",
    postgresDatabase: postgres,
    label: "Sekiban Runtime Postgres",
    hostPort: dbGatePort);

var wasmModulePathRaw = Environment.GetEnvironmentVariable("WASM_MODULE_PATH");
var wasmModulePath = string.IsNullOrWhiteSpace(wasmModulePathRaw)
    ? Path.GetFullPath(Path.Combine(
        builder.AppHostDirectory,
        "..",
        "..",
        "internalUsages",
        "cs",
        "modules",
        "csharp-weather.wasm"))
    : (Path.IsPathRooted(wasmModulePathRaw)
        ? wasmModulePathRaw
        : Path.GetFullPath(Path.Combine(builder.AppHostDirectory, wasmModulePathRaw)));

if (!File.Exists(wasmModulePath))
{
    throw new InvalidOperationException(
        $"WASM module not found at '{wasmModulePath}'. " +
        "Set WASM_MODULE_PATH to the path of the .wasm module you want to run.");
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
    3000,
    "SEKIBAN_RUNTIME_HTTP_PORT",
    "RUNTIME_HOST_PORT");

runtimeBuilder
    .WithEndpoint("http", endpoint =>
    {
        endpoint.Port = runtimePort;
        endpoint.TargetPort = runtimePort;
        endpoint.UriScheme = "http";
        endpoint.IsProxied = false;
    })
    .WithEnvironment("ASPNETCORE_URLS", "http://127.0.0.1:" + runtimePort)
    .WithExternalHttpEndpoints();

builder.Build().Run();
