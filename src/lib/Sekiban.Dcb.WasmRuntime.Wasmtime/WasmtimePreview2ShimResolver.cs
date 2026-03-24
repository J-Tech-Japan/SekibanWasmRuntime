using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Sekiban.Dcb.WasmRuntime.Wasmtime;

internal static class WasmtimePreview2ShimResolver
{
    private const string ShimLibraryName = "wasmtime_preview2_shim";

    private static readonly object Sync = new();
    private static int _resolverRegistered;
    private static bool _buildAttempted;
    private static string? _resolvedPath;

    public static void EnsureAvailable()
    {
        EnsureResolverRegistered();
        _ = ResolveLibraryPath();
    }

    private static void EnsureResolverRegistered()
    {
        if (Interlocked.Exchange(ref _resolverRegistered, 1) == 1)
        {
            return;
        }

        try
        {
            NativeLibrary.SetDllImportResolver(
                typeof(global::Wasmtime.ComponentCoreExtractor).Assembly,
                ResolveShimLibrary);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static IntPtr ResolveShimLibrary(
        string libraryName,
        Assembly assembly,
        DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, ShimLibraryName, StringComparison.Ordinal))
        {
            return IntPtr.Zero;
        }

        var libraryPath = ResolveLibraryPath();
        if (string.IsNullOrWhiteSpace(libraryPath))
        {
            return IntPtr.Zero;
        }

        return NativeLibrary.TryLoad(libraryPath, out var handle) ? handle : IntPtr.Zero;
    }

    private static string? ResolveLibraryPath()
    {
        lock (Sync)
        {
            if (!string.IsNullOrWhiteSpace(_resolvedPath) && File.Exists(_resolvedPath))
            {
                return _resolvedPath;
            }

            foreach (var candidate in EnumerateCandidates())
            {
                if (File.Exists(candidate))
                {
                    _resolvedPath = candidate;
                    return _resolvedPath;
                }
            }

            if (!_buildAttempted)
            {
                _buildAttempted = true;
                var builtLibrary = TryBuildShim();
                if (!string.IsNullOrWhiteSpace(builtLibrary) && File.Exists(builtLibrary))
                {
                    _resolvedPath = builtLibrary;
                    return _resolvedPath;
                }
            }

            return null;
        }
    }

    private static IEnumerable<string> EnumerateCandidates()
    {
        var libraryFileName = GetLibraryFileName();
        var envValue = Environment.GetEnvironmentVariable("WASMTIME_PREVIEW2_SHIM_PATH");
        if (!string.IsNullOrWhiteSpace(envValue))
        {
            if (Directory.Exists(envValue))
            {
                yield return Path.Combine(envValue, libraryFileName);
            }
            else
            {
                yield return envValue;
            }
        }

        foreach (var baseDirectory in new[]
                 {
                     AppContext.BaseDirectory,
                     Path.GetDirectoryName(typeof(global::Wasmtime.ComponentCoreExtractor).Assembly.Location),
                     Directory.GetCurrentDirectory()
                 })
        {
            if (!string.IsNullOrWhiteSpace(baseDirectory))
            {
                yield return Path.Combine(baseDirectory, libraryFileName);
            }
        }

        foreach (var shimDirectory in FindShimDirectories())
        {
            yield return Path.Combine(shimDirectory, "target", "release", libraryFileName);
            yield return Path.Combine(shimDirectory, "target", "debug", libraryFileName);
        }
    }

    private static IEnumerable<string> FindShimDirectories()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var startPath in new[]
                 {
                     AppContext.BaseDirectory,
                     Directory.GetCurrentDirectory(),
                     Path.GetDirectoryName(typeof(global::Wasmtime.ComponentCoreExtractor).Assembly.Location)
                 })
        {
            if (string.IsNullOrWhiteSpace(startPath))
            {
                continue;
            }

            var current = new DirectoryInfo(Path.GetFullPath(startPath));
            while (current is not null)
            {
                foreach (var shimDirectory in EnumerateShimDirectories(current.FullName))
                {
                    if (seen.Add(shimDirectory))
                    {
                        yield return shimDirectory;
                    }
                }

                current = current.Parent;
            }
        }
    }

    private static IEnumerable<string> EnumerateShimDirectories(string rootPath)
    {
        foreach (var relativePath in new[]
                 {
                     Path.Combine("external", "wasmtime-dotnet", "native", "wasmtime-preview2-shim"),
                     Path.Combine("SekibanWasmRuntime", "external", "wasmtime-dotnet", "native", "wasmtime-preview2-shim")
                 })
        {
            var shimDirectory = Path.Combine(rootPath, relativePath);
            if (File.Exists(Path.Combine(shimDirectory, "Cargo.toml")))
            {
                yield return shimDirectory;
            }
        }
    }

    private static string? TryBuildShim()
    {
        foreach (var shimDirectory in FindShimDirectories())
        {
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "cargo",
                        WorkingDirectory = shimDirectory,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.StartInfo.ArgumentList.Add("build");
                process.StartInfo.ArgumentList.Add("--release");

                if (!process.Start())
                {
                    continue;
                }

                if (!process.WaitForExit((int)TimeSpan.FromMinutes(5).TotalMilliseconds))
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                    }
                    continue;
                }

                if (process.ExitCode != 0)
                {
                    continue;
                }

                var builtLibrary = Path.Combine(
                    shimDirectory,
                    "target",
                    "release",
                    GetLibraryFileName());

                if (File.Exists(builtLibrary))
                {
                    return builtLibrary;
                }
            }
            catch
            {
            }
        }

        return null;
    }

    private static string GetLibraryFileName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return $"{ShimLibraryName}.dll";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return $"lib{ShimLibraryName}.dylib";
        }

        return $"lib{ShimLibraryName}.so";
    }
}
