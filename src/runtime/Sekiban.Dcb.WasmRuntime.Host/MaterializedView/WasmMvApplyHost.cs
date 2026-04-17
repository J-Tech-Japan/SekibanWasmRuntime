using Sekiban.Dcb.Events;
using Sekiban.Dcb.MaterializedView;

namespace Sekiban.Dcb.WasmRuntime.Host.MaterializedView;

/// <summary>
///     Direct Sekiban <see cref="IMvApplyHost"/> implementation that routes every call to the
///     Wasmtime-hosted WASM module through <see cref="IWasmMaterializedViewExecutor"/>.
///     Replaces the earlier shim projector (<c>WasmBackedMaterializedViewProjector</c> +
///     <c>MvParamDapperBridge</c>) with a one-pass translation between Sekiban's 10.2.0 types
///     and our stable WASM-side wire DTOs, so the WASM side never has to mirror CLR parameter
///     marshaling semantics.
///
///     <para>Mapping:</para>
///     <list type="bullet">
///       <item><c>InitializeAsync</c> enumerates the view's logical tables (from manifest),
///         drives <see cref="IMvTableBindings.RegisterTable"/> so the registry computes physical
///         names, and sends the resolved bindings into the WASM module's <c>mv_initialize</c>
///         export. Returned SQL (DDL) statements are handed back to the executor to run inside
///         the same init transaction.</item>
///       <item><c>ApplyEventAsync</c> serializes the Sekiban <see cref="SerializableEvent"/> +
///         bindings + sortable id, invokes <c>mv_apply_event</c>, and returns the produced
///         <see cref="MvSqlStatementDto"/> batch unchanged. Mid-apply query callbacks arrive
///         through the host import wired inside the executor and route back to the supplied
///         <see cref="IMvApplyQueryPort"/>.</item>
///     </list>
/// </summary>
public sealed class WasmMvApplyHost : IMvApplyHost
{
    private readonly IWasmMaterializedViewExecutor _executor;

    public WasmMvApplyHost(
        string viewName,
        int viewVersion,
        IReadOnlyList<string> logicalTables,
        IWasmMaterializedViewExecutor executor)
    {
        ViewName = viewName;
        ViewVersion = viewVersion;
        LogicalTables = logicalTables;
        _executor = executor;
    }

    public string ViewName { get; }
    public int ViewVersion { get; }
    public IReadOnlyList<string> LogicalTables { get; }

    public async Task<IReadOnlyList<MvSqlStatementDto>> InitializeAsync(
        IMvTableBindings tables,
        CancellationToken ct)
    {
        var bindingsDto = new WasmMvTableBindingsDto();
        foreach (var logical in LogicalTables)
        {
            var table = tables.RegisterTable(logical);
            bindingsDto.Bindings.Add(new WasmMvTableBindingEntry
            {
                Logical = table.LogicalName,
                Physical = table.PhysicalName
            });
        }

        var statements = await _executor.InitializeAsync(ViewName, ViewVersion, bindingsDto, ct)
            .ConfigureAwait(false);
        return ToSekibanStatements(statements);
    }

    public async Task<IReadOnlyList<MvSqlStatementDto>> ApplyEventAsync(
        SerializableEvent ev,
        IMvTableBindings tables,
        IMvApplyQueryPort queryPort,
        string sortableUniqueId,
        CancellationToken ct)
    {
        var bindingsDto = new WasmMvTableBindingsDto
        {
            Bindings = LogicalTables
                .Select(logical => new WasmMvTableBindingEntry
                {
                    Logical = logical,
                    Physical = tables.GetPhysicalName(logical)
                })
                .ToList()
        };

        var serializable = new WasmMvSerializableEventDto
        {
            EventType = ev.EventPayloadName,
            // SerializableEvent.Payload is already the event payload bytes as UTF-8 JSON.
            PayloadJson = System.Text.Encoding.UTF8.GetString(ev.Payload),
            SortableUniqueId = sortableUniqueId,
            Tags = ev.Tags?.ToList() ?? []
        };

        var statements = await _executor.ApplyEventAsync(
                ViewName,
                ViewVersion,
                bindingsDto,
                serializable,
                queryPort,
                ct)
            .ConfigureAwait(false);
        return ToSekibanStatements(statements);
    }

    // WASM wire types (WasmMvParam / WasmMvSqlStatementDto) are deliberately kept byte-for-byte
    // aligned with Sekiban's MvParam / MvSqlStatementDto so this translation is a pure relabel
    // — no value conversion, no reflection. If Sekiban's types ever diverge we pay the cost of
    // the remap here once, not inside every projector.
    private static IReadOnlyList<MvSqlStatementDto> ToSekibanStatements(
        IReadOnlyList<WasmMvSqlStatementDto> statements) =>
        statements
            .Select(s => new MvSqlStatementDto(
                s.Sql,
                s.Parameters
                    .Select(p => new MvParam(p.Name, (MvParamKind)(int)p.Kind, p.ValueJson))
                    .ToList()))
            .ToList();
}
