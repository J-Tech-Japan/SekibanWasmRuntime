using Sekiban.Dcb.Actors;

namespace Sekiban.Dcb.WasmRuntime.Host;

internal sealed class KnownTagExistenceProbe
{
    private readonly IActorObjectAccessor _actorAccessor;
    private readonly KnownTagTracker _tracker;

    public KnownTagExistenceProbe(KnownTagTracker tracker, IActorObjectAccessor actorAccessor)
    {
        _tracker = tracker;
        _actorAccessor = actorAccessor;
    }

    public async Task<KnownTagExistence> ProbeAsync(string tag)
    {
        if (_tracker.HasKnownEvents(tag))
        {
            return KnownTagExistence.Warm;
        }

        if (!await _actorAccessor.ActorExistsAsync(tag))
        {
            return KnownTagExistence.Missing;
        }

        _tracker.MarkTagsAsWritten([tag]);
        return KnownTagExistence.Backfilled;
    }

    public void MarkTagsAsWritten(IEnumerable<string> tags) => _tracker.MarkTagsAsWritten(tags);
}

internal enum KnownTagExistence
{
    Missing = 0,
    Warm = 1,
    Backfilled = 2
}
