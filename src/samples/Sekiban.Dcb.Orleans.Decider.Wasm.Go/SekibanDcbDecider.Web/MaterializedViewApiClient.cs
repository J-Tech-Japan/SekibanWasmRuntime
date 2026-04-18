using System.Net.Http.Json;

namespace SekibanDcbDecider.Web;

/// <summary>
///     Reads the `/api/mv/*` endpoints the Go ClientApi exposes against
///     `DcbMaterializedViewPostgres`. Pages use this client when the user flips the
///     "Data source" toggle to "Materialized view" — it bypasses the generic WASM runtime
///     host entirely and hits the language-owned Postgres-backed read model instead.
/// </summary>
public class MaterializedViewApiClient(HttpClient httpClient)
{
    public async Task<ClassRoomMvRow[]> GetClassroomsAsync(
        int? pageNumber = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default)
    {
        var rows = await httpClient.GetFromJsonAsync<ClassRoomMvRow[]>(
            Build("/api/mv/classrooms", pageNumber, pageSize),
            cancellationToken);
        return rows ?? [];
    }

    public async Task<StudentMvRow[]> GetStudentsAsync(
        int? pageNumber = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default)
    {
        var rows = await httpClient.GetFromJsonAsync<StudentMvRow[]>(
            Build("/api/mv/students", pageNumber, pageSize),
            cancellationToken);
        return rows ?? [];
    }

    public async Task<EnrollmentMvRow[]> GetEnrollmentsAsync(
        int? pageNumber = null,
        int? pageSize = null,
        string? studentId = null,
        string? classRoomId = null,
        CancellationToken cancellationToken = default)
    {
        var qp = new List<string>();
        if (pageNumber.HasValue) qp.Add($"page_number={pageNumber.Value}");
        if (pageSize.HasValue) qp.Add($"page_size={pageSize.Value}");
        if (!string.IsNullOrEmpty(studentId)) qp.Add($"student_id={Uri.EscapeDataString(studentId)}");
        if (!string.IsNullOrEmpty(classRoomId)) qp.Add($"class_room_id={Uri.EscapeDataString(classRoomId)}");
        var uri = qp.Count > 0 ? $"/api/mv/enrollments?{string.Join("&", qp)}" : "/api/mv/enrollments";
        var rows = await httpClient.GetFromJsonAsync<EnrollmentMvRow[]>(uri, cancellationToken);
        return rows ?? [];
    }

    private static string Build(string path, int? pageNumber, int? pageSize)
    {
        var qp = new List<string>();
        if (pageNumber.HasValue) qp.Add($"page_number={pageNumber.Value}");
        if (pageSize.HasValue) qp.Add($"page_size={pageSize.Value}");
        return qp.Count > 0 ? $"{path}?{string.Join("&", qp)}" : path;
    }
}

public record ClassRoomMvRow(
    string class_room_id,
    string name,
    int max_students,
    int enrolled_count,
    string last_sortable_unique_id,
    DateTimeOffset last_applied_at);

public record StudentMvRow(
    string student_id,
    string name,
    int max_class_count,
    int enrolled_count,
    string last_sortable_unique_id,
    DateTimeOffset last_applied_at);

public record EnrollmentMvRow(
    string student_id,
    string class_room_id,
    DateTimeOffset enrolled_at,
    string last_sortable_unique_id);
