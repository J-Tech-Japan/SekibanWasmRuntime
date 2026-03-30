using Projects;
using SekibanWasm.AppHostShared;

var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder
    .AddPostgres("sekibanCsPostgres")
    .AddDatabase("SekibanCsDb");

var dbGatePort = AppHostInfrastructure.ResolveConfiguredPort(5300, "E2E_DBGATE_PORT", "DBGATE_PORT");
builder.AddDbGateForPostgres(
    name: "dbgate",
    postgresDatabase: postgres,
    label: "C# Weather Postgres",
    hostPort: dbGatePort);

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
    .AddProject<Sekiban_Dcb_WasmRuntime_Host>("wasmserver")
    .WithEnvironment("SEKIBAN_STORAGE_PROVIDER", "postgres")
    .WithEnvironment("WASM_MODULE_PATH", wasmModulePath)
    .WithReference(postgres, "SekibanDcb")
    .WaitFor(postgres)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development");

var wasmApiPort = AppHostInfrastructure.ResolveConfiguredPort(5199, "E2E_API_PORT");
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

var clientApiBuilder = builder
    .AddProject<SekibanWasm_Cs_ClientApi>("clientapi")
    .WithEnvironment("WASM_SERVER_URL", "http://127.0.0.1:" + wasmApiPort);

var clientApiPort = AppHostInfrastructure.ResolveConfiguredPort(5198, "E2E_CLIENT_API_PORT");
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
    .WithReference(wasmServer)
    .WaitFor(wasmServer);

var webFrontend = builder
    .AddProject<SekibanWasm_Cs_Web>("webfrontend")
    .WithReference(clientApi)
    .WithEnvironment("CLIENT_API_URL", "http://127.0.0.1:" + clientApiPort)
    .WaitFor(clientApi);

var webPort = AppHostInfrastructure.ResolveConfiguredPort(5180, "E2E_WEB_PORT");
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
