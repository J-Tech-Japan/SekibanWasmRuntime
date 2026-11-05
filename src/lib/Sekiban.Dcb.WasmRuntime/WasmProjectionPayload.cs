using Sekiban.Dcb.MultiProjections;

namespace Sekiban.Dcb.WasmRuntime;

/// <summary>
///     Opaque guest JSON state carried through DCB multi-projection APIs for WASM-hosted projectors.
/// </summary>
public sealed record WasmProjectionPayload(string ProjectorName, string StateJson) : IMultiProjectionPayload;
