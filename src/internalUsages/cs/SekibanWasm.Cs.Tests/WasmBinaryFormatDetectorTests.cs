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
            Assert.False(WasmBinaryFormatDetector.IsCoreModuleFile(filePath));
        }
        finally
        {
            File.Delete(filePath);
        }
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
}
