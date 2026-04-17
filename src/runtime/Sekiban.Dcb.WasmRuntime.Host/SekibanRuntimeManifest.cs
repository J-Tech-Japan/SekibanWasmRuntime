using System.Text.Json;
using Sekiban.Dcb.WasmRuntime;

namespace Sekiban.Dcb.WasmRuntime.Host;

public sealed class SekibanRuntimeManifest
{
    public string DefaultModulePath { get; init; } = string.Empty;
    public string QueryAssemblyVersion { get; init; } = "wasm";
    public List<string> EventTypes { get; init; } = [];
    public List<SekibanRuntimeProjector> Projectors { get; init; } = [];
    public Dictionary<string, string> QueryProjectors { get; init; } =
        new(StringComparer.Ordinal);

    /// <summary>
    ///     Materialized view projectors exposed by the WASM module through the `mv_metadata`,
    ///     `mv_initialize`, `mv_apply_event` exports. When empty, the MV runtime is not
    ///     activated on the host even if the Sekiban.Dcb.MaterializedView.Postgres connection
    ///     string is configured.
    /// </summary>
    public List<SekibanRuntimeMaterializedView> MaterializedViews { get; init; } = [];

    public static SekibanRuntimeManifest Load(
        IConfiguration configuration,
        string manifestPath)
    {
        if (File.Exists(manifestPath))
        {
            var json = File.ReadAllText(manifestPath);
            var manifest = JsonSerializer.Deserialize<SekibanRuntimeManifest>(
                               json,
                               new JsonSerializerOptions(JsonSerializerDefaults.Web)) ??
                           throw new InvalidOperationException(
                               $"Failed to deserialize manifest at '{manifestPath}'.");
            return manifest.ResolveRelativePaths(manifestPath);
        }

        return CreateDefaultWeatherManifest(configuration);
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(DefaultModulePath))
        {
            throw new InvalidOperationException("DefaultModulePath is required.");
        }

        if (!File.Exists(DefaultModulePath))
        {
            throw new InvalidOperationException(
                $"WASM module not found at '{DefaultModulePath}'.");
        }

        if (Projectors.Count == 0)
        {
            throw new InvalidOperationException("At least one projector must be configured.");
        }

        if (EventTypes.Count == 0)
        {
            throw new InvalidOperationException("At least one event type must be configured.");
        }

