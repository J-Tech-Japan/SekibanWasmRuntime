using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.MaterializedView;
using Sekiban.Dcb.MaterializedView.Postgres;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.WasmRuntime.Host.MaterializedView;
using SekibanWasm.Cs.Domain;
using SekibanWasm.Cs.Domain.Weather;
using Xunit;
using DcbEvent = Sekiban.Dcb.Events.Event;

namespace SekibanWasm.Cs.Tests;

public sealed class WasmMaterializedViewVerifiedExecutionIntegrationTests
{
    private const string ServiceId = "swr-g083-service";
    private const string ViewName = "VerifiedExecution";
    private const int ViewVersion = 1;
    private const string LogicalTable = "records";

    [PostgresIntegrationFact]
    public async Task VerifyAndExecute_ProvesDdlDeniedBeforeCheckpointProgressAndKeepsPolicyRejectionAtomic()
    {
        var configuredConnectionString = Environment.GetEnvironmentVariable(
            PostgresIntegrationFactAttribute.ConnectionStringEnvironmentVariable)!;
        var schema = $"swr_g083_{Guid.NewGuid():N}";
        var role = $"swr_g083_exec_{Guid.NewGuid():N}";
        var password = Guid.NewGuid().ToString("N");
        var adminConnectionString = new NpgsqlConnectionStringBuilder(configuredConnectionString)
        {
            Pooling = false
        }.ConnectionString;
        var ownerConnectionString = new NpgsqlConnectionStringBuilder(adminConnectionString)
        {
            Pooling = false,
            SearchPath = schema
        }.ConnectionString;
        var roleCreated = false;
        var schemaCreated = false;

        try
        {
            await using var adminConnection = new NpgsqlConnection(adminConnectionString);
            await adminConnection.OpenAsync();
            await ExecuteNonQueryAsync(adminConnection, $"CREATE SCHEMA {schema};");
            schemaCreated = true;
            await ExecuteNonQueryAsync(
                adminConnection,
                $"CREATE ROLE {role} LOGIN PASSWORD '{password}' NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT;");
            roleCreated = true;

            var domainTypes = DomainType.GetDomainTypes();
            var eventStoreFactory = new InMemoryEventStoreFactory(domainTypes.EventTypes);
            var host = new CheckpointApplyHost();
            var ownerRegistry = new PostgresMvRegistryStore(ownerConnectionString);
            var ownerOptions = Options.Create(CreateProvisioningOptions());
            var ownerExecutor = new PostgresMvExecutor(
                eventStoreFactory,
                ownerRegistry,
                ownerOptions,
                NullLogger<PostgresMvExecutor>.Instance,
                ownerConnectionString);

            // Provisioning belongs to the owner. The restricted runtime role receives only DML permissions.
            await ownerExecutor.InitializeAsync(host);
            var physicalTable = GetPhysicalTableName(ownerOptions.Value);
            await ExecuteNonQueryAsync(
                adminConnection,
                $"""
                 GRANT USAGE ON SCHEMA {schema} TO {role};
                 GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA {schema} TO {role};
                 GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA {schema} TO {role};
                 REVOKE CREATE ON SCHEMA {schema} FROM {role};
                 """);

            var restrictedConnectionString = new NpgsqlConnectionStringBuilder(ownerConnectionString)
            {
                Username = role,
                Password = password,
                Pooling = false
            }.ConnectionString;

            // This negative control deliberately runs first, with the exact connection string that executes the
            // materialized-view lifecycle below. A missing or misspelled table would yield a different SQLSTATE.
            await AssertDdlDeniedAsync(restrictedConnectionString, physicalTable);

            var appliedEvent = CreateWeatherEvent(domainTypes.EventTypes, DateTime.UtcNow.AddMinutes(-2));
            await WriteAsync(eventStoreFactory.CreateForService(ServiceId), appliedEvent);
            var verifiedExecutor = new PostgresMvExecutor(
                eventStoreFactory,
                new PostgresMvRegistryStore(restrictedConnectionString),
                Options.Create(CreateOptions(new WasmMvSqlStatementPolicy())),
                NullLogger<PostgresMvExecutor>.Instance,
                restrictedConnectionString);

            await verifiedExecutor.InitializeAsync(host);
            var registryBeforeProgress = Assert.Single(await ownerRegistry.GetEntriesAsync(ServiceId, ViewName, ViewVersion));
            var progress = await verifiedExecutor.CatchUpOnceAsync(host);

            Assert.Equal(1, progress.AppliedEvents);
            Assert.Equal(appliedEvent.SortableUniqueIdValue, progress.LastAppliedSortableUniqueId);
            var registryAfterProgress = Assert.Single(await ownerRegistry.GetEntriesAsync(ServiceId, ViewName, ViewVersion));
            Assert.Equal(registryBeforeProgress.AppliedEventVersion + 1, registryAfterProgress.AppliedEventVersion);
            Assert.Equal(appliedEvent.SortableUniqueIdValue, registryAfterProgress.EffectiveCurrentPosition);
            Assert.Equal(1, await ReadRowCountAsync(ownerConnectionString, physicalTable));

            // The next empty catch-up transitions the proven checkpoint into the serving lifecycle state so the
            // rejection assertion below covers row, registry/status, and active-pointer durability together.
            var settled = await verifiedExecutor.CatchUpOnceAsync(host);
            Assert.Equal(0, settled.AppliedEvents);
            var registryBeforeReject = (await ownerRegistry.GetEntriesAsync(ServiceId, ViewName, ViewVersion)).ToArray();
            var activeBeforeReject = await ownerRegistry.GetActiveAsync(ServiceId, ViewName);
            var rowsBeforeReject = await SnapshotRowsAsync(ownerConnectionString, physicalTable);
            Assert.Equal(MvStatus.Active, Assert.Single(registryBeforeReject).Status);
            Assert.NotNull(activeBeforeReject);

            var rejectedHost = new DdlAppendingApplyHost(
                host,
                $"CREATE TABLE swr_g083_policy_probe_{Guid.NewGuid():N} (id INTEGER);");
            var rejectingExecutor = new PostgresMvExecutor(
                eventStoreFactory,
                new PostgresMvRegistryStore(restrictedConnectionString),
                Options.Create(CreateOptions(new RejectDdlPolicy())),
                NullLogger<PostgresMvExecutor>.Instance,
                restrictedConnectionString);
            await rejectingExecutor.InitializeAsync(rejectedHost);

            var rejection = await Assert.ThrowsAsync<MvSqlPolicyRejectedException>(() =>
                rejectingExecutor.ApplySerializableEventsAsync(
                    rejectedHost,
                    [CreateWeatherEvent(domainTypes.EventTypes, DateTime.UtcNow.AddMinutes(1))]));

            Assert.Equal(MvSqlPolicyFailureReason.Denied, rejection.Failure.FailureReason);
            Assert.Equal(rowsBeforeReject, await SnapshotRowsAsync(ownerConnectionString, physicalTable));
            var registryAfterReject = (await ownerRegistry.GetEntriesAsync(ServiceId, ViewName, ViewVersion)).ToArray();
            Assert.Equal(registryBeforeReject, registryAfterReject);
            Assert.Equal(
                Assert.Single(registryBeforeReject).Status,
                Assert.Single(registryAfterReject).Status);
            Assert.Equal(activeBeforeReject, await ownerRegistry.GetActiveAsync(ServiceId, ViewName));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var cleanupConnection = new NpgsqlConnection(adminConnectionString);
            await cleanupConnection.OpenAsync();
            if (roleCreated)
            {
                await ExecuteNonQueryAsync(cleanupConnection, $"DROP OWNED BY {role};");
                await ExecuteNonQueryAsync(cleanupConnection, $"DROP ROLE IF EXISTS {role};");
            }

            if (schemaCreated)
            {
                await ExecuteNonQueryAsync(cleanupConnection, $"DROP SCHEMA IF EXISTS {schema} CASCADE;");
            }
        }
    }

