using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;

namespace Sekiban.Dcb.WasmRuntime;

public static class PersistedSerializableEventFactory
{
    public static IReadOnlyList<SerializableEvent> FromExecutionResult(
        ExecutionResult executionResult,
        IEventTypes eventTypes) =>
        executionResult.Events
            .Select(evt => evt.ToSerializableEvent(eventTypes))
            .ToList();
}
