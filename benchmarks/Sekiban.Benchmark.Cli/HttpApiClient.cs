using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sekiban.Benchmark.Cli;

public sealed class HttpApiClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public HttpApiClient(string baseUrl)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromMinutes(5) };
        _http.DefaultRequestHeaders.Add("X-Debug-Display-Name", "BenchmarkUser");
        _http.DefaultRequestHeaders.Add("X-Debug-User-Id", Guid.NewGuid().ToString());
    }

    public void Dispose() => _http.Dispose();

    // ---------- Commands ----------

    public Task<(HttpResponseMessage Resp, double Ms)> CreateRoom(object payload) =>
        PostMeasured("/api/rooms", payload);

    public Task<(HttpResponseMessage Resp, double Ms)> CreateWeatherForecast(object payload) =>
        PostMeasured("/api/weatherforecast", payload);

    public Task<(HttpResponseMessage Resp, double Ms)> CreateReservationDraft(object payload) =>
        PostMeasured("/api/reservations/draft", payload);

    public Task<(HttpResponseMessage Resp, double Ms)> CommitHold(Guid reservationId, object payload) =>
        PostMeasured($"/api/reservations/{reservationId}/hold", payload);

    public Task<(HttpResponseMessage Resp, double Ms)> ConfirmReservation(Guid reservationId, object payload) =>
        PostMeasured($"/api/reservations/{reservationId}/confirm", payload);

    public Task<(HttpResponseMessage Resp, double Ms)> CancelReservation(Guid reservationId, object payload) =>
        PostMeasured($"/api/reservations/{reservationId}/cancel", payload);

    public Task<(HttpResponseMessage Resp, double Ms)> QuickReservation(object payload) =>
        PostMeasured("/api/reservations/quick", payload);

    // ---------- Queries ----------

    public Task<(HttpResponseMessage Resp, double Ms)> GetRooms(int pageSize = 100) =>
        GetMeasured($"/api/rooms?pageSize={pageSize}");

    public Task<(HttpResponseMessage Resp, double Ms)> GetReservations(int pageNumber = 1, int pageSize = 100) =>
        GetMeasured($"/api/reservations?pageNumber={pageNumber}&pageSize={pageSize}");

    public Task<(HttpResponseMessage Resp, double Ms)> GetReservationsByRoom(Guid roomId, int pageSize = 100) =>
        GetMeasured($"/api/reservations/by-room/{roomId}?pageSize={pageSize}");

    public Task<(HttpResponseMessage Resp, double Ms)> GetWeatherCount() =>
        GetMeasured("/api/weatherforecast/count");

    public Task<(HttpResponseMessage Resp, double Ms)> GetWeatherList(int pageSize = 100) =>
        GetMeasured($"/api/weatherforecast?pageSize={pageSize}");

    // ---------- Helpers ----------

    private async Task<(HttpResponseMessage Resp, double Ms)> PostMeasured(string path, object payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOpts);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var sw = Stopwatch.StartNew();
        var resp = await _http.PostAsync(path, content);
        sw.Stop();
        return (resp, sw.Elapsed.TotalMilliseconds);
    }

    private async Task<(HttpResponseMessage Resp, double Ms)> GetMeasured(string path)
    {
        var sw = Stopwatch.StartNew();
        var resp = await _http.GetAsync(path);
        sw.Stop();
        return (resp, sw.Elapsed.TotalMilliseconds);
    }

    public async Task<T?> ReadJson<T>(HttpResponseMessage resp) =>
        await resp.Content.ReadFromJsonAsync<T>(JsonOpts);
}

// Response models
public record CommandResponse(
    bool Success,
    string? EventId = null,
    string? SortableUniqueId = null,
    Guid? RoomId = null,
    Guid? ReservationId = null,
    string? OrganizerName = null,
    bool? RequiresApproval = null,
    Guid? ApprovalRequestId = null);
