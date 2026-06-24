using Microsoft.Extensions.Configuration;
using Sekiban.Dcb.ColdEvents;
using Sekiban.Dcb.CosmosDb;
using Sekiban.Dcb.Postgres;
using Sekiban.Dcb.Sqlite;

namespace Sekiban.Dcb.WasmRuntime.Host;

internal enum RuntimeHostStorageProvider
{
    Postgres,
    Sqlite,
    Cosmos
}

internal sealed record RuntimeHostStorageConfiguration(
    RuntimeHostStorageProvider Provider,
    string? ConnectionString = null,
    string? SqlitePath = null,
    string? CosmosDatabaseName = null)
{
    // Both relational providers (Postgres and Sqlite) need the runtime host to run
    // EF migrations at startup so the DCB schema (e.g. dcb_events) exists before the
    // first commit. Postgres was previously excluded, which let the public-container
    // sample reach /health against an empty Aspire-created database and then fail the
    // first commit with `42P01: relation "dcb_events" does not exist`: the upstream
    // Postgres DatabaseInitializerService only calls EnsureCreatedAsync, which no-ops
    // when the database already exists (Aspire pre-creates it empty). MigrateAsync,
    // which MigrateSekibanDcbDatabaseAsync uses, creates the schema reliably on an
    // existing empty database. Cosmos is not relational and self-provisions.
    public bool RequiresRelationalMigration =>
        Provider is RuntimeHostStorageProvider.Postgres or RuntimeHostStorageProvider.Sqlite;
}

internal static class RuntimeHostStorageConfigurationResolver
{
    public static RuntimeHostStorageConfiguration Resolve(
        IConfiguration configuration,
        string contentRootPath)
    {
        var provider = ResolveProvider(configuration);

        return provider switch
        {
            RuntimeHostStorageProvider.Postgres => new RuntimeHostStorageConfiguration(
                Provider: provider,
                ConnectionString: ResolvePostgresConnectionString(configuration)),
            RuntimeHostStorageProvider.Sqlite => new RuntimeHostStorageConfiguration(
                Provider: provider,
                SqlitePath: ResolveSqlitePath(configuration, contentRootPath)),
            RuntimeHostStorageProvider.Cosmos => new RuntimeHostStorageConfiguration(
                Provider: provider,
                ConnectionString: ResolveCosmosConnectionString(configuration),
                CosmosDatabaseName: ResolveCosmosDatabaseName(configuration)),
            _ => throw new InvalidOperationException($"Unsupported storage provider '{provider}'.")
        };
    }

