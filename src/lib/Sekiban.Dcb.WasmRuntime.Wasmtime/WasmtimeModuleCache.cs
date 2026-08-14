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

    /// <summary>
    /// Returns the immutable core-module bytes derived from one already-read module artifact.
    /// Core modules are returned unchanged. Components are extracted through the same preview2
    /// shim as the normal cache, but the input is first written from <paramref name="sourceBytes"
    /// /> so a path replacement cannot influence the extraction.
    /// </summary>
    public byte[] ReadEffectiveModuleBytes(string modulePath, ReadOnlySpan<byte> sourceBytes)
    {
        if (!WasmBinaryFormatDetector.IsComponent(sourceBytes))
        {
            if (!WasmBinaryFormatDetector.IsCoreModule(sourceBytes))
            {
                throw new InvalidOperationException(
                    $"WASM artifact '{modulePath}' is neither a core module nor a component.");
            }

            return sourceBytes.ToArray();
        }

        WasmtimePreview2ShimResolver.EnsureAvailable();
        var token = Guid.NewGuid().ToString("N");
        var sourcePath = Path.Combine(_cacheDir, $"component-source-{token}.wasm");
        var corePath = Path.Combine(_cacheDir, $"component-core-{token}.wasm");
        try
        {
            File.WriteAllBytes(sourcePath, sourceBytes.ToArray());
            ComponentCoreExtractor.ExtractMainModule(sourcePath, corePath);
            var coreBytes = File.ReadAllBytes(corePath);
            if (!WasmBinaryFormatDetector.IsCoreModule(coreBytes))
            {
                throw new InvalidOperationException(
                    $"Extracted core module for '{modulePath}' is not a valid WebAssembly core module.");
            }

            return coreBytes;
        }
        finally
        {
            TryDelete(sourcePath);
            TryDelete(corePath);
        }
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

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Temporary extraction artifacts are best-effort cleanup only.
        }
    }
}
