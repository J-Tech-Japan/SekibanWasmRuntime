// Lightweight view DTOs consumed by the Blazor razor pages imported from the
// Sekiban.Dcb.Orleans.Decider template. The template originally sourced these from
// `Dcb.EventSource.*` / `Dcb.ImmutableModels.*` but those projects pull in Orleans + the
// full Sekiban Dcb stack, which the Swift sample doesn't need. Matching the namespaces +
// property names keeps the razor code unchanged.

namespace Dcb.EventSource.ClassRoom
{
    public record ClassRoomItem
    {
        public Guid ClassRoomId { get; init; }
        public string Name { get; init; } = string.Empty;
        public int MaxStudents { get; init; }
        public int EnrolledCount { get; init; }
        public bool IsFull { get; init; }
        public int RemainingCapacity { get; init; }
    }
}

namespace Dcb.ImmutableModels.States.Student
{
    public record StudentState(
        Guid StudentId,
        string Name,
        int MaxClassCount,
        List<Guid> EnrolledClassRoomIds)
    {
        public int GetRemaining() => MaxClassCount - EnrolledClassRoomIds.Count;
        public static StudentState Empty => new(Guid.Empty, string.Empty, 0, []);
    }
}

namespace Dcb.EventSource.Projections
{
    public record WeatherForecastItem(
        Guid ForecastId,
        string Location,
        DateTime Date,
        int TemperatureC,
        string? Summary,
        DateTime LastUpdated);
}
