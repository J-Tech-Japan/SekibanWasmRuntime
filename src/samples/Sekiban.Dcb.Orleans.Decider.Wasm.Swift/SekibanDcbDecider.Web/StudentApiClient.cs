using Dcb.EventSource.Student;
using Dcb.ImmutableModels.States.Student;
using System.Text.Json;

namespace SekibanDcbDecider.Web;

public class StudentApiClient(HttpClient httpClient)
{
    private class StudentCreateResponse
    {
        public Guid studentId { get; set; }
        public Guid eventId { get; set; }
        public string? sortableUniqueId { get; set; }
        public string? message { get; set; }
    }
    
    private class ErrorResponse
    {
        public string? error { get; set; }
    }
    public async Task<StudentState[]> GetStudentsAsync(
        int? pageNumber = null,
        int? pageSize = null,
        string? waitForSortableUniqueId = null,
        CancellationToken cancellationToken = default)
    {
        var queryParams = new List<string>();

        if (!string.IsNullOrEmpty(waitForSortableUniqueId))
            queryParams.Add($"waitForSortableUniqueId={Uri.EscapeDataString(waitForSortableUniqueId)}");

        if (pageNumber.HasValue)
            queryParams.Add($"pageNumber={pageNumber.Value}");

        if (pageSize.HasValue)
            queryParams.Add($"pageSize={pageSize.Value}");

        var requestUri = queryParams.Count > 0
            ? $"/api/students?{string.Join("&", queryParams)}"
            : "/api/students";

        var students = await httpClient.GetFromJsonAsync<List<StudentState>>(requestUri, cancellationToken);

        return students?.ToArray() ?? [];
    }

    public async Task<StudentState?> GetStudentAsync(
        Guid studentId,
        CancellationToken cancellationToken = default)
    {
        // The Swift ClientApi's `/api/students/{id}` returns the matched projector item
        // directly (camelCase JSON) or 404 when it doesn't exist. No `payload` envelope.
        // Map the projector shape (`studentId`, `name`, `maxClassCount`,
        // `enrolledClassRoomIds`) into the template's `StudentState` record.
        var response = await httpClient.GetAsync(
            $"/api/students/{studentId}",
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }
        var dto = await response.Content.ReadFromJsonAsync<StudentListItemDto>(cancellationToken);
        if (dto is null) return null;
        return new StudentState(
            dto.StudentId,
            dto.Name,
            dto.MaxClassCount,
            dto.EnrolledClassRoomIds?.ToList() ?? []);
    }

    /// JSON shape emitted by the Swift `StudentListProjection.executeListQuery`.
    /// Lowercase UUID strings in classroom list — `Guid.Parse` handles both.
    private sealed record StudentListItemDto(
        Guid StudentId,
        string Name,
        int MaxClassCount,
        List<Guid>? EnrolledClassRoomIds);

    public async Task<CommandResponse> CreateStudentAsync(
        CreateStudent command,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await httpClient.PostAsJsonAsync("/api/students", command, cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<StudentCreateResponse>(cancellationToken);
                if (result != null)
                {
                    return new CommandResponse(
                        true, 
                        result.eventId, 
                        result.studentId, 
                        null, 
                        result.sortableUniqueId);
                }
            }
            else
            {
                var errorResult = await response.Content.ReadFromJsonAsync<ErrorResponse>(cancellationToken);
                return new CommandResponse(false, null, null, errorResult?.error ?? "Unknown error", null);
            }
        }
        catch (Exception ex)
        {
            return new CommandResponse(false, null, null, ex.Message, null);
        }
        
        return new CommandResponse(false, null, null, "Failed to create student", null);
    }
}