    private static MvOptions CreateOptions(IMvSqlStatementPolicy policy) => new()
    {
        ServiceId = ServiceId,
        InitializationMode = MvInitializationMode.VerifyAndExecute,
        SqlStatementPolicyMode = MvSqlStatementPolicyMode.Enforced,
        SqlStatementPolicy = policy,
        BatchSize = 100,
        SafeWindowMs = 0
    };

    private static MvOptions CreateProvisioningOptions() => new()
    {
        ServiceId = ServiceId,
        InitializationMode = MvInitializationMode.CreateOrEnsure,
        SqlStatementPolicyMode = MvSqlStatementPolicyMode.Enforced,
        SqlStatementPolicy = new WasmMvSqlStatementPolicy(),
        BatchSize = 100,
        SafeWindowMs = 0
    };

    private static string GetPhysicalTableName(MvOptions options) =>
        new MvTableBindings(ViewName, ViewVersion, options).RegisterTable(LogicalTable).PhysicalName;

    private static async Task AssertDdlDeniedAsync(string connectionString, string physicalTable)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        var create = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteNonQueryAsync(connection, $"CREATE TABLE swr_g083_create_probe_{Guid.NewGuid():N} (id INTEGER);"));
        var alter = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteNonQueryAsync(connection, $"ALTER TABLE {physicalTable} ADD COLUMN swr_g083_alter_probe INTEGER;"));
        var drop = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteNonQueryAsync(connection, $"DROP TABLE {physicalTable};"));

        Assert.Equal("42501", create.SqlState);
        Assert.Equal("42501", alter.SqlState);
        Assert.Equal("42501", drop.SqlState);
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
                    "verified-execution",
                    20,
                    "Sunny",
                    new DateTimeOffset(timestamp, TimeSpan.Zero)),
                SortableUniqueId.Generate(timestamp, eventId),
                nameof(WeatherForecastCreated),
                eventId,
                new EventMetadata("swr-g083", "verified-execution", "test"),
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

    private static async Task<string[]> SnapshotRowsAsync(string connectionString, string physicalTable)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
                               SELECT row_to_json(snapshot)::text
                               FROM (
                                   SELECT *
                                   FROM {physicalTable}
                                   ORDER BY record_id
                               ) AS snapshot;
                               """;
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync())
        {
            rows.Add(reader.GetString(0));
        }

        return [.. rows];
    }

    private sealed class CheckpointApplyHost : IMvApplyHost
    {
        public string ViewName => WasmMaterializedViewVerifiedExecutionIntegrationTests.ViewName;
        public int ViewVersion => WasmMaterializedViewVerifiedExecutionIntegrationTests.ViewVersion;
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

        public IReadOnlyList<MvSchemaTableRequirement> GetSchemaRequirements(IMvTableBindings tables)
        {
            var table = tables.RegisterTable(LogicalTable);
            return
            [
                new MvSchemaTableRequirement(
                    LogicalTable,
                    table.PhysicalName,
                    [
                        new MvSchemaColumnRequirement("record_id", MvSchemaTypeFamily.String, false),
                        new MvSchemaColumnRequirement("value", MvSchemaTypeFamily.String, false),
                        new MvSchemaColumnRequirement("_last_sortable_unique_id", MvSchemaTypeFamily.String, false)
                    ],
                    ["record_id"])
            ];
        }
    }

    private sealed class DdlAppendingApplyHost(CheckpointApplyHost inner, string ddl) : IMvApplyHost
    {
        public string ViewName => inner.ViewName;
        public int ViewVersion => inner.ViewVersion;
        public IReadOnlyList<string> LogicalTables => inner.LogicalTables;

        public Task<IReadOnlyList<MvSqlStatementDto>> InitializeAsync(
            IMvTableBindings tables,
            CancellationToken ct) => inner.InitializeAsync(tables, ct);

        public async Task<IReadOnlyList<MvSqlStatementDto>> ApplyEventAsync(
            SerializableEvent ev,
            IMvTableBindings tables,
            IMvApplyQueryPort queryPort,
            string sortableUniqueId,
            CancellationToken ct)
        {
            var statements = await inner.ApplyEventAsync(ev, tables, queryPort, sortableUniqueId, ct);
            return [.. statements, new MvSqlStatementDto(ddl, [])];
        }

        public IReadOnlyList<MvSchemaTableRequirement> GetSchemaRequirements(IMvTableBindings tables) =>
            inner.GetSchemaRequirements(tables);
    }

    private sealed class RejectDdlPolicy : IMvSqlStatementPolicy
    {
        private readonly WasmMvSqlStatementPolicy _inner = new();

        public ValueTask<MvSqlPolicyDecision> EvaluateAsync(
            MvSqlStatementContext context,
            CancellationToken cancellationToken = default) =>
            context.Origin == MvSqlStatementOrigin.ProjectorApply &&
            context.Sql.StartsWith("CREATE", StringComparison.OrdinalIgnoreCase)
                ? ValueTask.FromResult(MvSqlPolicyDecision.Reject("DDL is denied for the atomicity control.", "swr-g083-deny-ddl"))
                : _inner.EvaluateAsync(context, cancellationToken);
    }
}
