using Sekiban.Dcb;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.WasmRuntime.Remote;

/// <summary>
///     Re-publishes committed serialized events in the caller process.
///     This is required when the process that executes serialized commits is
///     different from the process that owns side-effect subscribers.
/// </summary>
public static class SerializedCommitResultRepublisher
{
    public static async Task PublishWrittenEventsAsync(
        SerializedCommitResult commitResult,
        DcbDomainTypes domainTypes,
        IEventPublisher? eventPublisher,
        CancellationToken cancellationToken = default)
    {
        if (eventPublisher is null || commitResult.WrittenEvents.Count == 0)
        {
            return;
        }

        var publishedEvents = new List<(Event Event, IReadOnlyCollection<ITag> Tags)>(commitResult.WrittenEvents.Count);
        foreach (SerializableEvent writtenEvent in commitResult.WrittenEvents)
        {
            var eventResult = writtenEvent.ToEvent(domainTypes.EventTypes);
            if (!eventResult.IsSuccess)
            {
                throw eventResult.GetException();
            }

            IReadOnlyCollection<ITag> tags = writtenEvent.Tags
                .Select(domainTypes.TagTypes.GetTag)
                .ToList();

            publishedEvents.Add((eventResult.GetValue(), tags));
        }

        await eventPublisher.PublishAsync(publishedEvents, cancellationToken);
    }
}
