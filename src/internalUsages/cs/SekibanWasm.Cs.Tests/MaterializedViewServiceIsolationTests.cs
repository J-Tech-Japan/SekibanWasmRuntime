using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.MaterializedView;
using Sekiban.Dcb.MaterializedView.Sqlite;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using SekibanWasm.Cs.Domain;
using SekibanWasm.Cs.Domain.Weather;
using Xunit;
using Xunit.Sdk;
using DcbEvent = Sekiban.Dcb.Events.Event;

namespace SekibanWasm.Cs.Tests;

public sealed class MaterializedViewServiceIsolationTests
{
    private const string ServiceA = "svc-a";
    private const string ServiceB = "svc-b";

    [Fact]
    public async Task ServiceScopedFactory_IsolatesRowsAndRegistryProgressPerService()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            var connectionString = $"Data Source={databasePath}";
            var domainTypes = DomainType.GetDomainTypes();
#pragma warning disable CS0618
            var sharedBackend = new InMemoryEventStore(
                domainTypes.EventTypes,
                new DefaultServiceIdProvider());
#pragma warning restore CS0618
            var sourceFactory = new InMemoryEventStoreFactory(sharedBackend);
            var storeA = sourceFactory.CreateForService(ServiceA);
            var storeB = sourceFactory.CreateForService(ServiceB);
            var registry = new SqliteMvRegistryStore(connectionString);
            var projector = new IsolationProjector();
            var host = new NativeMvApplyHost(projector, domainTypes.EventTypes, MvDbType.Sqlite);
            var serviceAExecutor = CreateScopedExecutor(sourceFactory, registry, connectionString, ServiceA);
            var serviceBExecutor = CreateScopedExecutor(sourceFactory, registry, connectionString, ServiceB);
            var eventA = CreateForecastEvent("forecast-a", "orders-location", DateTime.UtcNow.AddSeconds(-2), domainTypes.EventTypes);
            var eventB = CreateForecastEvent("forecast-b", "billing-location", DateTime.UtcNow.AddSeconds(-1), domainTypes.EventTypes);

            await WriteAsync(storeA, eventA);
            await WriteAsync(storeB, eventB);

            await serviceAExecutor.InitializeAsync(host);
            var serviceAResult = await serviceAExecutor.CatchUpOnceAsync(host);
            var afterA = await ObserveAsync(connectionString, projector.Forecasts.PhysicalName, registry);

            Assert.Equal(1, serviceAResult.AppliedEvents);
            Assert.Equal(1, afterA.ServiceARows);
            Assert.Equal(0, afterA.ServiceBRows);
            AssertRegistryAdvanced(afterA.ServiceARegistry, ServiceA, eventA.SortableUniqueIdValue);
            Assert.Empty(afterA.ServiceBRegistry);

            await serviceBExecutor.InitializeAsync(host);
            var serviceBResult = await serviceBExecutor.CatchUpOnceAsync(host);
            var afterB = await ObserveAsync(connectionString, projector.Forecasts.PhysicalName, registry);

            Assert.Equal(1, serviceBResult.AppliedEvents);
            Assert.Equal(1, afterB.ServiceARows);
            Assert.Equal(1, afterB.ServiceBRows);
            AssertRegistryAdvanced(afterB.ServiceBRegistry, ServiceB, eventB.SortableUniqueIdValue);
            var serviceAEntryBefore = Assert.Single(afterA.ServiceARegistry);
            var serviceAEntryAfter = Assert.Single(afterB.ServiceARegistry);
            Assert.Equal(serviceAEntryBefore.AppliedEventVersion, serviceAEntryAfter.AppliedEventVersion);
            Assert.Equal(serviceAEntryBefore.EffectiveCurrentPosition, serviceAEntryAfter.EffectiveCurrentPosition);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task VerifyOnlyExecutor_RefusesDirectCatchUpAndApplyWithTypedBoundary()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            var connectionString = $"Data Source={databasePath}";
            var domainTypes = DomainType.GetDomainTypes();
            var sourceFactory = new InMemoryEventStoreFactory(domainTypes.EventTypes);
            var registry = new SqliteMvRegistryStore(connectionString);
            var projector = new IsolationProjector();
            var host = new NativeMvApplyHost(projector, domainTypes.EventTypes, MvDbType.Sqlite);
            var executor = CreateScopedExecutor(
                sourceFactory,
                registry,
                connectionString,
                ServiceA,
                MvInitializationMode.VerifyOnly);

            var catchUp = await Assert.ThrowsAsync<MvTransitionNotAllowedException>(() =>
                executor.CatchUpOnceAsync(host));
            var apply = await Assert.ThrowsAsync<MvTransitionNotAllowedException>(() =>
                executor.ApplySerializableEventsAsync(host, []));

