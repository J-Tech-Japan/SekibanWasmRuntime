using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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

public sealed class WasmMaterializedViewSchemaLessIntegrationTests
{
    private const string ServiceId = "swr-g095-schema-less";
    private const string ViewName = "SchemaLessGuest";
    private const int ViewVersion = 1;
    private const string LogicalTable = "records";

    [PostgresIntegrationFact]
    public async Task CreateOrEnsure_AdvancesSchemaLessGuestOnFreshPostgres_WithSharedServiceId()
    {
        Assert.NotEqual(DefaultServiceIdProvider.DefaultServiceId, ServiceId);

        var configuredConnectionString = Environment.GetEnvironmentVariable(
            PostgresIntegrationFactAttribute.ConnectionStringEnvironmentVariable)!;
        var schema = $"swr_g095_{Guid.NewGuid():N}";
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

        try
        {
            await using var adminConnection = new NpgsqlConnection(adminConnectionString);
            await adminConnection.OpenAsync();
            await ExecuteNonQueryAsync(adminConnection, $"CREATE SCHEMA {schema};");
            schemaCreated = true;

            var domainTypes = DomainType.GetDomainTypes();
            var eventStoreFactory = new InMemoryEventStoreFactory(domainTypes.EventTypes);
            var host = new SchemaLessApplyHost();
            var registry = new PostgresMvRegistryStore(executorConnectionString);
            var options = Options.Create(CreateOptions());
            var executor = new PostgresMvExecutor(
                eventStoreFactory,
                registry,
                options,
                NullLogger<PostgresMvExecutor>.Instance,
                executorConnectionString);

            var appliedEvent = CreateWeatherEvent(domainTypes.EventTypes, DateTime.UtcNow.AddMinutes(-1));
            await WriteAsync(eventStoreFactory.CreateForService(ServiceId), appliedEvent);

            await executor.InitializeAsync(host, ServiceId);
            var progress = await executor.CatchUpOnceAsync(host, ServiceId);

            Assert.Equal(1, progress.AppliedEvents);
            Assert.Equal(appliedEvent.SortableUniqueIdValue, progress.LastAppliedSortableUniqueId);
            var registryEntry = Assert.Single(await registry.GetEntriesAsync(ServiceId, ViewName, ViewVersion));
            Assert.Equal(1, registryEntry.AppliedEventVersion);
            Assert.Equal(appliedEvent.SortableUniqueIdValue, registryEntry.EffectiveCurrentPosition);

            var physicalTable = new MvTableBindings(ViewName, ViewVersion, options.Value).RegisterTable(LogicalTable).PhysicalName;
            Assert.Equal(1, await ReadRowCountAsync(executorConnectionString, physicalTable));
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

    private static MvOptions CreateOptions() => new()
    {
        ServiceId = ServiceId,
        InitializationMode = MvInitializationMode.CreateOrEnsure,
        SqlStatementPolicyMode = MvSqlStatementPolicyMode.Enforced,
        SqlStatementPolicy = new WasmMvSqlStatementPolicy(),
        BatchSize = 100,
        SafeWindowMs = 0
    };

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
                    "schema-less",
                    20,
                    "Sunny",
                    new DateTimeOffset(timestamp, TimeSpan.Zero)),
                SortableUniqueId.Generate(timestamp, eventId),
                nameof(WeatherForecastCreated),
                eventId,
                new EventMetadata("swr-g095", "schema-less", "test"),
                [])
            .ToSerializableEvent(eventTypes);
    }

    private static async Task ExecuteNonQueryAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> ReadRowCountAsync(string connectionString, string physicalTable)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {physicalTable};";
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private sealed class SchemaLessApplyHost : IMvApplyHost
    {
        public string ViewName => WasmMaterializedViewSchemaLessIntegrationTests.ViewName;
        public int ViewVersion => WasmMaterializedViewSchemaLessIntegrationTests.ViewVersion;
        public IReadOnlyList<string> LogicalTables => [LogicalTable];

        public Task<IReadOnlyList<MvSqlStatementDto>> InitializeAsync(
            IMvTableBindings tables,
            CancellationToken ct)
        {
            var table = tables.RegisterTable(LogicalTable);
            return Task.FromResult<IReadOnlyList<MvSqlStatementDto>>(
            [
                new MvSqlStatementDto(
                    $"""
                     CREATE TABLE {table.PhysicalName} (
                         record_id TEXT NOT NULL PRIMARY KEY,
                         value TEXT NOT NULL,
                         _last_sortable_unique_id TEXT NOT NULL
                     );
                     """,
                    [])
            ]);
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
