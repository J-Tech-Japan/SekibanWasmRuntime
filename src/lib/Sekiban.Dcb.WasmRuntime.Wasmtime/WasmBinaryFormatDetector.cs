namespace Sekiban.Dcb.WasmRuntime.Wasmtime;

public static class WasmBinaryFormatDetector
{
    private static ReadOnlySpan<byte> WasmMagic => [0x00, 0x61, 0x73, 0x6d];
    private static ReadOnlySpan<byte> ModuleVersion => [0x01, 0x00, 0x00, 0x00];
    private static ReadOnlySpan<byte> ComponentVersion => [0x0d, 0x00, 0x01, 0x00];

    public static bool IsComponentFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return false;
        }

        Span<byte> header = stackalloc byte[8];
        using var stream = File.OpenRead(filePath);
        if (stream.Read(header) < header.Length)
        {
            return false;
        }

        return header[..4].SequenceEqual(WasmMagic) &&
               header[4..8].SequenceEqual(ComponentVersion);
    }

    public static bool IsCoreModuleFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return false;
        }

        Span<byte> header = stackalloc byte[8];
        using var stream = File.OpenRead(filePath);
        if (stream.Read(header) < header.Length)
        {
            return false;
        }

        return header[..4].SequenceEqual(WasmMagic) &&
               header[4..8].SequenceEqual(ModuleVersion);
    }
}
