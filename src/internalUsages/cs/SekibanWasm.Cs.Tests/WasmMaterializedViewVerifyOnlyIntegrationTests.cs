using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.MaterializedView;
using Sekiban.Dcb.MaterializedView.Sqlite;
using SekibanWasm.Cs.Domain;
using Sekiban.Dcb.WasmRuntime.Host.MaterializedView;
using Xunit;

namespace SekibanWasm.Cs.Tests;

public sealed class WasmMaterializedViewVerifyOnlyIntegrationTests
{
    [Fact]
    public async Task CSharpVerifyOnly_SucceedsForRegisteredPreProvisionedTableWithoutDdl()
    {
        const string serviceId = "tenant-a";
        const string viewName = "WeatherForecast";
        const int viewVersion = 1;
        const string logicalTable = "weather_forecast";
        var databasePath = Path.Combine(Path.GetTempPath(), $"swr-g079-verify-only-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={databasePath}";

        try
        {
            var options = new MvOptions
            {
                ServiceId = serviceId,
                InitializationMode = MvInitializationMode.VerifyOnly
            };
            var bindings = new MvTableBindings(viewName, viewVersion, options);
            var physicalTable = bindings.RegisterTable(logicalTable).PhysicalName;
            var metadata = CreateCSharpMetadata();
            var wasmExecutor = new RecordingWasmExecutor();
            var host = new WasmMvApplyHost(
                viewName,
                viewVersion,
                [logicalTable],
                wasmExecutor,
                serviceId,
                metadata);
            var registry = new SqliteMvRegistryStore(connectionString);

            // Provisioning is deliberately completed before the verify-only executor starts.
            // The executor must only inspect this schema and the existing registry binding.
            await registry.EnsureInfrastructureAsync();
            await using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"""
                    CREATE TABLE {physicalTable} (
                        forecast_id TEXT NOT NULL PRIMARY KEY,
                        created_at TEXT NULL
                    );
                    """;
                await command.ExecuteNonQueryAsync();
            }

            await registry.RegisterAsync(new MvRegistryEntry
            {
                ServiceId = serviceId,
                ViewName = viewName,
                ViewVersion = viewVersion,
                LogicalTable = logicalTable,
                PhysicalTable = physicalTable,
                Status = MvStatus.Ready,
                LastUpdated = DateTimeOffset.UtcNow
            });

            var schemaVersionBefore = await ReadSchemaVersionAsync(databasePath);
            var catalogCommands = new List<string>();
            var readOnlyConnections = new List<string>();
            var verifyRegistry = new SqliteMvRegistryStore(
                connectionString,
                catalogCommands.Add,
                readOnlyConnections.Add);
            var eventStoreFactory = new InMemoryEventStoreFactory(DomainType.GetDomainTypes().EventTypes);
            var executor = new SqliteMvExecutor(
                eventStoreFactory,
                verifyRegistry,
                Options.Create(options),
                NullLogger<SqliteMvExecutor>.Instance,
                connectionString);

            await executor.InitializeAsync(host);

            Assert.Equal(0, wasmExecutor.InitializeCalls);
            Assert.Equal(schemaVersionBefore, await ReadSchemaVersionAsync(databasePath));
            Assert.Contains("sqlite:Mode=ReadOnly", readOnlyConnections);
            Assert.NotEmpty(catalogCommands);
            Assert.DoesNotContain(
                catalogCommands,
                sql => Regex.IsMatch(
                    sql,
                    @"\b(?:CREATE|ALTER|DROP|INSERT|UPDATE|DELETE)\b",
                    RegexOptions.IgnoreCase));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    private static WasmMvMetadataDto CreateCSharpMetadata() => new()
    {
        ViewName = "WeatherForecast",
        ViewVersion = 1,
        AbiVersion = WasmMvContract.AbiVersion,
        Capabilities = [WasmMvContract.QueryRowsCapability],
        LogicalTables = ["weather_forecast"],
        Schema =
        [
            new WasmMvSchemaTableDto
            {
                LogicalTable = "weather_forecast",
                Columns =
                [
                    new() { Name = "forecast_id", TypeFamily = WasmMvSchemaTypeFamily.String, IsNullable = false },
                    new() { Name = "created_at", TypeFamily = WasmMvSchemaTypeFamily.DateTime, IsNullable = true }
                ],
                PrimaryKeyColumns = ["forecast_id"]
            }
        ]
    };

    private static async Task<long> ReadSchemaVersionAsync(string databasePath)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA schema_version;";
        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }

    private sealed class RecordingWasmExecutor : IWasmMaterializedViewExecutor
    {
        public int InitializeCalls { get; private set; }

        public Task<IReadOnlyList<WasmMvMetadataDto>> GetMetadataAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WasmMvMetadataDto>>([]);

        public Task<IReadOnlyList<WasmMvSqlStatementDto>> InitializeAsync(
            string viewName,
            int viewVersion,
            WasmMvTableBindingsDto tableBindings,
            CancellationToken cancellationToken = default)
        {
            InitializeCalls++;
            return Task.FromResult<IReadOnlyList<WasmMvSqlStatementDto>>([]);
        }

        public Task<IReadOnlyList<WasmMvSqlStatementDto>> ApplyEventAsync(
            string viewName,
            int viewVersion,
            WasmMvTableBindingsDto tableBindings,
            WasmMvSerializableEventDto serializableEvent,
            IMvApplyQueryPort queryPort,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WasmMvSqlStatementDto>>([]);
    }
}
