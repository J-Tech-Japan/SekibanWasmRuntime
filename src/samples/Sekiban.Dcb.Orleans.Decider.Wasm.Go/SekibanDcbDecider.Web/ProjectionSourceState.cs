namespace SekibanDcbDecider.Web;

/// <summary>
///     Tracks the user's current projection-source choice across the Students / Classrooms /
///     Enrollments pages. Scoped per Blazor circuit: a single session sees the same setting
///     everywhere, but different users (or browser tabs refreshing state) get their own copy.
/// </summary>
public enum ProjectionSource
{
    /// <summary>Use the existing in-memory / multi-projection read path via wasmserver.</summary>
    Memory = 0,

    /// <summary>Use the materialized view read path — Go ClientApi <c>/api/mv/*</c>, backed
    /// directly by <c>DcbMaterializedViewPostgres</c>.</summary>
    MaterializedView = 1,
}

public class ProjectionSourceState
{
    public ProjectionSource Current { get; private set; } = ProjectionSource.Memory;

    public event Action? Changed;

    public void Set(ProjectionSource source)
    {
        if (Current == source) return;
        Current = source;
        Changed?.Invoke();
    }

    public string Label => Current switch
    {
        ProjectionSource.Memory => "Memory projection",
        ProjectionSource.MaterializedView => "Materialized view",
        _ => "Unknown",
    };
}
