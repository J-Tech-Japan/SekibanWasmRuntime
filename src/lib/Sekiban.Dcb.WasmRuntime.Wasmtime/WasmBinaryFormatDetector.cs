using System.Text;

namespace Sekiban.Dcb.WasmRuntime.Wasmtime;

public static class WasmBinaryFormatDetector
{
    private static ReadOnlySpan<byte> WasmMagic => [0x00, 0x61, 0x73, 0x6d];
    private static ReadOnlySpan<byte> ModuleVersion => [0x01, 0x00, 0x00, 0x00];
    private static ReadOnlySpan<byte> ComponentVersion => [0x0d, 0x00, 0x01, 0x00];
    private static readonly byte[][] SekibanProjectionExportNames =
    [
        Encoding.UTF8.GetBytes("create-instance"),
        Encoding.UTF8.GetBytes("apply-event"),
        Encoding.UTF8.GetBytes("execute-query"),
        Encoding.UTF8.GetBytes("execute-list-query"),
        Encoding.UTF8.GetBytes("serialize-state"),
        Encoding.UTF8.GetBytes("restore-state"),
        Encoding.UTF8.GetBytes("serialize-event"),
        Encoding.UTF8.GetBytes("deserialize-event"),
        Encoding.UTF8.GetBytes("get-event-types")
    ];

    public static bool IsCoreModule(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 8 &&
        bytes[..4].SequenceEqual(WasmMagic) &&
        bytes[4..8].SequenceEqual(ModuleVersion);

    public static bool IsComponent(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 8 &&
        bytes[..4].SequenceEqual(WasmMagic) &&
        bytes[4..8].SequenceEqual(ComponentVersion);

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

        return IsComponent(header);
    }

    /// <summary>
    /// Identifies the component world consumed by <see cref="WasmtimeComponentProjectionInstance"/>.
    /// Other WASI Preview2 components, including the existing AssemblyScript/C# guest artifacts,
    /// must continue through <see cref="WasmtimeModuleCache"/>'s core-module extraction path.
    /// </summary>
    public static bool IsSekibanProjectionComponentFile(string filePath)
    {
        if (!IsComponentFile(filePath))
        {
            return false;
        }

        byte[] bytes = File.ReadAllBytes(filePath);
        return IsSekibanProjectionComponent(bytes);
    }

    public static bool IsSekibanProjectionComponent(ReadOnlySpan<byte> bytes)
    {
        if (!IsComponent(bytes))
        {
            return false;
        }

        foreach (byte[] exportName in SekibanProjectionExportNames)
        {
            if (bytes.IndexOf(exportName) < 0)
            {
                return false;
            }
        }

        return true;
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

        return IsCoreModule(header);
    }
}
