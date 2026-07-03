using System.Text.Json;

namespace SekibanDcbDecider.Wasm.MaterializedView;

/// <summary>
/// Registers the materialized view projectors available in this WASM module and routes
/// `mv_metadata` / `mv_initialize` / `mv_apply_event` export calls to the right projector.
/// </summary>
internal static class WasmMvRegistry
{
    private static readonly WeatherForecastMvV1 WeatherForecastV1 = new();

    private static readonly Dictionary<string, IWasmMvProjector> Projectors =
        new(StringComparer.Ordinal)
        {
            [KeyFor(WeatherForecastV1)] = WeatherForecastV1
        };

    public static string Metadata()
    {
        var list = Projectors.Values
            .Select(projector => new WasmMvMetadata
            {
                ViewName = projector.ViewName,
                ViewVersion = projector.ViewVersion,
                LogicalTables = projector.LogicalTables.ToList()
            })
            .ToList();
        return JsonSerializer.Serialize(list, WasmJsonContext.Default.ListWasmMvMetadata);
    }

    public static string Initialize(string viewName, int viewVersion, string tableBindingsJson)
    {
        var projector = Resolve(viewName, viewVersion);
        var bindings = JsonSerializer.Deserialize(tableBindingsJson, WasmJsonContext.Default.MvTableBindingsDto)
            ?? new MvTableBindingsDto();
        var statements = projector.Initialize(bindings).ToList();
        return JsonSerializer.Serialize(
            new MvStatementBatchDto { Statements = statements },
            WasmJsonContext.Default.MvStatementBatchDto);
    }

    public static string ApplyEvent(
        string viewName,
        int viewVersion,
        string tableBindingsJson,
        string serializableEventJson,
        IWasmMvQueryPort queryPort)
    {
        var projector = Resolve(viewName, viewVersion);
        var bindings = JsonSerializer.Deserialize(tableBindingsJson, WasmJsonContext.Default.MvTableBindingsDto)
            ?? new MvTableBindingsDto();
        var ev = JsonSerializer.Deserialize(serializableEventJson, WasmJsonContext.Default.MvSerializableEventDto)
            ?? throw new InvalidOperationException("Materialized view apply received an empty event payload.");

        var statements = projector.ApplyEvent(bindings, ev, queryPort).ToList();
        return JsonSerializer.Serialize(
            new MvStatementBatchDto { Statements = statements },
            WasmJsonContext.Default.MvStatementBatchDto);
    }

    private static IWasmMvProjector Resolve(string viewName, int viewVersion)
    {
        if (Projectors.TryGetValue(KeyFor(viewName, viewVersion), out var projector))
        {
            return projector;
        }

        throw new InvalidOperationException(
            $"No WASM materialized view projector registered for '{viewName}/{viewVersion}'.");
    }

    private static string KeyFor(IWasmMvProjector projector) => KeyFor(projector.ViewName, projector.ViewVersion);
    private static string KeyFor(string viewName, int viewVersion) => $"{viewName}/{viewVersion}";
}
