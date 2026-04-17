using Sekiban.Dcb.MaterializedView;

namespace Sekiban.Dcb.WasmRuntime.Host.MaterializedView;

/// <summary>
///     <see cref="IMvApplyHostFactory"/> that enumerates manifest-declared materialized views
///     and hands each one a fresh <see cref="WasmMvApplyHost"/>. Registered into DI via
///     <c>services.Replace(...)</c> so it takes over from Sekiban's default
///     <c>NativeMvApplyHostFactory</c> (which looks for CLR <c>IMaterializedViewProjector</c>
///     instances). In WASM mode there are no CLR projectors — the module is the projector.
/// </summary>
public sealed class WasmMvApplyHostFactory : IMvApplyHostFactory
{
    private readonly IWasmMaterializedViewExecutor _executor;
    private readonly IReadOnlyDictionary<(string ViewName, int ViewVersion), IReadOnlyList<string>> _tables;
    private readonly IReadOnlyList<MvApplyHostRegistration> _registrations;

    public WasmMvApplyHostFactory(
        IWasmMaterializedViewExecutor executor,
        IReadOnlyList<WasmMvApplyHostRegistration> registrations)
    {
        _executor = executor;
        _tables = registrations.ToDictionary(
            r => (r.ViewName, r.ViewVersion),
            r => (IReadOnlyList<string>)r.LogicalTables);
        _registrations = registrations
            .Select(r => new MvApplyHostRegistration(r.ViewName, r.ViewVersion))
            .OrderBy(r => r.ViewName, StringComparer.Ordinal)
            .ThenBy(r => r.ViewVersion)
            .ToList();
    }

    public IReadOnlyList<MvApplyHostRegistration> GetRegistrations() => _registrations;

    public IMvApplyHost Create(string viewName, int viewVersion)
    {
        if (!_tables.TryGetValue((viewName, viewVersion), out var tables))
        {
            throw new InvalidOperationException(
                $"WASM materialized view host '{viewName}/{viewVersion}' is not declared in the manifest.");
        }
        return new WasmMvApplyHost(viewName, viewVersion, tables, _executor);
    }
}

/// <summary>
///     Local registration struct bundling the logical table list with the view identity.
///     Consumed at host startup from <c>SekibanRuntimeManifest.MaterializedViews</c>.
/// </summary>
public sealed record WasmMvApplyHostRegistration(
    string ViewName,
    int ViewVersion,
    IReadOnlyList<string> LogicalTables);
