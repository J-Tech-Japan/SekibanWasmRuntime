using System.Collections;

namespace Sekiban.Dcb.WasmRuntime.Wasmtime;

public class WasmtimeHostOptions
{
    private static readonly string[] DefaultGuestEnvironmentPrefixes =
    [
        "KENBAI_WASM_",
        "SEKIBAN_WASM_",
        "WASM_RUNTIME_"
    ];

    public string? DefaultModulePath { get; set; }

    public Dictionary<string, string> ProjectorModulePaths { get; set; } = new();

    public Dictionary<string, string> GuestEnvironmentVariables { get; set; } =
        new(StringComparer.Ordinal);

    public bool IncludeHostEnvironmentByPrefix { get; set; } = true;

    public IReadOnlyList<string> HostEnvironmentPrefixes { get; set; } = DefaultGuestEnvironmentPrefixes;

    public string? ResolveModulePath(string projectorName)
    {
        if (ProjectorModulePaths.TryGetValue(projectorName, out var path))
        {
            return path;
        }
        return DefaultModulePath;
    }

    public IEnumerable<KeyValuePair<string, string>> ResolveGuestEnvironmentVariables()
    {
        var yielded = new HashSet<string>(StringComparer.Ordinal);

        foreach ((string key, string value) in GuestEnvironmentVariables)
        {
            yielded.Add(key);
            yield return new KeyValuePair<string, string>(key, value);
        }

        if (!IncludeHostEnvironmentByPrefix)
        {
            yield break;
        }

        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is not string key ||
                entry.Value is not string value ||
                yielded.Contains(key) ||
                !HostEnvironmentPrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal)))
            {
                continue;
            }

            yield return new KeyValuePair<string, string>(key, value);
        }
    }
}
