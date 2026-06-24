using Sekiban.Dcb.WasmRuntime.Host;
using Xunit;

namespace SekibanWasm.Cs.Tests;

public class RuntimeReadinessTests
{
    private static (string Manifest, string Module) CreateExistingFiles()
        => (Path.GetTempFileName(), Path.GetTempFileName());

    private static string MissingPath(string extension)
        => Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}{extension}");

    [Fact]
    public async Task EvaluateAsync_IsReady_WhenAllChecksPass()
    {
        var (manifest, module) = CreateExistingFiles();
        try
        {
            var report = await ReadinessChecker.EvaluateAsync(
                manifest, module, "postgres",
                _ => Task.FromResult(new ReadinessCheck("database", true, "postgres reachable")));

            Assert.True(report.Ready);
            Assert.Equal(4, report.Checks.Count);
            Assert.All(report.Checks, check => Assert.True(check.Ok));
        }
        finally
        {
            File.Delete(manifest);
            File.Delete(module);
        }
    }

    [Fact]
    public async Task EvaluateAsync_IsReady_WithoutDatabaseProbe_WhenFilesAndProviderPresent()
    {
        var (manifest, module) = CreateExistingFiles();
        try
        {
            var report = await ReadinessChecker.EvaluateAsync(
                manifest, module, "sqlite", databaseProbe: null);

            Assert.True(report.Ready);
            Assert.Equal(3, report.Checks.Count);
        }
        finally
        {
            File.Delete(manifest);
            File.Delete(module);
        }
    }

    [Fact]
    public async Task EvaluateAsync_IsNotReady_WhenManifestMissing()
    {
        var module = Path.GetTempFileName();
        try
        {
            var report = await ReadinessChecker.EvaluateAsync(
                MissingPath(".json"), module, "postgres",
                _ => Task.FromResult(new ReadinessCheck("database", true, "postgres reachable")));

            Assert.False(report.Ready);
            Assert.False(Assert.Single(report.Checks, check => check.Name == "manifest").Ok);
        }
        finally
        {
            File.Delete(module);
        }
    }

    [Fact]
    public async Task EvaluateAsync_IsNotReady_WhenWasmModuleMissing()
    {
        var manifest = Path.GetTempFileName();
        try
        {
            var report = await ReadinessChecker.EvaluateAsync(
                manifest, MissingPath(".wasm"), "postgres",
                _ => Task.FromResult(new ReadinessCheck("database", true, "postgres reachable")));

            Assert.False(report.Ready);
            Assert.False(Assert.Single(report.Checks, check => check.Name == "wasmModule").Ok);
        }
        finally
        {
            File.Delete(manifest);
        }
    }

    [Fact]
    public async Task EvaluateAsync_IsNotReady_WhenDatabaseUnreachable()
    {
        var (manifest, module) = CreateExistingFiles();
        try
        {
            var report = await ReadinessChecker.EvaluateAsync(
                manifest, module, "postgres",
                _ => Task.FromResult(new ReadinessCheck("database", false, "postgres not reachable")));

            Assert.False(report.Ready);
            Assert.False(Assert.Single(report.Checks, check => check.Name == "database").Ok);
        }
        finally
        {
            File.Delete(manifest);
            File.Delete(module);
        }
    }

    [Fact]
    public async Task EvaluateAsync_IsNotReady_WhenDatabaseProbeThrows()
    {
        var (manifest, module) = CreateExistingFiles();
        try
        {
            var report = await ReadinessChecker.EvaluateAsync(
                manifest, module, "postgres",
                _ => throw new InvalidOperationException("boom"));

            Assert.False(report.Ready);
            var database = Assert.Single(report.Checks, check => check.Name == "database");
            Assert.False(database.Ok);
            Assert.Contains("boom", database.Detail);
        }
        finally
        {
            File.Delete(manifest);
            File.Delete(module);
        }
    }
}
