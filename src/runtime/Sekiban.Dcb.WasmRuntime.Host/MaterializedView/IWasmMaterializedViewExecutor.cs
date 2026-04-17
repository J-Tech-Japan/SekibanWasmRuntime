using Sekiban.Dcb.MaterializedView;

namespace Sekiban.Dcb.WasmRuntime.Host.MaterializedView;

/// <summary>
///     Host-side facade over the WASM MV exports (`mv_metadata`, `mv_initialize`,
///     `mv_apply_event`) plus the `mv_host_query_rows` import. A single executor instance is
///     shared by all <see cref="WasmBackedMaterializedViewProjector"/> shims and is responsible
///     for:
///     <list type="bullet">
///       <item>Holding the Wasmtime instance pool dedicated to MV calls (separate from the
///         MultiProjection pool managed by <c>IPrimitiveProjectionHost</c>).</item>
///       <item>Registering the `mv_host_query_rows` linker import that dispatches mid-apply
///         queries back to the Postgres connection held in the current
///         <see cref="IMvApplyContext"/> (captured via AsyncLocal).</item>
///       <item>Allocating/freeing WASM linear memory buffers to marshal strings and JSON
///         payloads across the boundary (`alloc`/`dealloc` exports).</item>
///     </list>
/// </summary>
public interface IWasmMaterializedViewExecutor
{
    /// <summary>
    ///     Enumerate all materialized views registered in the WASM module. Typically called once
    ///     at host startup to register one <see cref="WasmBackedMaterializedViewProjector"/> per
    ///     MV in DI.
    /// </summary>
    Task<IReadOnlyList<WasmMvMetadataDto>> GetMetadataAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Invoke the WASM module's <c>mv_initialize(viewName, viewVersion, tableBindings)</c>
    ///     export and return the DDL statements the host should execute in the init transaction.
    /// </summary>
    Task<IReadOnlyList<WasmMvSqlStatementDto>> InitializeAsync(
        string viewName,
        int viewVersion,
        WasmMvTableBindingsDto tableBindings,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Invoke <c>mv_apply_event</c>. The WASM module may call back into the host via
    ///     <c>mv_host_query_rows</c> during execution; the executor captures
    ///     <paramref name="queryPort"/> in an AsyncLocal before invoking the WASM export so the
    ///     host import can route query callbacks back through Sekiban's <see cref="IMvApplyQueryPort"/>.
    /// </summary>
    Task<IReadOnlyList<WasmMvSqlStatementDto>> ApplyEventAsync(
        string viewName,
        int viewVersion,
        WasmMvTableBindingsDto tableBindings,
        WasmMvSerializableEventDto serializableEvent,
        IMvApplyQueryPort queryPort,
        CancellationToken cancellationToken = default);
}