            AssertVerifyOnlyRefusal(catchUp, MvTransition.CatchUp);
            AssertVerifyOnlyRefusal(apply, MvTransition.Apply);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task LegacyAmbientExecutor_KillingControlContaminatesFirstServicePass()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            var connectionString = $"Data Source={databasePath}";
            var domainTypes = DomainType.GetDomainTypes();
#pragma warning disable CS0618
            var aggregateStore = new InMemoryEventStore(
                domainTypes.EventTypes,
                new DefaultServiceIdProvider());
#pragma warning restore CS0618
            var registry = new SqliteMvRegistryStore(connectionString);
            var projector = new IsolationProjector();
            var host = new NativeMvApplyHost(projector, domainTypes.EventTypes, MvDbType.Sqlite);
            var options = Options.Create(new MvOptions
            {
                BatchSize = 100,
                SafeWindowMs = 0,
                AllowDefaultServiceId = true
            });
            var firstExecutor = new SqliteMvExecutor(
                aggregateStore,
                new DefaultServiceIdProvider(),
                registry,
                options,
                NullLogger<SqliteMvExecutor>.Instance,
                connectionString);
            var secondExecutor = new SqliteMvExecutor(
                aggregateStore,
                new DefaultServiceIdProvider(),
                registry,
                options,
                NullLogger<SqliteMvExecutor>.Instance,
                connectionString);

            await WriteAsync(
                aggregateStore,
                CreateForecastEvent("forecast-a", "orders-location", DateTime.UtcNow.AddSeconds(-2), domainTypes.EventTypes));
            await WriteAsync(
                aggregateStore,
                CreateForecastEvent("forecast-b", "billing-location", DateTime.UtcNow.AddSeconds(-1), domainTypes.EventTypes));

            await firstExecutor.InitializeAsync(host);
            var firstResult = await firstExecutor.CatchUpOnceAsync(host);
            var observation = await ObserveAmbientAsync(connectionString, projector.Forecasts.PhysicalName, registry);

            Assert.ThrowsAny<XunitException>(() => AssertFirstPassIsIsolated(firstResult, observation));
            Assert.Equal(2, firstResult.AppliedEvents);
            Assert.Equal(1, observation.ServiceARows);
            Assert.Equal(1, observation.ServiceBRows);
            var registryEntry = Assert.Single(observation.Registry);
            Assert.Equal(DefaultServiceIdProvider.DefaultServiceId, registryEntry.ServiceId);
            Assert.Equal(2, registryEntry.AppliedEventVersion);

            await secondExecutor.InitializeAsync(host);
            var secondResult = await secondExecutor.CatchUpOnceAsync(host);
            Assert.Equal(0, secondResult.AppliedEvents);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    private static SqliteMvExecutor CreateScopedExecutor(
        IEventStoreFactory sourceFactory,
        IMvRegistryStore registry,
        string connectionString,
        string serviceId,
        MvInitializationMode initializationMode = MvInitializationMode.CreateOrEnsure) =>
        new(
            sourceFactory,
            registry,
            Options.Create(new MvOptions
            {
                ServiceId = serviceId,
                InitializationMode = initializationMode,
                BatchSize = 100,
                SafeWindowMs = 0
            }),
            NullLogger<SqliteMvExecutor>.Instance,
            connectionString);

    private static void AssertVerifyOnlyRefusal(
        MvTransitionNotAllowedException exception,
        MvTransition transition)
    {
        Assert.Equal(MvInitializationMode.VerifyOnly, exception.Mode);
        Assert.Equal(transition, exception.Transition);
        Assert.Equal(MvTransitionNotAllowedReason.VerifyOnly, exception.Reason);
        Assert.Equal(ServiceA, exception.ServiceId);
        Assert.Equal(IsolationProjector.ViewNameValue, exception.ViewName);
        Assert.Equal(1, exception.ViewVersion);
    }

    private static SerializableEvent CreateForecastEvent(
        string forecastId,
        string location,
        DateTime timestamp,
        Sekiban.Dcb.Domains.IEventTypes eventTypes)
    {
        var eventId = Guid.NewGuid();
        return new DcbEvent(
                new WeatherForecastCreated(
                    forecastId,
                    location,
                    20,
                    "Sunny",
                    new DateTimeOffset(timestamp, TimeSpan.Zero)),
                SortableUniqueId.Generate(timestamp, eventId),
                nameof(WeatherForecastCreated),
                eventId,
                new EventMetadata("service-isolation", "service-isolation", "test"),
                [])
            .ToSerializableEvent(eventTypes);
    }

