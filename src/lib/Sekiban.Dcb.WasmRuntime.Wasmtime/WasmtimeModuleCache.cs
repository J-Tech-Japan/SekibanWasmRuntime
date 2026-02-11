using System.Collections.Concurrent;
using System.Security.Cryptography;
using global::Wasmtime;

namespace Sekiban.Dcb.WasmRuntime.Wasmtime;

public sealed class WasmtimeModuleCache
{
    private readonly WasmtimeRuntime _runtime;
    private readonly ConcurrentDictionary<string, Module> _modules = new();
    private readonly ConcurrentDictionary<string, string> _extractedCorePaths = new();
    private readonly string _cacheDir;

    public WasmtimeModuleCache(WasmtimeRuntime runtime)
    {
        _runtime = runtime;
        _cacheDir = Path.Combine(Path.GetTempPath(), "wasm-core-cache");
        Directory.CreateDirectory(_cacheDir);
    }

    public Module GetOrLoad(string modulePath)
    {
        if (string.IsNullOrWhiteSpace(modulePath))
        {
            throw new InvalidOperationException("Module path is missing.");
        }

        var effectivePath = GetOrExtractCoreModule(modulePath);
        return _modules.GetOrAdd(effectivePath, path => Module.FromFile(_runtime.Engine, path));
    }

    private string GetOrExtractCoreModule(string componentPath)
    {
        return _extractedCorePaths.GetOrAdd(componentPath, path =>
        {
            var hash = ComputeFileHash(path);
            var coreModulePath = Path.Combine(_cacheDir, $"core-{hash}.wasm");

            if (File.Exists(coreModulePath))
            {
                return coreModulePath;
            }

            try
            {
                ComponentCoreExtractor.ExtractMainModule(path, coreModulePath);
                return coreModulePath;
            }
            catch
            {
                if (File.Exists(coreModulePath))
                {
                    try { File.Delete(coreModulePath); } catch { }
                }
                return path;
            }
        });
    }

    private static string ComputeFileHash(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hashBytes = SHA256.HashData(stream);
        return Convert.ToHexString(hashBytes)[..16].ToLowerInvariant();
    }
}
