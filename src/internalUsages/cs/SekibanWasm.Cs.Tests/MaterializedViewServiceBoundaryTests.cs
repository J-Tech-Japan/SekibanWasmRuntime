using System.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MaterializedView;
using Sekiban.Dcb.MaterializedView.Postgres;
using Sekiban.Dcb.Storage;
using Xunit;

namespace SekibanWasm.Cs.Tests;

public sealed class MaterializedViewServiceBoundaryTests
{
    [Fact]
    public async Task PostgresExecutor_RejectsInvalidServiceBeforeRegistryOrSourceIo()
    {
        var sourceFactory = new ThrowingEventStoreFactory();
        var registry = new CountingRegistryStore();
        var host = new BoundaryHost();
        var executor = CreateExecutor(sourceFactory, registry, new MvOptions());

        await Assert.ThrowsAsync<InvalidOperationException>(() => executor.InitializeAsync(host));
        await Assert.ThrowsAsync<InvalidOperationException>(() => executor.CatchUpOnceAsync(host, " "));
        await Assert.ThrowsAsync<InvalidOperationException>(() => executor.CatchUpOnceAsync(host, "default"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ApplySerializableEventsAsync(host, [], "default"));

        Assert.Equal(0, registry.EnsureCalls);
        Assert.Equal(0, sourceFactory.CreateCalls);
    }

    [Fact]
    public async Task PostgresExecutor_RejectsOptionsMismatchBeforeRegistryOrSourceIo()
    {
        var sourceFactory = new ThrowingEventStoreFactory();
        var registry = new CountingRegistryStore();
        var executor = CreateExecutor(
            sourceFactory,
            registry,
            new MvOptions { ServiceId = "bound-service" });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.InitializeAsync(new BoundaryHost(), "other-service"));

        Assert.Contains("bound-service", exception.Message, StringComparison.Ordinal);
        Assert.Contains("other-service", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, registry.EnsureCalls);
        Assert.Equal(0, sourceFactory.CreateCalls);
    }

    [Fact]
    public async Task PostgresExecutor_VerifyOnlyRefusesDirectMutatingCallsWithTypedBoundary()
    {
        var sourceFactory = new ThrowingEventStoreFactory();
        var registry = new CountingRegistryStore();
        var host = new BoundaryHost();
        var executor = CreateExecutor(
            sourceFactory,
            registry,
            new MvOptions
            {
                ServiceId = "bound-service",
                InitializationMode = MvInitializationMode.VerifyOnly
            });

        var catchUp = await Assert.ThrowsAsync<MvTransitionNotAllowedException>(() =>
            executor.CatchUpOnceAsync(host));
        var apply = await Assert.ThrowsAsync<MvTransitionNotAllowedException>(() =>
            executor.ApplySerializableEventsAsync(host, []));

        AssertVerifyOnlyRefusal(catchUp, MvTransition.CatchUp);
        AssertVerifyOnlyRefusal(apply, MvTransition.Apply);
        Assert.Equal(0, registry.EnsureCalls);
        Assert.Equal(0, sourceFactory.CreateCalls);
    }

    [Fact]
    public void CatchUpWorker_RejectsMissingMismatchedAndImplicitDefaultServiceIdsInConstructor()
    {
        var factory = new BoundaryHostFactory();
        var executor = new NoOpExecutor();
        var logger = NullLogger<MvCatchUpWorker>.Instance;

        Assert.Throws<InvalidOperationException>(() =>
            new MvCatchUpWorker(factory, executor, Options.Create(new MvOptions()), logger, serviceId: null));
        Assert.Throws<InvalidOperationException>(() =>
            new MvCatchUpWorker(
                factory,
                executor,
                Options.Create(new MvOptions { ServiceId = "bound-service" }),
                logger,
                "other-service"));
        Assert.Throws<InvalidOperationException>(() =>
            new MvCatchUpWorker(
                factory,
                executor,
                Options.Create(new MvOptions()),
                logger,
                "default"));
    }

    [Fact]
    public void ServiceBoundWorkerRegistration_RejectsDefaultServiceId()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<ArgumentException>(() =>
            services.AddSekibanDcbMaterializedViewWorkerForService("default"));

        Assert.Contains(nameof(MvOptions.AllowDefaultServiceId), exception.Message, StringComparison.Ordinal);
        Assert.Empty(services);
    }

    private static PostgresMvExecutor CreateExecutor(
        IEventStoreFactory sourceFactory,
        IMvRegistryStore registry,
        MvOptions options) =>
        new(
            sourceFactory,
            registry,
            Options.Create(options),
            NullLogger<PostgresMvExecutor>.Instance,
            "Host=unused;Database=unused");

    private static void AssertVerifyOnlyRefusal(
        MvTransitionNotAllowedException exception,
        MvTransition transition)
    {
        Assert.Equal(MvInitializationMode.VerifyOnly, exception.Mode);
        Assert.Equal(transition, exception.Transition);
        Assert.Equal(MvTransitionNotAllowedReason.VerifyOnly, exception.Reason);
        Assert.Equal("bound-service", exception.ServiceId);
        Assert.Equal("Boundary", exception.ViewName);
        Assert.Equal(1, exception.ViewVersion);
    }

    private sealed class ThrowingEventStoreFactory : IEventStoreFactory
    {
        public int CreateCalls { get; private set; }

        public IEventStore CreateForService(string serviceId)
        {
            CreateCalls++;
            throw new InvalidOperationException("Source I/O must not occur during boundary validation.");
        }
    }

    private sealed class CountingRegistryStore : IMvRegistryStore
    {
        public int EnsureCalls { get; private set; }

        public Task EnsureInfrastructureAsync(CancellationToken cancellationToken = default)
        {
            EnsureCalls++;
            throw new InvalidOperationException("Registry I/O must not occur during boundary validation.");
        }

        public Task RegisterAsync(
            MvRegistryEntry entry,
            IDbTransaction? transaction = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task UpdatePositionAsync(
            MvPositionUpdate update,
            IDbTransaction? transaction = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task MarkStreamReceivedAsync(
            string serviceId,
            string viewName,
            int viewVersion,
            string sortableUniqueId,
            DateTimeOffset receivedAt,
            IDbTransaction? transaction = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task UpdateStatusAsync(
            string serviceId,
            string viewName,
            int viewVersion,
            MvStatus status,
            IDbTransaction? transaction = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<MvRegistryEntry>> GetEntriesAsync(
            string serviceId,
            string viewName,
            int viewVersion,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<MvActiveEntry?> GetActiveAsync(
            string serviceId,
            string viewName,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task SetActiveAsync(
            string serviceId,
            string viewName,
            int activeVersion,
            IDbTransaction? transaction = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class BoundaryHostFactory : IMvApplyHostFactory
    {
        public IReadOnlyList<MvApplyHostRegistration> GetRegistrations() => [];

        public IMvApplyHost Create(string viewName, int viewVersion) => new BoundaryHost();
    }

    private sealed class BoundaryHost : IMvApplyHost
    {
        public string ViewName => "Boundary";
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

    private sealed class NoOpExecutor : IMvExecutor
    {
        public Task InitializeAsync(
            IMvApplyHost host,
            string? serviceId = null,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<MvCatchUpResult> CatchUpOnceAsync(
            IMvApplyHost host,
            string? serviceId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new MvCatchUpResult(0, false));

        public Task<int> ApplySerializableEventsAsync(
            IMvApplyHost host,
            IReadOnlyList<SerializableEvent> events,
            string? serviceId = null,
            CancellationToken cancellationToken = default) => Task.FromResult(0);
    }
}
