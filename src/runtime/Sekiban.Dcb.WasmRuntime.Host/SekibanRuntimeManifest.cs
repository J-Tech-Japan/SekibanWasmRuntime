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
                ProjectorVersion: projector.ProjectorVersion));
        }

        foreach (var (queryType, projectorName) in QueryProjectors)
        {
            registry.MapQueryToProjector(queryType, projectorName);
        }

        return registry;
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
            Projectors = Projectors.Select(projector => projector.ResolvePath(baseDirectory)).ToList()
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

public sealed class SekibanRuntimeProjector
{
    public string ProjectorName { get; init; } = string.Empty;
    public string ModulePath { get; init; } = string.Empty;
    public string AbiKind { get; init; } = "wasi-preview1";
    public string ModuleVersion { get; init; } = "1.0.0";
    public string ProjectorVersion { get; init; } = "1.0.0";

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
