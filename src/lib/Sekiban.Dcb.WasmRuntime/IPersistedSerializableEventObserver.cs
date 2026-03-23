using Sekiban.Dcb.Events;

namespace Sekiban.Dcb.WasmRuntime;

public interface IPersistedSerializableEventObserver
{
    Task OnPersistedAsync(
        IReadOnlyList<SerializableEvent> events,
        CancellationToken cancellationToken);
}
