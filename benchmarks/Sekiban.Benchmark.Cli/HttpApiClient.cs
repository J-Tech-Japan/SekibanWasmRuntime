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
    private string _weatherPostPath = "/api/weatherforecast";

    public HttpApiClient(string baseUrl)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromMinutes(5) };
        _http.DefaultRequestHeaders.Add("X-Debug-Display-Name", "BenchmarkUser");
        _http.DefaultRequestHeaders.Add("X-Debug-User-Id", Guid.NewGuid().ToString());
    }

    public void Dispose() => _http.Dispose();

    /// <summary>
    /// Authenticate with the Native template's Identity+JWT auth system.
    /// Registers a user (if needed) then logs in to get a JWT token.
    /// </summary>
    public async Task<bool> AuthenticateAsync()
    {
        var email = $"benchmark-{Guid.NewGuid():N}@test.local";
        var password = "Bench!Mark123$";

        // Register
        var regPayload = new { email, password, displayName = "BenchmarkAdmin" };
        var (regResp, _) = await PostMeasured("/auth/register", regPayload);

        // Login with JWT
        var loginPayload = new { email, password, useCookies = false };
        var (loginResp, _) = await PostMeasured("/auth/login", loginPayload);
        if (!loginResp.IsSuccessStatusCode) return false;

        var loginJson = await loginResp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(loginJson);
        if (!doc.RootElement.TryGetProperty("accessToken", out var tokenProp)) return false;
        var token = tokenProp.GetString();
        if (string.IsNullOrEmpty(token)) return false;

        _http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // Promote to Admin role: Native template needs Admin role for rooms/reservations.
        // The template registers with "User" role. We need Admin.
        // Check if there's a way... Actually, for benchmark purposes, we need the test-data
        // and room/reservation endpoints which require authorization.
        // Let's check if basic auth is enough (RequireAuthorization without AdminOnly)
        Console.WriteLine($"  Authenticated as: {email}");
        return true;
    }

    // ---------- Commands ----------

    public Task<(HttpResponseMessage Resp, double Ms)> CreateRoom(object payload) =>
        PostMeasured("/api/rooms", payload);

    public Task<(HttpResponseMessage Resp, double Ms)> CreateWeatherForecast(object payload) =>
        PostMeasured(_weatherPostPath, payload);

    /// <summary>
    /// Auto-detect the correct weather POST path.
    /// Native template uses /api/inputweatherforecast, WASM samples use /api/weatherforecast.
    /// </summary>
    public async Task DetectWeatherEndpointAsync()
    {
        // Try WASM-style first
        var testPayload = new { forecastId = Guid.NewGuid(), location = "probe", date = DateTime.UtcNow.ToString("yyyy-MM-dd"), temperatureC = 0, summary = "probe" };
        var (resp, _) = await PostMeasured("/api/weatherforecast", testPayload);
        if (resp.IsSuccessStatusCode)
        {
            _weatherPostPath = "/api/weatherforecast";
            return;
        }
        // Try Native-style
        var (resp2, _) = await PostMeasured("/api/inputweatherforecast", testPayload);
        if (resp2.IsSuccessStatusCode)
        {
            _weatherPostPath = "/api/inputweatherforecast";
            return;
        }
        // Fallback
        _weatherPostPath = "/api/weatherforecast";
    }

    public Task<(HttpResponseMessage Resp, double Ms)> CreateReservationDraft(object payload) =>
        PostMeasured("/api/reservations/draft", payload);

    public Task<(HttpResponseMessage Resp, double Ms)> CommitHold(Guid reservationId, object payload) =>
        PostMeasured($"/api/reservations/{reservationId}/hold", payload);

    public Task<(HttpResponseMessage Resp, double Ms)> ConfirmReservation(Guid reservationId, object payload) =>
        PostMeasured($"/api/reservations/{reservationId}/confirm", payload);

    public Task<(HttpResponseMessage Resp, double Ms)> CancelReservation(Guid reservationId, object payload) =>
        PostMeasured($"/api/reservations/{reservationId}/cancel", payload);

    public Task<(HttpResponseMessage Resp, double Ms)> QuickReservation(object payload) =>
        PostMeasuredWithUniqueUser("/api/reservations/quick", payload);

    public Task<(HttpResponseMessage Resp, double Ms)> CreateReservationDraftUniqueUser(object payload) =>
        PostMeasuredWithUniqueUser("/api/reservations/draft", payload);

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

    /// <summary>
    /// POST with a unique X-Debug-User-Id per request to avoid UserMonthlyReservation tag contention.
    /// Each request appears as a different user.
    /// </summary>
    private async Task<(HttpResponseMessage Resp, double Ms)> PostMeasuredWithUniqueUser(string path, object payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOpts);
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        // Copy Bearer token if set (for Native template auth)
        if (_http.DefaultRequestHeaders.Authorization != null)
        {
            request.Headers.Authorization = _http.DefaultRequestHeaders.Authorization;
        }
        request.Headers.Add("X-Debug-User-Id", Guid.NewGuid().ToString());
        request.Headers.Add("X-Debug-Display-Name", "BenchUser");
        var sw = Stopwatch.StartNew();
        var resp = await _http.SendAsync(request);
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
