using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.MaterializedView;
using Sekiban.Dcb.MaterializedView.Postgres;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.WasmRuntime.Host.MaterializedView;
using SekibanWasm.Cs.Domain;
using SekibanWasm.Cs.Domain.Weather;
using Xunit;
using DcbEvent = Sekiban.Dcb.Events.Event;

namespace SekibanWasm.Cs.Tests;

public sealed class WasmMaterializedViewCreateOrEnsureInitializationRaceIntegrationTests
{
    private const string ServiceId = "swr-g098-create-or-ensure";
    private const string ViewName = "CreateOrEnsureRaceGuest";
    private const int ViewVersion = 1;
    private const string LogicalTable = "records";

    [PostgresIntegrationFact]
    public async Task CreateOrEnsure_RegisteredExecutorSerializesOverlappingInitializeAsync_OnFreshPostgres()
    {
        Assert.NotEqual(DefaultServiceIdProvider.DefaultServiceId, ServiceId);

        var configuredConnectionString = Environment.GetEnvironmentVariable(
            PostgresIntegrationFactAttribute.ConnectionStringEnvironmentVariable)!;
        var schema = $"swr_g098_{Guid.NewGuid():N}";
        var adminConnectionString = new NpgsqlConnectionStringBuilder(configuredConnectionString)
        {
            Pooling = false
        }.ConnectionString;
        var executorConnectionString = new NpgsqlConnectionStringBuilder(adminConnectionString)
        {
            Pooling = false,
            SearchPath = schema
        }.ConnectionString;
        var schemaCreated = false;
        var applyHost = new RaceTrackingApplyHost();

        try
        {
            await using var adminConnection = new NpgsqlConnection(adminConnectionString);
            await adminConnection.OpenAsync();
            await ExecuteNonQueryAsync(adminConnection, $"CREATE SCHEMA {schema};");
            schemaCreated = true;

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IEventStoreFactory>(
                new InMemoryEventStoreFactory(DomainType.GetDomainTypes().EventTypes));

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DcbMaterializedViewPostgres"] = executorConnectionString,
                    [WasmMaterializedViewExtensions.InitializationModeConfigurationKey] = nameof(MvInitializationMode.CreateOrEnsure)
                })
                .Build();

            Assert.True(services.AddSekibanWasmMaterializedViewRuntime(
                configuration,
                "unused.wasm",
                [new WasmMvApplyHostRegistration(ViewName, ViewVersion, [LogicalTable])],
                ServiceId,
                validatedModule: WasmMaterializedViewValidationResult.ForTesting("unused.wasm")));

            Assert.Contains(
                services,
                descriptor =>
                    descriptor.ServiceType == typeof(IHostedService) &&
                    descriptor.ImplementationFactory is not null);
            Assert.Contains(
                services,
                descriptor =>
                    descriptor.ServiceType == typeof(IHostedService) &&
                    descriptor.ImplementationType?.Name == "MaterializedViewGrainActivator");

            await using var provider = services.BuildServiceProvider();
            var executor = provider.GetRequiredService<IMvExecutor>();
            Assert.NotEqual(typeof(PostgresMvExecutor), executor.GetType());

            await Task.WhenAll(
                executor.InitializeAsync(applyHost, ServiceId),
                executor.InitializeAsync(applyHost, ServiceId));

            Assert.Equal(1, applyHost.MaxInFlight);
            var registry = provider.GetRequiredService<IMvRegistryStore>();
            Assert.Single(await registry.GetEntriesAsync(ServiceId, ViewName, ViewVersion));

            var domainTypes = DomainType.GetDomainTypes();
            var appliedEvent = CreateWeatherEvent(domainTypes.EventTypes, DateTime.UtcNow.AddMinutes(-1));
            await WriteAsync(
                provider.GetRequiredService<IEventStoreFactory>().CreateForService(ServiceId),
                appliedEvent);

            var catchUp = await executor.CatchUpOnceAsync(applyHost, ServiceId);
            Assert.Equal(1, catchUp.AppliedEvents);

            var orleansCatchUpInterface = executor
                .GetType()
                .GetInterfaces()
                .Single(static iface => iface.Name == "IMvOrleansCatchUpExecutor");
            var orleansCatchUpMethod = orleansCatchUpInterface.GetMethod("CatchUpOnceForOrleansAsync")
                ?? throw new InvalidOperationException("CatchUpOnceForOrleansAsync was not found.");
            var orleansCatchUpTask = (Task<MvCatchUpResult>)orleansCatchUpMethod.Invoke(
                executor,
                [applyHost, ServiceId, CancellationToken.None])!;
            var orleansCatchUp = await orleansCatchUpTask;
            Assert.True(orleansCatchUp.AppliedEvents >= 0);

