namespace Sekiban.Dcb.WasmRuntime;

public enum WasmRuntimeMode
{
    Native,
    Wasm,
    Hybrid,
    Remote
}

public class WasmTagStateOptions
{
    public WasmRuntimeMode Mode { get; set; }
    public string? WasmModulePath { get; set; }
    public string? RemoteEndpoint { get; set; }

    public void Validate()
    {
        switch (Mode)
        {
            case WasmRuntimeMode.Wasm:
            case WasmRuntimeMode.Hybrid:
                if (string.IsNullOrWhiteSpace(WasmModulePath))
                {
                    throw new InvalidOperationException(
                        $"WasmModulePath is required for runtime mode '{Mode}'.");
                }
                break;
            case WasmRuntimeMode.Remote:
                if (string.IsNullOrWhiteSpace(RemoteEndpoint))
                {
                    throw new InvalidOperationException(
                        $"RemoteEndpoint is required for runtime mode '{Mode}'.");
                }
                break;
            case WasmRuntimeMode.Native:
                break;
        }
    }
}
