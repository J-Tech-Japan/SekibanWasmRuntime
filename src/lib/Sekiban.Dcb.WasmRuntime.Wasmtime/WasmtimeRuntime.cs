using global::Wasmtime;

namespace Sekiban.Dcb.WasmRuntime.Wasmtime;

public sealed class WasmtimeRuntime : IDisposable
{
    public Engine Engine { get; }
    public Linker Linker { get; }

    public WasmtimeRuntime()
    {
        var config = new Config().WithWasmThreads(true);
        Engine = new Engine(config);
        Linker = new Linker(Engine);
        Linker.DefineWasi();
        Linker.DefineWasiPreview2Stubs();
    }

    public void Dispose()
    {
        Linker.Dispose();
        Engine.Dispose();
    }
}
