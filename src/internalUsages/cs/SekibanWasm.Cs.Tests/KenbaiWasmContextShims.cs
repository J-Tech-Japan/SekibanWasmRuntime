using Aic.Kenbai.EventSource.Projections.Kanyushyas;

namespace Aic.Kenbai.EventSource.Wasm;

public static partial class WasmExports
{
    public sealed record KanyushaListProjectionCheckpointPayload(
        KanyushaListProjection Projection,
        Dictionary<string, int>? ReverseIndex);
}
