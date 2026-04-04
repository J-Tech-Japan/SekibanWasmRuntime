using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.WasmRuntime.Host;
using Xunit;

namespace SekibanWasm.Cs.Tests;

public class KnownTagExistenceProbeTests
{
    [Fact]
    public async Task ProbeAsync_ShouldUseTrackerHitWithoutCallingActorStore()
    {
        var tracker = new KnownTagTracker();
        tracker.MarkTagsAsWritten(["Room:room-1"]);
        var accessor = new RecordingActorAccessor(exists: false);
        var probe = new KnownTagExistenceProbe(tracker, accessor);

        var result = await probe.ProbeAsync("Room:room-1");

        Assert.Equal(KnownTagExistence.Warm, result);
        Assert.Equal(0, accessor.ActorExistsCallCount);
    }

    [Fact]
    public async Task ProbeAsync_ShouldBackfillTrackerWhenPersistedTagExists()
    {
        var tracker = new KnownTagTracker();
        var accessor = new RecordingActorAccessor(exists: true);
        var probe = new KnownTagExistenceProbe(tracker, accessor);

        var result = await probe.ProbeAsync("Room:room-2");

        Assert.Equal(KnownTagExistence.Backfilled, result);
        Assert.Equal(1, accessor.ActorExistsCallCount);
        Assert.True(tracker.HasKnownEvents("Room:room-2"));
    }

    [Fact]
    public async Task ProbeAsync_ShouldReturnMissingWhenPersistedTagDoesNotExist()
    {
        var tracker = new KnownTagTracker();
        var accessor = new RecordingActorAccessor(exists: false);
        var probe = new KnownTagExistenceProbe(tracker, accessor);

        var result = await probe.ProbeAsync("Room:room-3");

        Assert.Equal(KnownTagExistence.Missing, result);
        Assert.Equal(1, accessor.ActorExistsCallCount);
        Assert.False(tracker.HasKnownEvents("Room:room-3"));
    }

    private sealed class RecordingActorAccessor : IActorObjectAccessor
    {
        private readonly bool _exists;

        public RecordingActorAccessor(bool exists) => _exists = exists;

        public int ActorExistsCallCount { get; private set; }

        public Task<ResultBox<T>> GetActorAsync<T>(string actorId) where T : class =>
            throw new NotSupportedException();

        public Task<bool> ActorExistsAsync(string actorId)
        {
            ActorExistsCallCount++;
            return Task.FromResult(_exists);
        }
    }
}
