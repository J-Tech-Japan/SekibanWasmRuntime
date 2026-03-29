using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Dcb.ColdEvents;
using Sekiban.Dcb.CosmosDb;
using Sekiban.Dcb.Sqlite;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.WasmRuntime.Host;
using SekibanWasm.Cs.Domain;
using Xunit;

namespace SekibanWasm.Cs.Tests;

public class RuntimeHostStorageConfigurationTests
{
    [Fact]
    public void Resolve_ShouldDefaultToPostgres()
    {
        var configuration = new ConfigurationBuilder().Build();

        var resolved = RuntimeHostStorageConfigurationResolver.Resolve(configuration, Directory.GetCurrentDirectory());

        Assert.Equal(RuntimeHostStorageProvider.Postgres, resolved.Provider);
        Assert.True(resolved.RequiresRelationalMigration);
        Assert.Contains("Host=127.0.0.1", resolved.ConnectionString);
    }

    [Fact]
    public void Resolve_ShouldUseSqlitePathFromConfiguration()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), "runtime-host-storage-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contentRoot);

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["SEKIBAN_STORAGE_PROVIDER"] = "sqlite",
                    ["SEKIBAN_SQLITE_PATH"] = "data/runtime.sqlite"
                })
                .Build();

            var resolved = RuntimeHostStorageConfigurationResolver.Resolve(configuration, contentRoot);

            Assert.Equal(RuntimeHostStorageProvider.Sqlite, resolved.Provider);
            Assert.False(resolved.RequiresRelationalMigration);
            Assert.Equal(
                Path.Combine(contentRoot, "data", "runtime.sqlite"),
                resolved.SqlitePath);
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    [Fact]
    public void Resolve_ShouldUseCosmosConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SEKIBAN_STORAGE_PROVIDER"] = "cosmos",
                ["ConnectionStrings:SekibanDcbCosmos"] =
                    "AccountEndpoint=https://localhost:8081/;AccountKey=C2F3AkfakeKey==;",
                ["CosmosDb:DatabaseName"] = "SekibanRuntimeTests"
            })
            .Build();

        var resolved = RuntimeHostStorageConfigurationResolver.Resolve(configuration, Directory.GetCurrentDirectory());

        Assert.Equal(RuntimeHostStorageProvider.Cosmos, resolved.Provider);
        Assert.Equal("SekibanRuntimeTests", resolved.CosmosDatabaseName);
        Assert.Contains("AccountEndpoint=", resolved.ConnectionString);
    }

    [Fact]
    public void ConfigureServices_ShouldRegisterSqliteStore_WhenSqliteIsSelected()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), "runtime-host-sqlite-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contentRoot);

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["SEKIBAN_STORAGE_PROVIDER"] = "sqlite",
                    ["SEKIBAN_SQLITE_PATH"] = Path.Combine(contentRoot, "runtime.sqlite")
                })
                .Build();

            var services = CreateBaseServices(configuration);
            var resolved = RuntimeHostStorageConfigurationResolver.Resolve(configuration, contentRoot);

            RuntimeHostStorageConfigurationResolver.ConfigureServices(
                services,
                configuration,
                resolved,
                contentRoot);

            using var provider = services.BuildServiceProvider();

            Assert.IsType<SqliteEventStore>(provider.GetRequiredService<IEventStore>());
            Assert.NotNull(provider.GetRequiredService<IColdEventStoreFeature>());
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ConfigureServices_ShouldWrapHotStoreWithHybridEventStore_WhenColdStorageEnabled()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), "runtime-host-cold-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contentRoot);

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["SEKIBAN_STORAGE_PROVIDER"] = "sqlite",
                    ["SEKIBAN_SQLITE_PATH"] = Path.Combine(contentRoot, "runtime.sqlite"),
                    ["Sekiban:ColdEvent:Enabled"] = "true",
                    ["Sekiban:ColdEvent:Storage:Type"] = "sqlite",
                    ["Sekiban:ColdEvent:Storage:BasePath"] = Path.Combine(contentRoot, "cold")
                })
                .Build();

            var services = CreateBaseServices(configuration);
            var resolved = RuntimeHostStorageConfigurationResolver.Resolve(configuration, contentRoot);

            RuntimeHostStorageConfigurationResolver.ConfigureServices(
                services,
                configuration,
                resolved,
                contentRoot);

            using var provider = services.BuildServiceProvider();

            Assert.IsType<HybridEventStore>(provider.GetRequiredService<IEventStore>());
            var status = await provider.GetRequiredService<IColdEventStoreFeature>().GetStatusAsync(CancellationToken.None);
            Assert.True(status.IsEnabled);
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    [Fact]
    public void ConfigureServices_ShouldRegisterCosmosStore_WhenCosmosIsSelected()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SEKIBAN_STORAGE_PROVIDER"] = "cosmos",
                ["ConnectionStrings:SekibanDcbCosmos"] =
                    "AccountEndpoint=https://localhost:8081/;AccountKey=C2F3AkfakeKey==;",
                ["CosmosDb:DatabaseName"] = "SekibanRuntimeTests"
            })
            .Build();

        var services = CreateBaseServices(configuration);
        var resolved = RuntimeHostStorageConfigurationResolver.Resolve(configuration, Directory.GetCurrentDirectory());

        RuntimeHostStorageConfigurationResolver.ConfigureServices(
            services,
            configuration,
            resolved,
            Directory.GetCurrentDirectory());

        using var provider = services.BuildServiceProvider();

        Assert.IsType<CosmosDbEventStore>(provider.GetRequiredService<IEventStore>());
    }

    private static ServiceCollection CreateBaseServices(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(DomainType.GetDomainTypes());
        return services;
    }
}
