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

        // Only probe a manifest colocated with the running host.
        // Repo-level docker manifests must be opted into explicitly via SEKIBAN_MANIFEST_PATH,
        // otherwise local development hosts should fall back to the built-in weather manifest.
        return Path.Combine(Directory.GetCurrentDirectory(), "sekiban-manifest.json");
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