    public static void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration,
        RuntimeHostStorageConfiguration storageConfiguration,
        string contentRootPath)
    {
        switch (storageConfiguration.Provider)
        {
            case RuntimeHostStorageProvider.Postgres:
                services.AddSekibanDcbPostgresWithAspire("SekibanDcb");
                break;
            case RuntimeHostStorageProvider.Sqlite:
                EnsureParentDirectoryExists(storageConfiguration.SqlitePath!);
                services.AddSekibanDcbSqlite(storageConfiguration.SqlitePath!);
                break;
            case RuntimeHostStorageProvider.Cosmos:
                services.AddSekibanDcbCosmosDb(
                    storageConfiguration.ConnectionString!,
                    storageConfiguration.CosmosDatabaseName!);
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported storage provider '{storageConfiguration.Provider}'.");
        }

        ConfigureColdStorage(services, configuration, contentRootPath);
    }

    private static void ConfigureColdStorage(
        IServiceCollection services,
        IConfiguration configuration,
        string contentRootPath)
    {
        if (IsColdStorageEnabled(configuration))
        {
            services.AddSekibanDcbColdExport(configuration, contentRootPath);
            services.AddSekibanDcbColdEventHybridRead();
            return;
        }

        services.AddSekibanDcbColdEventDefaults();
    }

    internal static RuntimeHostStorageProvider ResolveProvider(IConfiguration configuration)
    {
        var raw = FirstNonEmpty(
            configuration["SEKIBAN_STORAGE_PROVIDER"],
            configuration["Sekiban:Database"],
            configuration["SEKIBAN_DATABASE"],
            configuration["DATABASE_TYPE"]);

        return (raw ?? "postgres").Trim().ToLowerInvariant() switch
        {
            "postgres" or "postgresql" => RuntimeHostStorageProvider.Postgres,
            "sqlite" => RuntimeHostStorageProvider.Sqlite,
            "cosmos" or "cosmosdb" => RuntimeHostStorageProvider.Cosmos,
            var unknown => throw new InvalidOperationException(
                $"Unsupported storage provider '{unknown}'. Expected postgres, sqlite, or cosmos.")
        };
    }

    private static bool IsColdStorageEnabled(IConfiguration configuration)
        => configuration.GetSection("Sekiban:ColdEvent").GetValue<bool>("Enabled");

    private static string ResolvePostgresConnectionString(IConfiguration configuration)
    {
        return FirstNonEmpty(
                   configuration.GetConnectionString("SekibanDcb"),
                   configuration["ConnectionStrings:SekibanDcb"],
                   configuration["ConnectionStrings__SekibanDcb"],
                   configuration["SEKIBAN_DCB_CONNECTION"])
               ?? "Host=127.0.0.1;Port=5432;Database=sekiban;Username=postgres;Password=postgres";
    }

    private static string ResolveSqlitePath(IConfiguration configuration, string contentRootPath)
    {
        var configuredPath = FirstNonEmpty(
            configuration.GetConnectionString("SekibanDcbSqlite"),
            configuration.GetConnectionString("Sqlite"),
            configuration["ConnectionStrings:SekibanDcbSqlite"],
            configuration["ConnectionStrings:Sqlite"],
            configuration["ConnectionStrings__SekibanDcbSqlite"],
            configuration["ConnectionStrings__Sqlite"],
            configuration["SEKIBAN_SQLITE_PATH"],
            configuration["Sekiban:Sqlite:Path"],
            configuration["Sekiban:SqlitePath"]);

        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return NormalizeSqlitePath(configuredPath!, contentRootPath);
        }

        var cachePath = FirstNonEmpty(
            configuration["Sekiban:SqliteCachePath"],
            configuration["SEKIBAN_SQLITE_CACHE_PATH"]);

        if (!string.IsNullOrWhiteSpace(cachePath))
        {
            return NormalizeSqlitePath(Path.Combine(cachePath!, "events.db"), contentRootPath);
        }

        return Path.Combine(contentRootPath, "sekiban-runtime.sqlite");
    }

    private static string ResolveCosmosConnectionString(IConfiguration configuration)
    {
        return FirstNonEmpty(
                   configuration.GetConnectionString("SekibanDcbCosmos"),
                   configuration.GetConnectionString("SekibanDcbCosmosDb"),
                   configuration.GetConnectionString("CosmosDb"),
                   configuration.GetConnectionString("cosmosdb"),
                   configuration["ConnectionStrings:SekibanDcbCosmos"],
                   configuration["ConnectionStrings:SekibanDcbCosmosDb"],
                   configuration["ConnectionStrings:CosmosDb"],
                   configuration["ConnectionStrings:cosmosdb"],
                   configuration["ConnectionStrings__SekibanDcbCosmos"],
                   configuration["ConnectionStrings__SekibanDcbCosmosDb"],
                   configuration["ConnectionStrings__CosmosDb"],
                   configuration["ConnectionStrings__cosmosdb"],
                   configuration["SEKIBAN_DCB_COSMOS_CONNECTION"])
               ?? throw new InvalidOperationException(
                   "Cosmos storage requires a connection string in " +
                   "'ConnectionStrings:SekibanDcbCosmos', 'ConnectionStrings:SekibanDcbCosmosDb', " +
                   "'ConnectionStrings:CosmosDb', 'ConnectionStrings:cosmosdb', " +
                   "'ConnectionStrings__SekibanDcbCosmos', 'ConnectionStrings__SekibanDcbCosmosDb', " +
                   "'ConnectionStrings__CosmosDb', 'ConnectionStrings__cosmosdb', " +
                   "or 'SEKIBAN_DCB_COSMOS_CONNECTION'.");
    }

    private static string ResolveCosmosDatabaseName(IConfiguration configuration)
        => FirstNonEmpty(
               configuration["CosmosDb:DatabaseName"],
               configuration["CosmosDb__DatabaseName"],
               configuration["SEKIBAN_COSMOS_DATABASE"])
           ?? "SekibanDcb";

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));

    private static void EnsureParentDirectoryExists(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static string NormalizeSqlitePath(string path, string contentRootPath)
    {
        var effectivePath = Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(Path.Combine(contentRootPath, path));

        return HasDatabaseFileExtension(effectivePath)
            ? effectivePath
            : Path.Combine(effectivePath, "events.db");
    }

    private static bool HasDatabaseFileExtension(string path) =>
        path.EndsWith(".db", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".sqlite3", StringComparison.OrdinalIgnoreCase);
}
