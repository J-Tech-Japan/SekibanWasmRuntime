using Projects;
using SekibanWasm.AppHostShared;

var builder = DistributedApplication.CreateBuilder(args);

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
        Directory.CreateDirectory(string.IsNullOrWhiteSpace(sqliteDirectory) ? builder.AppHostDirectory : sqliteDirectory);

        runtimeBuilder = runtimeBuilder
            .WithEnvironment("SEKIBAN_SQLITE_PATH", sqlitePath)
            .WithEnvironment("ConnectionStrings__SekibanDcbSqlite", sqlitePath);
        break;
    }
    case AppHostStorageProvider.Cosmos:
    {
        var cosmosConnectionStringKeys = new[]
        {
            "ConnectionStrings__SekibanDcbCosmos",
            "ConnectionStrings__SekibanDcbCosmosDb",
            "ConnectionStrings__CosmosDb",
            "ConnectionStrings__cosmosdb",
            "SEKIBAN_DCB_COSMOS_CONNECTION"
        };
        var cosmosConnectionString = AppHostInfrastructure.GetFirstConfiguredEnvironmentValue(cosmosConnectionStringKeys);

        if (string.IsNullOrWhiteSpace(cosmosConnectionString))
        {
            throw new InvalidOperationException(
                "Cosmos storage requires one of the following environment variables or connection string keys to be set: "
                + string.Join(", ", cosmosConnectionStringKeys) + ".");
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
