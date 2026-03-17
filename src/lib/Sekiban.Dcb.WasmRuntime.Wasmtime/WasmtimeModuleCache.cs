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
        if (!WasmBinaryFormatDetector.IsComponentFile(componentPath))
        {
            return componentPath;
        }

        WasmtimePreview2ShimResolver.EnsureAvailable();

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
                if (!WasmBinaryFormatDetector.IsCoreModuleFile(coreModulePath))
                {
                    throw new InvalidOperationException(
                        $"Extracted core module '{coreModulePath}' is not a valid WebAssembly core module.");
                }
                return coreModulePath;
            }
            catch (Exception ex)
            {
                if (File.Exists(coreModulePath))
                {
                    try { File.Delete(coreModulePath); } catch { }
                }

                throw new InvalidOperationException(
                    $"Failed to extract a core WebAssembly module from component '{path}'. " +
                    "Ensure the Wasmtime preview2 shim is available, or set WASMTIME_PREVIEW2_SHIM_PATH to the built native library.",
                    ex);
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
