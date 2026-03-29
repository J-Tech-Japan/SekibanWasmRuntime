using Sekiban.Dcb.Events;

namespace Sekiban.Dcb.WasmRuntime;

public interface ISerializableEventBatchProjectionInstance
{
    void ApplySerializableEvents(IReadOnlyList<SerializableEvent> events);
}
