using Dapper;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MaterializedView;
using Sekiban.Dcb.WasmRuntime.Host;

namespace Sekiban.Dcb.WasmRuntime.Host.MaterializedView;

/// <summary>
///     CLR-side shim implementing <see cref="IMaterializedViewProjector"/> by delegating every
///     method to the WASM module through <see cref="IWasmMaterializedViewExecutor"/>.
///
///     Sekiban's <c>MaterializedViewGrain</c> + <c>PostgresMvExecutor</c> call this projector
///     exactly like a native CLR-authored one. The shim:
///     <list type="number">
///       <item>In <see cref="InitializeAsync"/>: registers logical tables via
///         <see cref="IMvInitContext.RegisterTable"/>, calls <c>mv_initialize</c> to obtain DDL
///         SQL from the WASM module, and executes each statement via <c>ctx.ExecuteAsync</c>.</item>
///       <item>In <see cref="ApplyToViewAsync"/>: extracts the raw event JSON from
///         <see cref="DynamicJsonEventPayload"/> (the Host runs in WASM-mode and therefore uses
///         <c>DynamicJsonEventTypes</c> which preserves raw payload JSON), then calls
///         <c>mv_apply_event</c>. Mid-apply DB queries from the WASM module land on
///         <see cref="IMvApplyContext"/> via the <c>mv_host_query_rows</c> host import.</item>
///     </list>
/// </summary>
public sealed class WasmBackedMaterializedViewProjector : IMaterializedViewProjector
{
    private readonly IWasmMaterializedViewExecutor _executor;
    private readonly IReadOnlyList<string> _logicalTables;

    // Cached bindings populated during InitializeAsync. Reused for every ApplyEvent call so we
    // don't rely on IMvApplyContext.GetDependencyViewTable (which Sekiban treats as a cross-view
    // request and throws NotSupportedException for in the PoC).
    private WasmMvTableBindingsDto? _tableBindings;

    public WasmBackedMaterializedViewProjector(
        string viewName,
        int viewVersion,
        IReadOnlyList<string> logicalTables,
        IWasmMaterializedViewExecutor executor)
    {
        ViewName = viewName;
        ViewVersion = viewVersion;
        _logicalTables = logicalTables;
        _executor = executor;
    }

    public string ViewName { get; }
    public int ViewVersion { get; }

    public async Task InitializeAsync(IMvInitContext ctx, CancellationToken cancellationToken = default)
    {
        var bindings = new WasmMvTableBindingsDto();
        foreach (var logicalTable in _logicalTables)
        {
            var table = ctx.RegisterTable(logicalTable);
            bindings.Bindings.Add(new WasmMvTableBindingEntry
            {
                Logical = table.LogicalName,
                Physical = table.PhysicalName
            });
        }
        _tableBindings = bindings;

        var statements = await _executor.InitializeAsync(ViewName, ViewVersion, bindings, cancellationToken)
            .ConfigureAwait(false);

        foreach (var statement in statements)
        {
            await ctx.ExecuteAsync(
                    statement.Sql,
                    MvParamDapperBridge.ToDapperParameters(statement.Parameters),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<MvSqlStatement>> ApplyToViewAsync(
        Event ev,
        IMvApplyContext ctx,
        CancellationToken cancellationToken = default)
    {
        var bindings = _tableBindings
            ?? throw new InvalidOperationException(
                $"Materialized view '{ViewName}/{ViewVersion}' was not initialized before apply.");

        var serializable = new WasmMvSerializableEventDto
        {
            EventType = ev.EventType,
            // In WASM host mode IEventTypes returns DynamicJsonEventPayload. Fall back to
            // JsonSerializer for CLR-typed payloads so the same shim works regardless of which
            // DcbDomainTypes flavor is registered (useful for tests).
            PayloadJson = ev.Payload is DynamicJsonEventPayload dyn
                ? dyn.ToJsonString()
                : System.Text.Json.JsonSerializer.Serialize(ev.Payload, ev.Payload.GetType()),
            SortableUniqueId = ctx.CurrentSortableUniqueId,
            Tags = ev.Tags?.ToList() ?? []
        };

        var statements = await _executor.ApplyEventAsync(
                ViewName,
                ViewVersion,
                bindings,
                serializable,
                ctx,
                cancellationToken)
            .ConfigureAwait(false);

        return statements
            .Select(s => new MvSqlStatement(s.Sql, MvParamDapperBridge.ToDapperParameters(s.Parameters)))
            .ToList();
    }
}

/// <summary>
///     Converts the WASM-originated <see cref="WasmMvParam"/> list into a Dapper
///     <see cref="DynamicParameters"/> bag that mirrors the shape anonymous-type parameters
///     produce on the native side, so the rest of Sekiban's MV runtime is none the wiser.
/// </summary>
internal static class MvParamDapperBridge
{
    public static DynamicParameters ToDapperParameters(IReadOnlyList<WasmMvParam> parameters)
    {
        var dapper = new DynamicParameters();
        foreach (var p in parameters)
        {
            dapper.Add(p.Name, ReadClrValue(p));
        }
        return dapper;
    }

    private static object? ReadClrValue(WasmMvParam param)
    {
        if (param.Kind == WasmMvParamKind.Null || param.ValueJson is null)
        {
            return null;
        }

        var token = System.Text.Json.JsonDocument.Parse(param.ValueJson).RootElement;
        return param.Kind switch
        {
            WasmMvParamKind.String => token.GetString(),
            WasmMvParamKind.Guid => Guid.Parse(token.GetString() ?? throw new FormatException("Guid param was null.")),
            WasmMvParamKind.Int32 => token.GetInt32(),
            WasmMvParamKind.Int64 => token.GetInt64(),
            WasmMvParamKind.Boolean => token.GetBoolean(),
            WasmMvParamKind.Decimal => token.GetDecimal(),
            WasmMvParamKind.Double => token.GetDouble(),
            WasmMvParamKind.DateTimeOffset => DateTimeOffset.Parse(
                token.GetString() ?? throw new FormatException("DateTimeOffset param was null."),
                System.Globalization.CultureInfo.InvariantCulture),
            WasmMvParamKind.Bytes => Convert.FromBase64String(
                token.GetString() ?? throw new FormatException("Bytes param was null.")),
            _ => throw new NotSupportedException($"Unsupported MvParamKind: {param.Kind}")
        };
    }
}
