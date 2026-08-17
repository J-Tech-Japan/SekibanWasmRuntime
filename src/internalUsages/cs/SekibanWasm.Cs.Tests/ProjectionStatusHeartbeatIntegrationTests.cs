using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Dcb;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.WasmRuntime.Host;
using SekibanWasm.Cs.Domain;
using Xunit;

namespace SekibanWasm.Cs.Tests;

public sealed class ProjectionStatusHeartbeatIntegrationTests
{
    [PostgresIntegrationFact]
    public async Task HostPostgresStore_ObservesHeartbeatSequenceAdvanceAcrossMultipleCycles()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            PostgresIntegrationFactAttribute.ConnectionStringEnvironmentVariable)!;
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SEKIBAN_STORAGE_PROVIDER"] = "postgres",
                ["ConnectionStrings:SekibanDcb"] = connectionString
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(DomainType.GetDomainTypes());

        var storageConfiguration = RuntimeHostStorageConfigurationResolver.Resolve(
            configuration,
            Directory.GetCurrentDirectory());
        RuntimeHostStorageConfigurationResolver.ConfigureServices(
            services,
            configuration,
            storageConfiguration,
            Directory.GetCurrentDirectory());

        using var serviceProvider = services.BuildServiceProvider();
        var multiProjectionStateStore = serviceProvider.GetRequiredService<IMultiProjectionStateStore>();
        var statusStore = Assert.IsAssignableFrom<IProjectionStatusStore>(multiProjectionStateStore);
        var projectorName = $"swr-g084-{Guid.NewGuid():N}";
        const string projectorVersion = "1";
        const string clusterId = "swr-g084-test-cluster";
        var activationId = Guid.NewGuid().ToString("N");
        var observedSequences = new List<long>();

        for (var sequence = 1L; sequence <= 3; sequence++)
        {
            var write = await statusStore.UpsertAsync(
                new ProjectionStatusHeartbeat(
                    ServiceId: "default",
                    ProjectorName: projectorName,
                    ProjectorVersion: projectorVersion,
                    ClusterId: clusterId,
                    ActivationId: activationId,
                    Sequence: sequence,
                    AppliedEventCount: sequence,
                    LastAppliedSortableUniqueId: $"swr-g084-{sequence}",
                    LastTraversedSortableUniqueId: $"swr-g084-{sequence}",
                    RecordedAtUtc: DateTimeOffset.UtcNow)
                {
                    Phase = ProjectionStatusPhases.Active
                },
                expectedSequence: sequence - 1);

            Assert.True(write.IsSuccess, write.IsSuccess ? string.Empty : write.GetException().ToString());
            Assert.True(write.GetValue().Committed);

            var rows = await statusStore.ListAsync(projectorName, projectorVersion);
            Assert.True(rows.IsSuccess, rows.IsSuccess ? string.Empty : rows.GetException().ToString());
            observedSequences.Add(Assert.Single(rows.GetValue()).Sequence);
        }

        Assert.Equal([1L, 2L, 3L], observedSequences);
        Assert.True(observedSequences[1] > observedSequences[0]);
        Assert.True(observedSequences[2] > observedSequences[1]);
    }
}