        foreach (var projector in Projectors)
        {
            if (string.IsNullOrWhiteSpace(projector.ProjectorName))
            {
                throw new InvalidOperationException("Each projector must define ProjectorName.");
            }

            if (!File.Exists(projector.ModulePath))
            {
                throw new InvalidOperationException(
                    $"Projector '{projector.ProjectorName}' module not found at '{projector.ModulePath}'.");
            }
        }
    }

    public WasmProjectorRegistry CreateRegistry()
    {
        var registry = new WasmProjectorRegistry();
        foreach (var projector in Projectors)
        {
            registry.Register(new WasmModuleRef(
                ProjectorName: projector.ProjectorName,
                ModulePath: projector.ModulePath,
                AbiKind: projector.AbiKind,
                ModuleVersion: projector.ModuleVersion,
                ProjectorVersion: projector.ProjectorVersion,
                TagPayloadName: projector.TagPayloadName ?? InferTagPayloadName(projector.ProjectorName)));
        }

        foreach (var (queryType, projectorName) in QueryProjectors)
        {
            registry.MapQueryToProjector(queryType, projectorName);
        }

        return registry;
    }

    /// <summary>
    /// Heuristic: "RoomProjector" → "RoomState", "WeatherForecastProjector" → "WeatherForecastState".
    /// WASM projectors don't know C# type names, so the host infers the payload name from the projector name.
    /// </summary>
    internal static string InferTagPayloadName(string projectorName)
    {
        if (projectorName.EndsWith("Projector", StringComparison.Ordinal))
        {
            return projectorName[..^"Projector".Length] + "State";
        }
        return projectorName;
    }

    private SekibanRuntimeManifest ResolveRelativePaths(string manifestPath)
    {
        var baseDirectory = Path.GetDirectoryName(manifestPath) ?? Directory.GetCurrentDirectory();

        return new SekibanRuntimeManifest
        {
            DefaultModulePath = ResolvePath(DefaultModulePath, baseDirectory),
            QueryAssemblyVersion = QueryAssemblyVersion,
            EventTypes = [.. EventTypes],
            QueryProjectors = new Dictionary<string, string>(QueryProjectors, StringComparer.Ordinal),
            Projectors = Projectors.Select(projector => projector.ResolvePath(baseDirectory)).ToList(),
            MaterializedViews = MaterializedViews.Select(mv => mv.ResolvePath(baseDirectory)).ToList()
        };
    }

    private static SekibanRuntimeManifest CreateDefaultWeatherManifest(IConfiguration configuration)
    {
        var moduleCandidates = new[]
        {
            configuration["WASM_MODULE_PATH"],
            configuration["Wasm:DefaultModulePath"],
            "/app/modules/weather.wasm",
            Path.GetFullPath(Path.Combine(
                Directory.GetCurrentDirectory(),
                "..",
                "..",
                "internalUsages",
                "cs",
                "modules",
                "csharp-weather.wasm"))
        };

        var modulePath = moduleCandidates
            .FirstOrDefault(static path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            ?? throw new InvalidOperationException(
                "Manifest file was not found and no default weather module could be resolved. " +
                "Set SEKIBAN_MANIFEST_PATH or WASM_MODULE_PATH.");

        return new SekibanRuntimeManifest
        {
            DefaultModulePath = modulePath,
            EventTypes =
            [
                "WeatherForecastCreated",
                "WeatherForecastLocationUpdated",
                "WeatherForecastDeleted"
            ],
            Projectors =
            [
                new SekibanRuntimeProjector
                {
                    ProjectorName = "WeatherForecastProjector",
                    ModulePath = modulePath,
                    ProjectorVersion = "v1"
                },
                new SekibanRuntimeProjector
                {
                    ProjectorName = "WeatherForecastMultiProjection",
                    ModulePath = modulePath,
                    ProjectorVersion = "1.0.0"
                }
            ],
            QueryProjectors = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["GetWeatherForecastCountQuery"] = "WeatherForecastMultiProjection",
                ["GetWeatherForecastListQuery"] = "WeatherForecastMultiProjection",
                ["WeatherForecastListQuery"] = "WeatherForecastMultiProjection"
            }
        };
    }

    private static string ResolvePath(string path, string baseDirectory)
    {
        if (Path.IsPathRooted(path))
        {
            return path;
        }

        return Path.GetFullPath(Path.Combine(baseDirectory, path));
    }
}

public sealed class SekibanRuntimeMaterializedView
{
    /// <summary>
    ///     WASM module that exposes this materialized view. When omitted, the host falls back to
    ///     the manifest's <see cref="SekibanRuntimeManifest.DefaultModulePath"/>.
    /// </summary>
    public string? ModulePath { get; init; }

    public string ViewName { get; init; } = string.Empty;
    public int ViewVersion { get; init; } = 1;

    /// <summary>
    ///     Logical table names the projector declares. These are mirrored in the WASM module via
    ///     <c>IWasmMvProjector.LogicalTables</c> and are used by the host-side registry to compute
    ///     physical table names.
    /// </summary>
    public List<string> LogicalTables { get; init; } = [];

    public SekibanRuntimeMaterializedView ResolvePath(string baseDirectory) =>
        new()
        {
            ModulePath = ModulePath is null
                ? null
                : Path.IsPathRooted(ModulePath)
                    ? ModulePath
                    : Path.GetFullPath(Path.Combine(baseDirectory, ModulePath)),
            ViewName = ViewName,
            ViewVersion = ViewVersion,
            LogicalTables = [.. LogicalTables]
        };
}

public sealed class SekibanRuntimeProjector
{
    public string ProjectorName { get; init; } = string.Empty;
    public string ModulePath { get; init; } = string.Empty;
    public string AbiKind { get; init; } = "wasi-preview1";
    public string ModuleVersion { get; init; } = "1.0.0";
    public string ProjectorVersion { get; init; } = "1.0.0";
    /// <summary>
    /// Optional explicit tag payload name (e.g. "RoomState").
    /// When null, the host infers it from ProjectorName via heuristic.
    /// </summary>
    public string? TagPayloadName { get; init; }

    public SekibanRuntimeProjector ResolvePath(string baseDirectory) =>
        new()
        {
            ProjectorName = ProjectorName,
            ModulePath = Path.IsPathRooted(ModulePath)
                ? ModulePath
                : Path.GetFullPath(Path.Combine(baseDirectory, ModulePath)),
            AbiKind = AbiKind,
            ModuleVersion = ModuleVersion,
            ProjectorVersion = ProjectorVersion
        };
}
