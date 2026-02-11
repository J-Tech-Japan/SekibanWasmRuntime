using Sekiban.Dcb.WasmRuntime;
using Xunit;

namespace SekibanWasm.Tests;

public class WasmProjectionStateTests
{
    [Fact]
    public void Constructor_ShouldInitializeWithDefaultMetadata()
    {
        // Given
        var mockInstance = new FakePrimitiveProjectionInstance();

        // When
        var state = new WasmProjectionState(mockInstance, "TestProjector");

        // Then
        Assert.Equal("TestProjector", state.ProjectorName);
        Assert.Equal(0, state.SafeVersion);
        Assert.Equal(0, state.UnsafeVersion);
        Assert.Null(state.SafeLastSortableUniqueId);
        Assert.Null(state.LastSortableUniqueId);
        Assert.Null(state.LastEventId);
    }

    [Fact]
    public void Constructor_WithSnapshot_ShouldRestoreMetadata()
    {
        // Given
        var mockInstance = new FakePrimitiveProjectionInstance();
        var eventId = Guid.NewGuid();
        var snapshot = new WasmStateSnapshot(
            "{\"test\":true}",
            SafeVersion: 5,
            UnsafeVersion: 10,
            SafeLastSortableUniqueId: "safe-123",
            LastSortableUniqueId: "last-456",
            LastEventId: eventId);

        // When
        var state = new WasmProjectionState(mockInstance, "TestProjector", snapshot);

        // Then
        Assert.Equal(5, state.SafeVersion);
        Assert.Equal(10, state.UnsafeVersion);
        Assert.Equal("safe-123", state.SafeLastSortableUniqueId);
        Assert.Equal("last-456", state.LastSortableUniqueId);
        Assert.Equal(eventId, state.LastEventId);
    }

    [Fact]
    public void GetSafePayload_ShouldReturnNull()
    {
        // Given
        var mockInstance = new FakePrimitiveProjectionInstance();
        var state = new WasmProjectionState(mockInstance, "TestProjector");

        // When / Then
        Assert.Null(state.GetSafePayload());
    }

    [Fact]
    public void GetUnsafePayload_ShouldReturnNull()
    {
        // Given
        var mockInstance = new FakePrimitiveProjectionInstance();
        var state = new WasmProjectionState(mockInstance, "TestProjector");

        // When / Then
        Assert.Null(state.GetUnsafePayload());
    }
}
