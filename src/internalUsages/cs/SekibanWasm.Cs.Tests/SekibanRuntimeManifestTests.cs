using Sekiban.Dcb.WasmRuntime.Host;
using Xunit;

namespace SekibanWasm.Cs.Tests;

/// <summary>
///     Guards the <see cref="SekibanRuntimeManifest.Validate"/> behaviour that landed with the
///     Swift MV sample (<c>feat/swift-materialized-view</c>). Accepts MV-only manifests but still
///     rejects manifests with neither projectors nor materialized views.
/// </summary>
public class SekibanRuntimeManifestTests
{
    [Fact]
    public void Validate_MvOnlyManifest_Accepted()
    {
        using var moduleFile = new TempWasmFile();
        var manifest = new SekibanRuntimeManifest
        {
            DefaultModulePath = moduleFile.Path,
            EventTypes = ["ClassRoomCreated"],
            Projectors = [],
            MaterializedViews =
            [
                new SekibanRuntimeMaterializedView
                {
                    ViewName = "ClassRoomEnrollment",
                    ViewVersion = 1,
                    LogicalTables = ["classrooms", "students", "enrollments"]
                }
            ]
        };

        // Should not throw — an MV-only manifest is a valid configuration.
        manifest.Validate();
    }

    [Fact]
    public void Validate_NoProjectorsNoMaterializedViews_Throws()
    {
        using var moduleFile = new TempWasmFile();
        var manifest = new SekibanRuntimeManifest
        {
            DefaultModulePath = moduleFile.Path,
            EventTypes = ["ClassRoomCreated"],
            Projectors = [],
            MaterializedViews = []
        };

        var ex = Assert.Throws<InvalidOperationException>(manifest.Validate);
        Assert.Contains("projector", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_ProjectorOnlyManifest_StillAccepted()
    {
        using var moduleFile = new TempWasmFile();
        var manifest = new SekibanRuntimeManifest
        {
            DefaultModulePath = moduleFile.Path,
            EventTypes = ["ClassRoomCreated"],
            Projectors =
            [
                new SekibanRuntimeProjector
                {
                    ProjectorName = "ClassRoomProjector",
                    ModulePath = moduleFile.Path,
                    ProjectorVersion = "1.0.0"
                }
            ],
            MaterializedViews = []
        };

        manifest.Validate();
    }

    // Validate() requires DefaultModulePath to point at an existing file; the tests don't care
    // about wasm contents so a throw-away empty file is enough.
    private sealed class TempWasmFile : IDisposable
    {
        public string Path { get; }

        public TempWasmFile()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"sekiban-runtime-manifest-test-{Guid.NewGuid():N}.wasm");
            File.WriteAllBytes(Path, new byte[] { 0x00, 0x61, 0x73, 0x6d }); // \0asm magic
        }

        public void Dispose()
        {
            try
            {
                if (File.Exists(Path)) File.Delete(Path);
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }
}