    private static async Task WriteAsync(IEventStore store, SerializableEvent serializableEvent)
    {
        var result = await store.WriteSerializableEventsAsync([serializableEvent]);
        if (!result.IsSuccess)
        {
            throw result.GetException();
        }
    }

    private static async Task<IsolationObservation> ObserveAsync(
        string connectionString,
        string table,
        IMvRegistryStore registry)
    {
        var (serviceARows, serviceBRows) = await ReadRowCountsAsync(connectionString, table);
        return new IsolationObservation(
            serviceARows,
            serviceBRows,
            await registry.GetEntriesAsync(ServiceA, IsolationProjector.ViewNameValue, 1),
            await registry.GetEntriesAsync(ServiceB, IsolationProjector.ViewNameValue, 1));
    }

    private static async Task<AmbientObservation> ObserveAmbientAsync(
        string connectionString,
        string table,
        IMvRegistryStore registry)
    {
        var (serviceARows, serviceBRows) = await ReadRowCountsAsync(connectionString, table);
        return new AmbientObservation(
            serviceARows,
            serviceBRows,
            await registry.GetEntriesAsync(
                DefaultServiceIdProvider.DefaultServiceId,
                IsolationProjector.ViewNameValue,
                1));
    }

    private static async Task<(long ServiceARows, long ServiceBRows)> ReadRowCountsAsync(
        string connectionString,
        string table)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        return (
            await ReadCountAsync(connection, table, "forecast-a"),
            await ReadCountAsync(connection, table, "forecast-b"));
    }

    private static async Task<long> ReadCountAsync(SqliteConnection connection, string table, string forecastId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table} WHERE forecast_id = $forecastId;";
        command.Parameters.AddWithValue("$forecastId", forecastId);
        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }

    private static void AssertRegistryAdvanced(
        IReadOnlyList<MvRegistryEntry> entries,
        string serviceId,
        string expectedPosition)
    {
        var entry = Assert.Single(entries);
        Assert.Equal(serviceId, entry.ServiceId);
        Assert.Equal(1, entry.AppliedEventVersion);
        Assert.Equal(expectedPosition, entry.EffectiveCurrentPosition);
    }

    private static void AssertFirstPassIsIsolated(MvCatchUpResult result, AmbientObservation observation)
    {
        Assert.Equal(1, result.AppliedEvents);
        Assert.Equal(1, observation.ServiceARows);
        Assert.Equal(0, observation.ServiceBRows);
    }

    private static string CreateDatabasePath() =>
        Path.Combine(Path.GetTempPath(), $"sekiban-mv-isolation-{Guid.NewGuid():N}.db");

    private sealed record IsolationObservation(
        long ServiceARows,
        long ServiceBRows,
        IReadOnlyList<MvRegistryEntry> ServiceARegistry,
        IReadOnlyList<MvRegistryEntry> ServiceBRegistry);

    private sealed record AmbientObservation(
        long ServiceARows,
        long ServiceBRows,
        IReadOnlyList<MvRegistryEntry> Registry);

    private sealed class IsolationProjector : IMaterializedViewProjector
    {
        public const string ViewNameValue = "ServiceIsolation";

        public string ViewName => ViewNameValue;
        public int ViewVersion => 1;
        public MvTable Forecasts { get; private set; } = default!;

        public async Task InitializeAsync(IMvInitContext ctx, CancellationToken cancellationToken = default)
        {
            Forecasts = ctx.RegisterTable("forecasts");
            await ctx.ExecuteAsync(
                $"""
                 CREATE TABLE IF NOT EXISTS {Forecasts.PhysicalName} (
                     forecast_id TEXT NOT NULL PRIMARY KEY,
                     location TEXT NOT NULL,
                     _last_sortable_unique_id TEXT NOT NULL
                 );
                 """,
                cancellationToken: cancellationToken);
        }

        public Task<IReadOnlyList<MvSqlStatement>> ApplyToViewAsync(
            DcbEvent ev,
            IMvApplyContext ctx,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MvSqlStatement>>(
                ev.Payload is WeatherForecastCreated created
                    ?
                    [
                        new MvSqlStatement(
                            $"""
                             INSERT INTO {Forecasts.PhysicalName}
                                 (forecast_id, location, _last_sortable_unique_id)
                             VALUES (@ForecastId, @Location, @SortableUniqueId)
                             ON CONFLICT (forecast_id) DO UPDATE SET
                                 location = excluded.location,
                                 _last_sortable_unique_id = excluded._last_sortable_unique_id
                             WHERE {Forecasts.PhysicalName}._last_sortable_unique_id < excluded._last_sortable_unique_id;
                             """,
                            new
                            {
                                created.ForecastId,
                                created.Location,
                                SortableUniqueId = ctx.CurrentSortableUniqueId
                            })
                    ]
                    : []);
    }
}
