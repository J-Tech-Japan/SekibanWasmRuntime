using global::Wasmtime;

namespace Sekiban.Dcb.WasmRuntime.Wasmtime;

public sealed class WasmtimeRuntime : IDisposable
{
    public Engine Engine { get; }
    public object SyncRoot { get; } = new();

    public WasmtimeRuntime(WasmtimeHostOptions options)
    {
        var config = new Config();
        if (options.StaticMemoryMaximumSizeBytes is { } staticMemoryMaximumSizeBytes &&
            staticMemoryMaximumSizeBytes > 0)
        {
            config.WithStaticMemoryMaximumSize(staticMemoryMaximumSizeBytes);
        }

        Engine = new Engine(config);
    }

    public Linker CreateLinker()
    {
        var linker = new Linker(Engine);
        linker.DefineWasi();
        linker.DefineWasiPreview2Stubs();
        return linker;
    }

    public void Dispose()
    {
        Engine.Dispose();
    }
}
