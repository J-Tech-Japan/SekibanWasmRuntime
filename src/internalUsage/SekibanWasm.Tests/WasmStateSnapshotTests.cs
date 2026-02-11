using System.Text.Json;
using Sekiban.Dcb.WasmRuntime;
using Xunit;

namespace SekibanWasm.Tests;

public class WasmStateSnapshotTests
{
    [Fact]
    public void ShouldRoundTripThroughJson()
    {
        // Given
        var eventId = Guid.NewGuid();
        var snapshot = new WasmStateSnapshot(
            StateJson: "{\"forecasts\":{}}",
            SafeVersion: 10,
            UnsafeVersion: 15,
            SafeLastSortableUniqueId: "safe-abc",
            LastSortableUniqueId: "last-xyz",
            LastEventId: eventId);

        // When
        var json = JsonSerializer.Serialize(snapshot);
        var restored = JsonSerializer.Deserialize<WasmStateSnapshot>(json);

        // Then
        Assert.NotNull(restored);
        Assert.Equal("{\"forecasts\":{}}", restored.StateJson);
        Assert.Equal(10, restored.SafeVersion);
        Assert.Equal(15, restored.UnsafeVersion);
        Assert.Equal("safe-abc", restored.SafeLastSortableUniqueId);
        Assert.Equal("last-xyz", restored.LastSortableUniqueId);
        Assert.Equal(eventId, restored.LastEventId);
    }

    [Fact]
    public void ShouldHandleNullValues()
    {
        // Given
        var snapshot = new WasmStateSnapshot(
            StateJson: "{}",
            SafeVersion: 0,
            UnsafeVersion: 0,
            SafeLastSortableUniqueId: null,
            LastSortableUniqueId: null,
            LastEventId: null);

        // When
        var json = JsonSerializer.Serialize(snapshot);
        var restored = JsonSerializer.Deserialize<WasmStateSnapshot>(json);

        // Then
        Assert.NotNull(restored);
        Assert.Null(restored.SafeLastSortableUniqueId);
        Assert.Null(restored.LastSortableUniqueId);
        Assert.Null(restored.LastEventId);
    }
}
