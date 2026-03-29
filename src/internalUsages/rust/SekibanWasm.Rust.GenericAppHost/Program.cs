using Projects;
using SekibanWasm.AppHostShared;

var builder = DistributedApplication.CreateBuilder(args);

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

var storageProvider = AppHostInfrastructure.ResolveStorageProvider();

var runtimeBuilder = builder
    .AddProject<Sekiban_Dcb_WasmRuntime_Host>("runtime")
    .WithEnvironment("SEKIBAN_STORAGE_PROVIDER", storageProvider.ToString().ToLowerInvariant())
    .WithEnvironment("WASM_MODULE_PATH", wasmModulePath);

switch (storageProvider)
{
    case AppHostStorageProvider.Postgres:
    {
        var externalConnectionString = AppHostInfrastructure.GetFirstConfiguredEnvironmentValue(
            "ConnectionStrings__SekibanDcb",
            "SEKIBAN_DCB_CONNECTION");

        if (string.IsNullOrWhiteSpace(externalConnectionString))
        {
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

            runtimeBuilder = runtimeBuilder
                .WithReference(postgres, "SekibanDcb")
                .WaitFor(postgres);
        }
        else
        {
            runtimeBuilder = runtimeBuilder.WithEnvironment("ConnectionStrings__SekibanDcb", externalConnectionString);
        }

        break;
    }
    case AppHostStorageProvider.Sqlite:
    {
        var sqlitePathRaw = AppHostInfrastructure.GetFirstConfiguredEnvironmentValue(
            "SEKIBAN_SQLITE_PATH",
            "ConnectionStrings__SekibanDcbSqlite",
            "ConnectionStrings__Sqlite");
        var sqlitePath = string.IsNullOrWhiteSpace(sqlitePathRaw)
            ? Path.GetFullPath(Path.Combine(builder.AppHostDirectory, ".data", "sekiban-runtime.sqlite"))
            : (Path.IsPathRooted(sqlitePathRaw)
                ? sqlitePathRaw
                : Path.GetFullPath(Path.Combine(builder.AppHostDirectory, sqlitePathRaw)));

        var sqliteDirectory = Path.GetDirectoryName(sqlitePath);
        if (string.IsNullOrWhiteSpace(sqliteDirectory))
        {
            sqliteDirectory = builder.AppHostDirectory;
        }

        Directory.CreateDirectory(sqliteDirectory);

        runtimeBuilder = runtimeBuilder
            .WithEnvironment("SEKIBAN_SQLITE_PATH", sqlitePath)
            .WithEnvironment("ConnectionStrings__SekibanDcbSqlite", sqlitePath);
        break;
    }
    case AppHostStorageProvider.Cosmos:
    {
        var cosmosConnectionString = AppHostInfrastructure.GetFirstConfiguredEnvironmentValue(
            "ConnectionStrings__SekibanDcbCosmos",
            "ConnectionStrings__SekibanDcbCosmosDb",
            "ConnectionStrings__CosmosDb",
            "ConnectionStrings__cosmosdb",
            "SEKIBAN_DCB_COSMOS_CONNECTION");

        if (string.IsNullOrWhiteSpace(cosmosConnectionString))
        {
            throw new InvalidOperationException(
                "Cosmos storage requires a configured connection string. Set one of: " +
                "ConnectionStrings__SekibanDcbCosmos, ConnectionStrings__SekibanDcbCosmosDb, " +
                "ConnectionStrings__CosmosDb, ConnectionStrings__cosmosdb, or SEKIBAN_DCB_COSMOS_CONNECTION.");
        }

        var cosmosDatabaseName = AppHostInfrastructure.GetFirstConfiguredEnvironmentValue(
            "CosmosDb__DatabaseName",
            "SEKIBAN_COSMOS_DATABASE") ?? "SekibanDcb";

        runtimeBuilder = runtimeBuilder
            .WithEnvironment("ConnectionStrings__SekibanDcbCosmos", cosmosConnectionString)
            .WithEnvironment("CosmosDb__DatabaseName", cosmosDatabaseName);
        break;
    }
}

foreach (var envVarName in new[]
         {
             "Sekiban__ColdEvent__Enabled",
             "Sekiban__ColdEvent__Storage__Type",
             "Sekiban__ColdEvent__Storage__Provider",
             "Sekiban__ColdEvent__Storage__Format",
             "Sekiban__ColdEvent__Storage__BasePath",
             "Sekiban__ColdEvent__Storage__JsonlDirectory",
             "Sekiban__ColdEvent__Storage__SqliteFile",
             "Sekiban__ColdEvent__Storage__DuckDbFile",
             "Sekiban__ColdEvent__Storage__AzureBlobClientName",
             "Sekiban__ColdEvent__Storage__AzureContainerName",
             "Sekiban__ColdEvent__Storage__AzurePrefix",
             "ColdExport__Interval",
             "ColdExport__CycleBudget",
             "ConnectionStrings__MultiProjectionOffload"
         })
{
    var configuredValue = Environment.GetEnvironmentVariable(envVarName);
    if (!string.IsNullOrWhiteSpace(configuredValue))
    {
        runtimeBuilder = runtimeBuilder.WithEnvironment(envVarName, configuredValue);
    }
}

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
