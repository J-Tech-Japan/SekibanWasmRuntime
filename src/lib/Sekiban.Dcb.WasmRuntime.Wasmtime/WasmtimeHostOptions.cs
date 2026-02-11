namespace Sekiban.Dcb.WasmRuntime.Wasmtime;

public class WasmtimeHostOptions
{
    public string? DefaultModulePath { get; set; }

    public Dictionary<string, string> ProjectorModulePaths { get; set; } = new();

    public string? ResolveModulePath(string projectorName)
    {
        if (ProjectorModulePaths.TryGetValue(projectorName, out var path))
        {
            return path;
        }
        return DefaultModulePath;
    }
}
