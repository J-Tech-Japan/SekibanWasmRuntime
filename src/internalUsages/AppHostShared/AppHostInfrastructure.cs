using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using System.Data.Common;

namespace SekibanWasm.AppHostShared;

internal enum AppHostStorageProvider
{
    Postgres,
    Sqlite,
    Cosmos
}

internal static class AppHostInfrastructure
{
    public static IResourceBuilder<ContainerResource> AddDbGateForPostgres(
        this IDistributedApplicationBuilder builder,
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

    public static int ResolveConfiguredPort(int defaultPort, params string[] envVarNames)
    {
        foreach (var envVarName in envVarNames)
        {
            var configuredPort = Environment.GetEnvironmentVariable(envVarName);
            if (string.IsNullOrWhiteSpace(configuredPort))
            {
                continue;
            }

            if (int.TryParse(configuredPort, out var port) && port is >= 1 and <= 65535)
            {
                return port;
            }

            throw new InvalidOperationException(
                $"Environment variable '{envVarName}' must be a valid TCP port between 1 and 65535. Actual value: '{configuredPort}'.");
        }

        return defaultPort;
    }

    public static AppHostStorageProvider ResolveStorageProvider()
    {
        var configured = GetFirstConfiguredEnvironmentValue(
            "SEKIBAN_STORAGE_PROVIDER",
            "DATABASE_TYPE",
            "Sekiban__Database");

        return (configured ?? "postgres").Trim().ToLowerInvariant() switch
        {
            "postgres" or "postgresql" => AppHostStorageProvider.Postgres,
            "sqlite" => AppHostStorageProvider.Sqlite,
            "cosmos" or "cosmosdb" => AppHostStorageProvider.Cosmos,
            var unknown => throw new InvalidOperationException(
                $"Unsupported storage provider '{unknown}'. Expected postgres, sqlite, or cosmos.")
        };
    }

    public static string? GetFirstConfiguredEnvironmentValue(params string[] envVarNames)
    {
        foreach (var envVarName in envVarNames)
        {
            var value = Environment.GetEnvironmentVariable(envVarName);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static DbGatePostgresSettings ParseDbGatePostgresSettings(
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

    private static string GetRequiredConnectionValue(DbConnectionStringBuilder builder, params string[] keys)
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

    private static string? GetOptionalConnectionValue(DbConnectionStringBuilder builder, params string[] keys)
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

    private static int? GetOptionalPort(DbConnectionStringBuilder builder, params string[] keys)
    {
        var rawValue = GetOptionalConnectionValue(builder, keys);
        if (rawValue is null)
        {
            return null;
        }

        return int.TryParse(rawValue, out var port) ? port : null;
    }

    private static bool IsLoopbackHost(string? host)
    {
        return string.IsNullOrWhiteSpace(host) ||
               string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(host, "[::1]", StringComparison.OrdinalIgnoreCase);
    }

    private readonly record struct DbGatePostgresSettings(
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
}
