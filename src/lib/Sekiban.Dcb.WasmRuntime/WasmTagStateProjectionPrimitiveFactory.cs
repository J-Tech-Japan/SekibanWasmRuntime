using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Primitives;
using Sekiban.Dcb.Runtime;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.WasmRuntime;

/// <summary>
/// Registers a WASM-backed TagState primitive behind Sekiban's latest accumulator contract.
/// </summary>
public sealed class WasmTagStateProjectionPrimitiveFactory : ITagStateProjectionPrimitive
{
    private readonly IPrimitiveProjectionHost _host;
    private readonly WasmProjectorRegistry _registry;
    private readonly IEventTypes _eventTypes;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ILogger<WasmTagStateProjectionPrimitiveFactory> _logger;

    public WasmTagStateProjectionPrimitiveFactory(
        IPrimitiveProjectionHost host,
        WasmProjectorRegistry registry,
        IEventTypes eventTypes,
        JsonSerializerOptions jsonOptions,
        ILogger<WasmTagStateProjectionPrimitiveFactory> logger)
    {
        _host = host;
        _registry = registry;
        _eventTypes = eventTypes;
        _jsonOptions = jsonOptions;
        _logger = logger;
    }

    public ITagStateProjectionAccumulator CreateAccumulator(TagStateId tagStateId)
    {
        var moduleRef = _registry.TryGet(tagStateId.TagProjectorName);
        try
        {
            var instance = _host.CreateInstance(tagStateId.TagProjectorName);
            return new WasmTagStateProjectionPrimitive(
                instance,
                tagStateId.TagProjectorName,
                moduleRef?.ProjectorVersion ?? string.Empty,
                _eventTypes,
                _jsonOptions,
                moduleRef?.TagPayloadName);
        }
        catch (Exception ex)
        {
            if (moduleRef is null)
            {
                _logger.LogDebug(
                    ex,
                    "No manifest entry for tag projector {ProjectorName}, and default module instance creation failed; returning empty accumulator.",
                    tagStateId.TagProjectorName);
            }
            else
            {
                _logger.LogWarning(
                    ex,
                    "Failed to create WASM accumulator for tag projector {ProjectorName}; returning empty accumulator.",
                    tagStateId.TagProjectorName);
            }

            return new MissingTagStateProjectionAccumulator(
                tagStateId,
                moduleRef?.ProjectorVersion ?? string.Empty);
        }
    }

    private sealed class MissingTagStateProjectionAccumulator : ITagStateProjectionAccumulator
    {
        private readonly TagStateId _tagStateId;
        private readonly string _projectorVersion;
        private SerializableTagState? _cachedState;

        public MissingTagStateProjectionAccumulator(TagStateId tagStateId, string projectorVersion)
        {
            _tagStateId = tagStateId;
            _projectorVersion = projectorVersion;
        }

        public bool ApplyState(SerializableTagState? cachedState)
        {
            _cachedState = cachedState;
            return true;
        }

        public bool ApplyEvents(
            IReadOnlyList<SerializableEvent> events,
            string? latestSortableUniqueId,
            CancellationToken cancellationToken = default) => events.Count == 0;

        public SerializableTagState GetSerializedState() =>
            _cachedState ??
            new(
                Payload: Array.Empty<byte>(),
                Version: 0,
                LastSortedUniqueId: string.Empty,
                TagGroup: _tagStateId.TagGroup,
                TagContent: _tagStateId.TagContent,
                TagProjector: _tagStateId.TagProjectorName,
                TagPayloadName: nameof(EmptyTagStatePayload),
                ProjectorVersion: _projectorVersion);

        public void Dispose()
        {
        }
    }
}
