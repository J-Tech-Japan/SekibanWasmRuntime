using ResultBoxes;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Orleans.Serialization;
using Sekiban.Dcb.Runtime;
using Xunit;

namespace SekibanWasm.Cs.Tests;

public sealed class DirectSnapshotQueryCacheTests
{
    [Fact]
    public void Prune_ShouldEvictIdleEntries_WhenCacheExceedsConfiguredLimit()
    {
        var cache = new DirectSnapshotQueryCache(
            new DirectSnapshotQueryCacheOptions
            {
                MaxEntries = 1,
                IdleEntryLifetime = TimeSpan.FromMilliseconds(10),
                EvictRssThresholdBytes = long.MaxValue,
                ResetActiveEntryRssThresholdBytes = long.MaxValue
            },
            rssProvider: static () => 0);

        var first = cache.GetOrAdd("WeatherForecastMultiProjection");
        first.Replace(new StubProjectionActorHost(), metadata: null, "1.0.0", null, 0);
        Thread.Sleep(30);

        var second = cache.GetOrAdd("ReservationListProjection");
        second.Replace(new StubProjectionActorHost(), metadata: null, "1.0.0", null, 0);

        cache.Prune(activeProjectorName: "ReservationListProjection");

        Assert.Equal(1, cache.Count);
        Assert.Equal(["ReservationListProjection"], cache.ProjectorNames);
    }

    [Fact]
    public void Prune_ShouldDisposeInactiveEntries_WhenRssThresholdIsExceeded()
    {
        var firstHost = new StubProjectionActorHost();
        var secondHost = new StubProjectionActorHost();
        var cache = new DirectSnapshotQueryCache(
            new DirectSnapshotQueryCacheOptions
            {
                MaxEntries = 8,
                IdleEntryLifetime = TimeSpan.FromMinutes(5),
                EvictRssThresholdBytes = 1,
                ResetActiveEntryRssThresholdBytes = 2
            },
            rssProvider: static () => 4);

        cache.GetOrAdd("WeatherForecastMultiProjection")
            .Replace(firstHost, metadata: null, "1.0.0", null, 0);
        cache.GetOrAdd("ReservationListProjection")
            .Replace(secondHost, metadata: null, "1.0.0", null, 0);

        cache.Prune(activeProjectorName: "ReservationListProjection");

        Assert.True(firstHost.IsDisposed);
        Assert.False(secondHost.IsDisposed);
        Assert.True(cache.ShouldResetActiveEntryOnMemoryPressure());
    }

    private sealed class StubProjectionActorHost : IProjectionActorHost, IDisposable
    {
        public bool IsDisposed { get; private set; }

        public Task AddSerializableEventsAsync(IReadOnlyList<SerializableEvent> events, bool finishedCatchUp = true) =>
            Task.CompletedTask;

        public Task<ResultBox<ProjectionStateMetadata>> GetStateMetadataAsync(bool includeUnsafe = true) =>
            Task.FromResult(ResultBox.FromValue(new ProjectionStateMetadata(
                ProjectorName: "stub",
                ProjectorVersion: "1.0.0",
                IsCatchedUp: true,
                UnsafeVersion: 0,
                UnsafeLastSortableUniqueId: string.Empty,
                UnsafeLastEventId: null,
                SafeVersion: 0,
                SafeLastSortableUniqueId: string.Empty)));

        public Task<ResultBox<MultiProjectionState>> GetStateAsync(bool canGetUnsafeState = true) =>
            throw new NotSupportedException();

        public Task<ResultBox<bool>> WriteSnapshotToStreamAsync(Stream target, bool canGetUnsafeState, CancellationToken cancellationToken) =>
            Task.FromResult(ResultBox.FromValue(true));

        public Task<ResultBox<bool>> RestoreSnapshotFromStreamAsync(Stream source, CancellationToken cancellationToken) =>
            Task.FromResult(ResultBox.FromValue(true));

        public Task<ResultBox<SerializableQueryResult>> ExecuteQueryAsync(
            SerializableQueryParameter query,
            int? safeVersion,
            string? safeThreshold,
            DateTime? safeThresholdTime,
            int? unsafeVersion) =>
            throw new NotSupportedException();

        public Task<ResultBox<SerializableListQueryResult>> ExecuteListQueryAsync(
            SerializableQueryParameter query,
            int? safeVersion,
            string? safeThreshold,
            DateTime? safeThresholdTime,
            int? unsafeVersion) =>
            throw new NotSupportedException();

        public void ForcePromoteBufferedEvents()
        {
        }

        public void CompactSafeHistory()
        {
        }

        public void ForcePromoteAllBufferedEvents()
        {
        }

        public Task<string> GetSafeLastSortableUniqueIdAsync() => Task.FromResult(string.Empty);

        public Task<bool> IsSortableUniqueIdReceivedAsync(string sortableUniqueId) => Task.FromResult(false);

        public long EstimateStateSizeBytes(bool includeUnsafeDetails) => 0;

        public string PeekCurrentSafeWindowThreshold() => string.Empty;

        public string GetProjectorVersion() => "1.0.0";

        public Task<ResultBox<bool>> RewriteSnapshotVersionAsync(
            Stream source,
            Stream target,
            string newVersion,
            CancellationToken cancellationToken) =>
            Task.FromResult(ResultBox.FromValue(true));

        public void Dispose() => IsDisposed = true;
    }
}
