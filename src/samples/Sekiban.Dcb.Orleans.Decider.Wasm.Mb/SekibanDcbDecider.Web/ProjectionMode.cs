namespace SekibanDcbDecider.Web;

public enum ProjectionMode
{
    MemoryProjection,
    MaterializedView
}

public static class ProjectionModeExtensions
{
    public static string ToQueryValue(this ProjectionMode mode) =>
        mode == ProjectionMode.MaterializedView ? "materializedView" : "memory";
}
