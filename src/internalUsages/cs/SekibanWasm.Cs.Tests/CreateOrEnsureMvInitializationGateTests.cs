using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.MaterializedView;
using Sekiban.Dcb.MaterializedView.Postgres;
using Sekiban.Dcb.WasmRuntime.Host.MaterializedView;
using SekibanWasm.Cs.Domain;
using Xunit;

namespace SekibanWasm.Cs.Tests;

public sealed class CreateOrEnsureMvInitializationGateTests
{
    [Fact]
    public async Task Wrap_SerializesOverlappingInitializeAsyncCalls()
    {
        var inner = new SerializingStubExecutor();
        var gated = CreateOrEnsureMvInitializationGate.Wrap(inner);

        await Task.WhenAll(
            gated.InitializeAsync(inner.Host, "tenant-a"),
            gated.InitializeAsync(inner.Host, "tenant-a"));

        Assert.Equal(1, inner.MaxInFlight);
        Assert.Equal(2, inner.InitializeCallCount);
    }

    [Fact]
    public void Wrap_ImplementsEveryPostgresMvExecutorInterface()
    {
        var inner = CreateUnusedPostgresExecutor();
        var gated = CreateOrEnsureMvInitializationGate.Wrap(inner);

        var expected = typeof(PostgresMvExecutor)
            .GetInterfaces()
            .Select(static iface => iface.FullName)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
        var actual = gated
            .GetType()
            .GetInterfaces()
            .Select(static iface => iface.FullName)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, actual);
    }

    private static PostgresMvExecutor CreateUnusedPostgresExecutor()
    {
        return new PostgresMvExecutor(
            new InMemoryEventStoreFactory(DomainType.GetDomainTypes().EventTypes),
            new PostgresMvRegistryStore("Host=unused;Database=unused"),
            Options.Create(new MvOptions
            {
                ServiceId = "tenant-a",
                InitializationMode = MvInitializationMode.CreateOrEnsure
            }),
            NullLogger<PostgresMvExecutor>.Instance,
            "Host=unused;Database=unused");
    }

    private sealed class SerializingStubExecutor : IMvExecutor
    {
        private int _inFlight;
        private int _maxInFlight;

        public ContractHost Host { get; } = new();

        public int MaxInFlight => _maxInFlight;

        public int InitializeCallCount { get; private set; }

        public async Task InitializeAsync(
            IMvApplyHost host,
            string? serviceId = null,
            CancellationToken cancellationToken = default)
        {
            var current = Interlocked.Increment(ref _inFlight);
            var observed = Volatile.Read(ref _maxInFlight);
            while (current > observed &&
                   Interlocked.CompareExchange(ref _maxInFlight, current, observed) != observed)
            {
                observed = Volatile.Read(ref _maxInFlight);
            }

            InitializeCallCount++;
            try
            {
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }

        public Task<MvCatchUpResult> CatchUpOnceAsync(
            IMvApplyHost host,
            string? serviceId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new MvCatchUpResult(0, false));

        public Task<int> ApplySerializableEventsAsync(
            IMvApplyHost host,
            IReadOnlyList<SerializableEvent> events,
            string? serviceId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }

    private sealed class ContractHost : IMvApplyHost
    {
        public string ViewName => "GateView";
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
}
