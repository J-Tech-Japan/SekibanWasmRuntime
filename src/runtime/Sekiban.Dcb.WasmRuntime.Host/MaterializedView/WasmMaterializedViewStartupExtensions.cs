using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sekiban.Dcb.MaterializedView;
using Sekiban.Dcb.MaterializedView.Postgres;
using Sekiban.Dcb.Storage;

namespace Sekiban.Dcb.WasmRuntime.Host.MaterializedView;

/// <summary>
///     Bootstraps the MV Postgres schema before hosted MV workers start in
///     <see cref="MvInitializationMode.VerifyAndExecute"/> mode. Aspire and other
///     local hosts create an empty <c>DcbMaterializedViewPostgres</c> database; verified
///     execution requires infrastructure tables and registered view tables to exist first.
/// </summary>
public static class WasmMaterializedViewStartupExtensions
{
    public static async Task ProvisionWasmMaterializedViewSchemaAsync(
        this WebApplication app,
        string serviceId,
        IReadOnlyList<WasmMvApplyHostRegistration> registrations,
        string connectionStringName = WasmMaterializedViewExtensions.DefaultConnectionStringName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceId);
        ArgumentNullException.ThrowIfNull(registrations);

        if (registrations.Count == 0)
        {
            return;
        }

        var connectionString = app.Configuration.GetConnectionString(connectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var logger = app.Services.GetService<ILogger<PostgresMvExecutor>>()
            ?? NullLogger<PostgresMvExecutor>.Instance;

        var registry = new PostgresMvRegistryStore(connectionString);
        await registry.EnsureInfrastructureAsync(cancellationToken).ConfigureAwait(false);

        var provisioningOptions = Options.Create(new MvOptions
        {
            ServiceId = serviceId,
            InitializationMode = MvInitializationMode.CreateOrEnsure,
            SqlStatementPolicyMode = MvSqlStatementPolicyMode.Enforced,
            SqlStatementPolicy = new WasmMvSqlStatementPolicy(),
            BatchSize = 100,
            SafeWindowMs = 0
        });

        var eventStoreFactory = app.Services.GetRequiredService<IEventStoreFactory>();
        var executor = new PostgresMvExecutor(
            eventStoreFactory,
            registry,
            provisioningOptions,
            logger,
            connectionString);
        var wasmExecutor = app.Services.GetRequiredService<IWasmMaterializedViewExecutor>();

        foreach (var registration in registrations)
        {
            var host = new WasmMvApplyHost(
                registration.ViewName,
                registration.ViewVersion,
                registration.LogicalTables,
                wasmExecutor,
                serviceId,
                registration.Metadata);
            await executor.InitializeAsync(host, serviceId, cancellationToken).ConfigureAwait(false);
        }

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
            "Materialized-view Postgres schema provisioned for service {ServiceId} ({ViewCount} view registration(s)).",
                serviceId,
                registrations.Count);
        }
    }
}
