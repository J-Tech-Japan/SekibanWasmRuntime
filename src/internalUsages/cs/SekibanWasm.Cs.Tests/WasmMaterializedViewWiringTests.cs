using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MaterializedView;
using Sekiban.Dcb.WasmRuntime.Host.MaterializedView;
using Xunit;

namespace SekibanWasm.Cs.Tests;

public sealed class WasmMaterializedViewWiringTests
{
    [Fact]
    public void ServiceIdConfiguration_NormalizesOnceIntoFixedProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [WasmMaterializedViewExtensions.ServiceIdConfigurationKey] = "Tenant-A"
            })
            .Build();

        var provider = WasmMaterializedViewExtensions.ResolveRequiredServiceIdProvider(configuration);

        Assert.Equal("tenant-a", provider.GetCurrentServiceId());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("default")]
    public void ServiceIdConfiguration_RejectsMissingEmptyAndImplicitDefaultWithBothKeyNames(string? value)
    {
        var values = new Dictionary<string, string?>();
        if (value is not null)
        {
            values[WasmMaterializedViewExtensions.ServiceIdConfigurationKey] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            WasmMaterializedViewExtensions.ResolveRequiredServiceIdProvider(configuration));

        Assert.Contains(WasmMaterializedViewExtensions.ServiceIdConfigurationKey, exception.Message, StringComparison.Ordinal);
        Assert.Contains(WasmMaterializedViewExtensions.ServiceIdEnvironmentKey, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RuntimeRegistration_BindsHostedWorkerToExactNormalizedServiceId()
    {
        const string requestedServiceId = "Tenant-A";
        const string expectedServiceId = "tenant-a";
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DcbMaterializedViewPostgres"] = "Host=unused;Database=unused"
            })
            .Build();
        var registrations = new[]
        {
            new WasmMvApplyHostRegistration("ContractView", 1, ["main"])
        };

        Assert.True(services.AddSekibanWasmMaterializedViewRuntime(
            configuration,
            "unused.wasm",
            registrations,
            requestedServiceId,
            validatedModule: WasmMaterializedViewValidationResult.ForTesting("unused.wasm")));

        var applyHost = new ContractHost();
        var hostFactory = new ContractHostFactory(applyHost);
        var executor = new CapturingExecutor();
        services.Replace(ServiceDescriptor.Singleton<IMvApplyHostFactory>(hostFactory));
        services.Replace(ServiceDescriptor.Singleton<IMvExecutor>(executor));

        await using var provider = services.BuildServiceProvider();
        Assert.Equal(
            MvInitializationMode.VerifyOnly,
            provider.GetRequiredService<IOptions<MvOptions>>().Value.InitializationMode);
        var workerDescriptor = Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(IHostedService) &&
            descriptor.ImplementationFactory is not null);
        var worker = Assert.IsType<MvCatchUpWorker>(workerDescriptor.ImplementationFactory!(provider));
        await worker.StartAsync(CancellationToken.None);
        try
        {
            await executor.CatchUpObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var initialize = Assert.Single(executor.InitializeCalls);
            var catchUp = Assert.Single(executor.CatchUpCalls);
            Assert.Same(applyHost, initialize.Host);
            Assert.Same(applyHost, catchUp.Host);
            Assert.Equal(expectedServiceId, initialize.ServiceId);
            Assert.Equal(expectedServiceId, catchUp.ServiceId);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    private sealed class ContractHostFactory(ContractHost host) : IMvApplyHostFactory
    {
        public IReadOnlyList<MvApplyHostRegistration> GetRegistrations() =>
            [new MvApplyHostRegistration(host.ViewName, host.ViewVersion)];

        public IMvApplyHost Create(string viewName, int viewVersion)
        {
            Assert.Equal(host.ViewName, viewName);
            Assert.Equal(host.ViewVersion, viewVersion);
            return host;
        }
    }

    private sealed class ContractHost : IMvApplyHost
    {
        public string ViewName => "ContractView";
        public int ViewVersion => 1;
        public IReadOnlyList<string> LogicalTables => ["main"];

        public Task<IReadOnlyList<MvSqlStatementDto>> InitializeAsync(
            IMvTableBindings tables,
            CancellationToken ct) => Task.FromResult<IReadOnlyList<MvSqlStatementDto>>([]);

        public Task<IReadOnlyList<MvSqlStatementDto>> ApplyEventAsync(
            SerializableEvent ev,
            IMvTableBindings tables,
            IMvApplyQueryPort queryPort,
            string sortableUniqueId,
            CancellationToken ct) => Task.FromResult<IReadOnlyList<MvSqlStatementDto>>([]);
    }

    private sealed class CapturingExecutor : IMvExecutor
    {
        private readonly object _gate = new();

        public List<Call> InitializeCalls { get; } = [];
        public List<Call> CatchUpCalls { get; } = [];
        public TaskCompletionSource CatchUpObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task InitializeAsync(
            IMvApplyHost host,
            string? serviceId = null,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                InitializeCalls.Add(new Call(host, serviceId));
            }

            return Task.CompletedTask;
        }

        public Task<MvCatchUpResult> CatchUpOnceAsync(
            IMvApplyHost host,
            string? serviceId = null,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                CatchUpCalls.Add(new Call(host, serviceId));
            }

            CatchUpObserved.TrySetResult();
            return Task.FromResult(new MvCatchUpResult(0, false));
        }

        public Task<int> ApplySerializableEventsAsync(
            IMvApplyHost host,
            IReadOnlyList<SerializableEvent> events,
            string? serviceId = null,
            CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed record Call(IMvApplyHost Host, string? ServiceId);
}
