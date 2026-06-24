namespace Sekiban.Dcb.WasmRuntime.Host;

/// <summary>A single readiness component result surfaced by the <c>/ready</c> endpoint.</summary>
internal sealed record ReadinessCheck(string Name, bool Ok, string Detail);

/// <summary>Aggregate readiness result: <see cref="Ready"/> is true only when every check passed.</summary>
internal sealed record ReadinessReport(bool Ready, IReadOnlyList<ReadinessCheck> Checks);

/// <summary>
///     Strict readiness evaluation for the runtime host. Unlike <c>/health</c> (lightweight
///     liveness), this verifies that the manifest and WASM module are present, the storage
///     provider is configured, and — when a probe is supplied — that the database is reachable.
///     The database probe is injected so the file/config checks stay hermetically testable.
/// </summary>
internal static class ReadinessChecker
{
    public static async Task<ReadinessReport> EvaluateAsync(
        string manifestPath,
        string wasmModulePath,
        string storageProvider,
        Func<CancellationToken, Task<ReadinessCheck>>? databaseProbe,
        CancellationToken cancellationToken = default)
    {
        var checks = new List<ReadinessCheck>
        {
            FileCheck("manifest", manifestPath, "manifest file"),
            FileCheck("wasmModule", wasmModulePath, "WASM module"),
            StorageProviderCheck(storageProvider),
        };

        if (databaseProbe is not null)
        {
            try
            {
                checks.Add(await databaseProbe(cancellationToken));
            }
            catch (Exception ex)
            {
                checks.Add(new ReadinessCheck("database", false, $"probe failed: {ex.Message}"));
            }
        }

        return new ReadinessReport(checks.All(static check => check.Ok), checks);
    }

    private static ReadinessCheck FileCheck(string name, string path, string label) =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(path)
            ? new ReadinessCheck(name, true, path)
            : new ReadinessCheck(name, false, $"{label} not found at '{path}'");

    private static ReadinessCheck StorageProviderCheck(string storageProvider) =>
        !string.IsNullOrWhiteSpace(storageProvider)
            ? new ReadinessCheck("storageProvider", true, storageProvider)
            : new ReadinessCheck("storageProvider", false, "storage provider not configured");
}
