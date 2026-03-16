namespace Sekiban.Dcb.WasmRuntime.Host;

internal static class ManifestPathResolver
{
    public static string Resolve(IConfiguration configuration)
    {
        var configuredPath = Environment.GetEnvironmentVariable("SEKIBAN_MANIFEST_PATH");
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            configuredPath = configuration["Sekiban:ManifestPath"];
        }

        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var resolvedConfiguredPath = ResolveRelativeToCurrentDirectory(configuredPath);
            if (!File.Exists(resolvedConfiguredPath))
            {
                throw new InvalidOperationException(
                    $"Configured manifest path '{resolvedConfiguredPath}' does not exist.");
            }

            return resolvedConfiguredPath;
        }

        var defaultCandidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "sekiban-manifest.json"),
            Path.GetFullPath(Path.Combine(
                Directory.GetCurrentDirectory(),
                "..",
                "..",
                "..",
                "docker",
                "sekiban-wasm-runtime",
                "config",
                "sekiban-manifest.json"))
        };

        return defaultCandidates.FirstOrDefault(File.Exists) ?? defaultCandidates[0];
    }

    private static string ResolveRelativeToCurrentDirectory(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return path;
        }

        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), path));
    }
}
