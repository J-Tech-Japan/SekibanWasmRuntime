using System.ComponentModel.DataAnnotations;

// Lightweight C# DTOs that match the JSON shapes the Swift ClientApi accepts for
// ClassRoom / Student / Enrollment / Weather write paths. The Blazor pages imported from
// the Sekiban.Dcb.Orleans.Decider template reference domain records like
// `CreateClassRoom` by type name; rather than pulling in the full Sekiban Dcb.EventSource
// project with all its ICommand / ICommandWithHandler plumbing, we provide matching
// record shapes in the namespaces the razor pages import. Only the JSON-serialized
// fields matter — the Swift ClientApi does the state reads + tag fan-out server-side.

namespace Dcb.EventSource.ClassRoom
{
    public record CreateClassRoom(Guid ClassRoomId, string Name, int MaxStudents = 10);
}

namespace Dcb.EventSource.Student
{
    public record CreateStudent(Guid StudentId, string Name, int MaxClassCount = 5);
}

namespace Dcb.EventSource.Enrollment
{
    public record EnrollStudentInClassRoom(Guid StudentId, Guid ClassRoomId);
    public record DropStudentFromClassRoom(Guid StudentId, Guid ClassRoomId);
}

namespace Dcb.EventSource.Weather
{
    public record CreateWeatherForecast
    {
        public Guid ForecastId { get; init; }

        [Required]
        [StringLength(100, MinimumLength = 1)]
        public string Location { get; init; } = string.Empty;

        [Required]
        public DateOnly Date { get; init; }

        [Range(-273, 60)]
        public int TemperatureC { get; init; }

        [Required]
        [StringLength(200)]
        public string Summary { get; init; } = string.Empty;
    }

    public record DeleteWeatherForecast
    {
        public Guid ForecastId { get; init; }
    }

    public record UpdateWeatherForecastLocation
    {
        public Guid ForecastId { get; init; }
        public string NewLocation { get; init; } = string.Empty;
    }

    // Alias the template Weather.razor uses (payload shape differs slightly — uses
    // `NewLocationName` instead of `NewLocation`). Both land at Swift ClientApi's
    // /api/updateweatherforecastlocation endpoint which accepts `newLocation` only, so the
    // ApiClient converts when it posts.
    public record ChangeLocationName
    {
        public Guid ForecastId { get; init; }
        public string NewLocationName { get; init; } = string.Empty;
    }
}

namespace Dcb.EventSource.Projections
{
    // Placeholder namespace so Weather.razor's `@using Dcb.EventSource.Projections`
    // compiles. The template's projection DTOs aren't needed — the razor pages decode
    // the JSON array inline.
}
