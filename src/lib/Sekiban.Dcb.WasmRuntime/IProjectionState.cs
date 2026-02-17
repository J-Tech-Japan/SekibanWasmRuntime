using System.Text.Json;

namespace Sekiban.Dcb.Runtime;

/// <summary>
///     Projection state abstraction for C# native and WASM runtime paths.
/// </summary>
public interface IProjectionState
{
    int SafeVersion { get; }
    int UnsafeVersion { get; }
    string? SafeLastSortableUniqueId { get; }
    string? LastSortableUniqueId { get; }
    Guid? LastEventId { get; }
    object? GetSafePayload();
    object? GetUnsafePayload();
    long EstimatePayloadSizeBytes(JsonSerializerOptions? options);
}
