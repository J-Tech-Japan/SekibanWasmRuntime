using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Dcb;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.CosmosDb;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Orleans;
using Sekiban.Dcb.Orleans.Streams;
using Sekiban.Dcb.Runtime;
using Sekiban.Dcb.Postgres;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Sqlite;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.WasmRuntime;
using Sekiban.Dcb.WasmRuntime.Host;
using SekibanWasm.Cs.Domain;
using Xunit;

namespace SekibanWasm.Cs.Tests;

public sealed class RuntimeHostStorageServiceIdTests
{
    private const string ConfiguredServiceId = "swr-g095-configured";

    [Fact]
    public void ConfigureServices_Postgres_ResolvesConfiguredServiceId()
    {
        var configuredProvider = new FixedServiceIdProvider(ConfiguredServiceId);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SEKIBAN_STORAGE_PROVIDER"] = "postgres",
                ["ConnectionStrings:SekibanDcb"] = "Host=127.0.0.1;Port=5432;Database=unused;Username=postgres;Password=postgres"
            })
            .Build();
        var services = CreateBaseServices(configuration);
        services.AddSingleton<IServiceIdProvider>(configuredProvider);

        var resolved = RuntimeHostStorageConfigurationResolver.Resolve(configuration, Directory.GetCurrentDirectory());
        RuntimeHostStorageConfigurationResolver.ConfigureServices(
            services,
            configuration,
            resolved,
            Directory.GetCurrentDirectory(),
            configuredProvider);

        using var provider = services.BuildServiceProvider();
        Assert.Same(configuredProvider, provider.GetRequiredService<IServiceIdProvider>());
        Assert.Equal(ConfiguredServiceId, provider.GetRequiredService<IServiceIdProvider>().GetCurrentServiceId());
        Assert.NotEqual(DefaultServiceIdProvider.DefaultServiceId, provider.GetRequiredService<IServiceIdProvider>().GetCurrentServiceId());
        _ = provider.GetRequiredService<IEventStore>();
    }

    [Fact]
    public void ConfigureServices_Sqlite_ResolvesConfiguredServiceIdForEventAndProjectionStores()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), "runtime-host-service-id", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contentRoot);
        var configuredProvider = new FixedServiceIdProvider(ConfiguredServiceId);

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
            services.AddSingleton<IServiceIdProvider>(configuredProvider);
            var resolved = RuntimeHostStorageConfigurationResolver.Resolve(configuration, contentRoot);
            RuntimeHostStorageConfigurationResolver.ConfigureServices(
                services,
                configuration,
                resolved,
                contentRoot,
                configuredProvider);

            using var provider = services.BuildServiceProvider();
            Assert.Same(configuredProvider, provider.GetRequiredService<IServiceIdProvider>());
            Assert.IsType<SqliteEventStore>(provider.GetRequiredService<IEventStore>());
            _ = provider.GetRequiredService<IMultiProjectionStateStore>();
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    [Fact]
    public void ConfigureServices_Cosmos_ResolvesConfiguredServiceId()
    {
        var configuredProvider = new FixedServiceIdProvider(ConfiguredServiceId);
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
        services.AddSingleton<IServiceIdProvider>(configuredProvider);
        var resolved = RuntimeHostStorageConfigurationResolver.Resolve(configuration, Directory.GetCurrentDirectory());
        RuntimeHostStorageConfigurationResolver.ConfigureServices(
            services,
            configuration,
            resolved,
            Directory.GetCurrentDirectory(),
            configuredProvider);

        using var provider = services.BuildServiceProvider();
        Assert.Same(configuredProvider, provider.GetRequiredService<IServiceIdProvider>());
        Assert.IsType<CosmosDbEventStore>(provider.GetRequiredService<IEventStore>());
    }

    [Fact]
    public void ConfigureServices_Postgres_KeepsConfiguredServiceId_AfterOrleansExecutorRegistration()
    {
        var configuredProvider = new FixedServiceIdProvider(ConfiguredServiceId);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SEKIBAN_STORAGE_PROVIDER"] = "postgres",
                ["ConnectionStrings:SekibanDcb"] = "Host=127.0.0.1;Port=5432;Database=unused;Username=postgres;Password=postgres"
            })
            .Build();
        var services = CreateBaseServices(configuration);
        services.AddSingleton<IServiceIdProvider>(configuredProvider);
        var resolved = RuntimeHostStorageConfigurationResolver.Resolve(configuration, Directory.GetCurrentDirectory());
        RuntimeHostStorageConfigurationResolver.ConfigureServices(
            services,
            configuration,
            resolved,
            Directory.GetCurrentDirectory(),
            configuredProvider);
        AddHostOrleansExecutorDescriptors(services);

        using var provider = services.BuildServiceProvider();
        Assert.Same(configuredProvider, provider.GetRequiredService<IServiceIdProvider>());
    }

    private static void AddHostOrleansExecutorDescriptors(IServiceCollection services)
    {
        services.AddSekibanDcbSharedRuntime();
        services.AddSingleton<IEventSubscriptionResolver>(_ =>
            new DefaultOrleansEventSubscriptionResolver(
                "EventStreamProvider",
                "AllEvents",
                Guid.Empty));
        services.AddSingleton<IMultiProjectionEventStatistics, NoOpMultiProjectionEventStatistics>();
        services.AddSingleton(new GeneralMultiProjectionActorOptions());
        services.AddSingleton<IActorObjectAccessor, OrleansActorObjectAccessor>();
        services.AddSingleton<IProjectionActorHostFactory, WasmProjectionActorHostFactory>();
        services.AddTransient<OrleansDcbExecutor>();
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
