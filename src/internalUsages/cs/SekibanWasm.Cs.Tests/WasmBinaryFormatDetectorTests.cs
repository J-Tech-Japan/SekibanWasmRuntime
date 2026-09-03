using Sekiban.Dcb.WasmRuntime.Wasmtime;
using Xunit;

namespace SekibanWasm.Cs.Tests;

public sealed class WasmBinaryFormatDetectorTests
{
    [Fact]
    public void IsComponentFile_ReturnsTrue_ForComponentHeader()
    {
        var filePath = CreateTempWasmFile([0x00, 0x61, 0x73, 0x6d, 0x0d, 0x00, 0x01, 0x00]);

        try
        {
            Assert.True(WasmBinaryFormatDetector.IsComponentFile(filePath));
            Assert.False(WasmBinaryFormatDetector.IsSekibanProjectionComponentFile(filePath));
            Assert.False(WasmBinaryFormatDetector.IsCoreModuleFile(filePath));
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void IsSekibanProjectionComponent_ReturnsTrue_ForCompleteJsonBridgeExportSet()
    {
        const string exportNames =
            "create-instance apply-event execute-query execute-list-query serialize-state " +
            "restore-state serialize-event deserialize-event get-event-types";
        byte[] bytes =
        [
            0x00, 0x61, 0x73, 0x6d, 0x0d, 0x00, 0x01, 0x00,
            ..System.Text.Encoding.UTF8.GetBytes(exportNames)
        ];

        Assert.True(WasmBinaryFormatDetector.IsSekibanProjectionComponent(bytes));
    }

    [Fact]
    public void ExistingCSharpPreview2Component_IsNotMisclassifiedAsJsonBridgeComponent()
    {
        string repositoryRoot = FindRepositoryRoot();
        string filePath = Path.Combine(
            repositoryRoot,
            "src",
            "internalUsages",
            "cs",
            "modules",
            "csharp-weather.wasm");

        Assert.True(File.Exists(filePath), $"Existing component artifact not found: {filePath}");
        Assert.True(WasmBinaryFormatDetector.IsComponentFile(filePath));
        Assert.False(WasmBinaryFormatDetector.IsSekibanProjectionComponentFile(filePath));
    }

    [Fact]
    public void IsCoreModuleFile_ReturnsTrue_ForModuleHeader()
    {
        var filePath = CreateTempWasmFile([0x00, 0x61, 0x73, 0x6d, 0x01, 0x00, 0x00, 0x00]);

        try
        {
            Assert.True(WasmBinaryFormatDetector.IsCoreModuleFile(filePath));
            Assert.False(WasmBinaryFormatDetector.IsComponentFile(filePath));
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    private static string CreateTempWasmFile(byte[] headerBytes)
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.wasm");
        File.WriteAllBytes(filePath, headerBytes);
        return filePath;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "global.json")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the SekibanWasmRuntime repository root.");
    }
}