            var registryAfterCatchUp = Assert.Single(await registry.GetEntriesAsync(ServiceId, ViewName, ViewVersion));
            Assert.Equal(1, registryAfterCatchUp.AppliedEventVersion);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            if (schemaCreated)
            {
                await using var cleanupConnection = new NpgsqlConnection(adminConnectionString);
                await cleanupConnection.OpenAsync();
                await ExecuteNonQueryAsync(cleanupConnection, $"DROP SCHEMA IF EXISTS {schema} CASCADE;");
            }
        }
    }

    private static async Task WriteAsync(IEventStore store, SerializableEvent serializableEvent)
    {
        var result = await store.WriteSerializableEventsAsync([serializableEvent]);
        if (!result.IsSuccess)
        {
            throw result.GetException();
        }
    }

    private static SerializableEvent CreateWeatherEvent(
        Sekiban.Dcb.Domains.IEventTypes eventTypes,
        DateTime timestamp)
    {
        var eventId = Guid.NewGuid();
        return new DcbEvent(
                new WeatherForecastCreated(
                    $"forecast-{eventId:N}",
                    "create-or-ensure-race",
                    20,
                    "Sunny",
                    new DateTimeOffset(timestamp, TimeSpan.Zero)),
                SortableUniqueId.Generate(timestamp, eventId),
                nameof(WeatherForecastCreated),
                eventId,
                new EventMetadata("swr-g098", "create-or-ensure-race", "test"),
                [])
            .ToSerializableEvent(eventTypes);
    }

    private static async Task ExecuteNonQueryAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private sealed class RaceTrackingApplyHost : IMvApplyHost
    {
        private int _inFlight;
        private int _maxInFlight;

        public int MaxInFlight => _maxInFlight;

        public string ViewName => WasmMaterializedViewCreateOrEnsureInitializationRaceIntegrationTests.ViewName;
        public int ViewVersion => WasmMaterializedViewCreateOrEnsureInitializationRaceIntegrationTests.ViewVersion;
        public IReadOnlyList<string> LogicalTables => [LogicalTable];

        public async Task<IReadOnlyList<MvSqlStatementDto>> InitializeAsync(
            IMvTableBindings tables,
            CancellationToken ct)
        {
            var current = Interlocked.Increment(ref _inFlight);
            var observed = Volatile.Read(ref _maxInFlight);
            while (current > observed &&
                   Interlocked.CompareExchange(ref _maxInFlight, current, observed) != observed)
            {
                observed = Volatile.Read(ref _maxInFlight);
            }

            try
            {
                await Task.Delay(250, ct).ConfigureAwait(false);
                var table = tables.RegisterTable(LogicalTable);
                return
                [
                    new MvSqlStatementDto(
                        $"""
                         CREATE TABLE IF NOT EXISTS {table.PhysicalName} (
                             record_id TEXT NOT NULL PRIMARY KEY,
                             value TEXT NOT NULL,
                             _last_sortable_unique_id TEXT NOT NULL
                         );
                         """,
                        [])
                ];
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }

        public Task<IReadOnlyList<MvSqlStatementDto>> ApplyEventAsync(
            SerializableEvent ev,
            IMvTableBindings tables,
            IMvApplyQueryPort queryPort,
            string sortableUniqueId,
            CancellationToken ct)
        {
            var table = tables.RegisterTable(LogicalTable);
            return Task.FromResult<IReadOnlyList<MvSqlStatementDto>>(
            [
                new MvSqlStatementDto(
                    $"""
                     INSERT INTO {table.PhysicalName} (record_id, value, _last_sortable_unique_id)
                     VALUES (@RecordId, @Value, @SortableUniqueId)
                     ON CONFLICT (record_id) DO NOTHING;
                     """,
                    [
                        new MvParam("RecordId", MvParamKind.String, JsonSerializer.Serialize($"record-{sortableUniqueId}")),
                        new MvParam("Value", MvParamKind.String, JsonSerializer.Serialize("applied")),
                        new MvParam("SortableUniqueId", MvParamKind.String, JsonSerializer.Serialize(sortableUniqueId))
                    ])
            ]);
        }
    }
}
