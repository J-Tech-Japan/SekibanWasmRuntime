using Projects;
using Aspire.Hosting.ApplicationModel;
using System.Data.Common;
using System.Net;
using System.Net.Sockets;

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

var dbGatePort = ResolveLoopbackPort("E2E_DBGATE_PORT", "DBGATE_PORT");
AddDbGate(
    builder,
    name: "dbgate",
    postgresDatabase: postgres,
    label: "Rust Weather Postgres",
    hostPort: dbGatePort);

var orleans = builder
    .AddOrleans("default")
    .WithClustering(clusteringTable)
    .WithGrainStorage("Default", grainStorage)
    .WithStreaming(queue);

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
    .AddProject<SekibanWasm_Rust_WasmServer>("wasmserver")
    .WithReference(postgres)
    .WithReference(orleans)
    .WaitFor(postgres)
    .WithEnvironment("Wasm__DefaultModulePath", wasmModulePath);

var wasmApiPort = ResolveLoopbackPort("E2E_API_PORT");
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

var clientApiPort = ResolveLoopbackPort("E2E_CLIENT_API_PORT");
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

var webPort = ResolveLoopbackPort("E2E_WEB_PORT");
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

IResourceBuilder<ContainerResource> AddDbGate(
    IDistributedApplicationBuilder builder,
    string name,
    IResourceBuilder<PostgresDatabaseResource> postgresDatabase,
    string label,
    int hostPort)
{
    var postgresServerName = postgresDatabase.Resource.Parent.Name;
    var databaseName = postgresDatabase.Resource.DatabaseName;

    return builder
        .AddContainer(name, "dbgate/dbgate", "7.1.2-alpine")
        .WithReference(postgresDatabase)
        .WaitFor(postgresDatabase)
        .WithHttpEndpoint(targetPort: 3000, port: hostPort, name: "http", isProxied: false)
        .WithExternalHttpEndpoints()
        .WithEnvironment("CONNECTIONS", "weather")
        .WithEnvironment("LABEL_weather", label)
        .WithEnvironment("ENGINE_weather", "postgres@dbgate-plugin-postgres")
        .WithEnvironment(async context =>
        {
            var connectionString = await postgresDatabase.Resource.Parent.GetConnectionStringAsync(context.CancellationToken)
                ?? throw new InvalidOperationException(
                    $"Connection string for '{postgresDatabase.Resource.Parent.Name}' was not available.");

            var settings = ParseDbGatePostgresSettings(
                connectionString,
                defaultHost: postgresServerName,
                defaultPort: 5432,
                defaultDatabase: databaseName);

            context.EnvironmentVariables["URL_weather"] = settings.Uri;
            context.EnvironmentVariables["SERVER_weather"] = settings.Host;
            context.EnvironmentVariables["PORT_weather"] = settings.Port.ToString();
            context.EnvironmentVariables["DATABASE_weather"] = settings.Database;
            context.EnvironmentVariables["USER_weather"] = settings.Username;
            context.EnvironmentVariables["PASSWORD_weather"] = settings.Password;
        });
}

DbGatePostgresSettings ParseDbGatePostgresSettings(
    string connectionString,
    string defaultHost,
    int defaultPort,
    string defaultDatabase)
{
    var builder = new DbConnectionStringBuilder
    {
        ConnectionString = connectionString
    };

    var configuredHost = GetOptionalConnectionValue(builder, "Host");
    var configuredPort = GetOptionalPort(builder, "Port");

    var host = IsLoopbackHost(configuredHost) ? defaultHost : configuredHost ?? defaultHost;
    var port = IsLoopbackHost(configuredHost) ? defaultPort : configuredPort ?? defaultPort;
    var database = GetOptionalConnectionValue(builder, "Database") ?? defaultDatabase;
    var username = GetRequiredConnectionValue(builder, "Username", "User ID", "User Id", "UserName");
    var password = GetRequiredConnectionValue(builder, "Password");

    return new DbGatePostgresSettings(host, port, database, username, password);
}

string GetRequiredConnectionValue(DbConnectionStringBuilder builder, params string[] keys)
{
    foreach (var key in keys)
    {
        if (builder.TryGetValue(key, out var rawValue) && rawValue is not null)
        {
            var value = rawValue.ToString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }
    }

    throw new InvalidOperationException(
        $"Connection string did not contain any of the expected keys: {string.Join(", ", keys)}.");
}

string? GetOptionalConnectionValue(DbConnectionStringBuilder builder, params string[] keys)
{
    foreach (var key in keys)
    {
        if (builder.TryGetValue(key, out var rawValue) && rawValue is not null)
        {
            var value = rawValue.ToString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }
    }

    return null;
}

int? GetOptionalPort(DbConnectionStringBuilder builder, params string[] keys)
{
    var rawValue = GetOptionalConnectionValue(builder, keys);
    if (rawValue is null)
    {
        return null;
    }

    return int.TryParse(rawValue, out var port) ? port : null;
}

bool IsLoopbackHost(string? host)
{
    return string.IsNullOrWhiteSpace(host) ||
           string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(host, "[::1]", StringComparison.OrdinalIgnoreCase);
}

int ResolveLoopbackPort(params string[] envVarNames)
{
    foreach (var envVarName in envVarNames)
    {
        var configuredPort = Environment.GetEnvironmentVariable(envVarName);
        if (int.TryParse(configuredPort, out var port))
        {
            return port;
        }
    }

    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    return ((IPEndPoint)listener.LocalEndpoint).Port;
}

readonly record struct DbGatePostgresSettings(
    string Host,
    int Port,
    string Database,
    string Username,
    string Password)
{
    public string Uri =>
        new UriBuilder("postgresql", Host, Port, Database)
        {
            UserName = Username,
            Password = Password
        }.Uri.ToString();
}
