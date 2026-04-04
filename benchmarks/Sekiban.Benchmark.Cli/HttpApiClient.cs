using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Sekiban.Benchmark.Cli;

public sealed class HttpApiClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private string _weatherPostPath = "/api/weatherforecast";
    private string? _authEmail;
    private string? _authPassword;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MaxValue;

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
    /// Token is automatically refreshed before expiry.
    /// </summary>
    public async Task<bool> AuthenticateAsync()
    {
        _authEmail = $"benchmark-{Guid.NewGuid():N}@test.local";
        _authPassword = "Bench!Mark123$";

        var regPayload = new { email = _authEmail, password = _authPassword, displayName = "BenchmarkAdmin" };
        await WaitForAuthEndpointAsync("/auth/register", regPayload);

        return await RefreshTokenAsync();
    }

    /// <summary>
    /// Re-login to get a fresh JWT token. Called automatically when the token is near expiry.
    /// </summary>
    private async Task<bool> RefreshTokenAsync()
    {
        if (_authEmail is null || _authPassword is null) return false;

        var loginPayload = new { email = _authEmail, password = _authPassword, useCookies = false };
        MeasuredResponse loginResp = await WaitForAuthEndpointAsync(
            "/auth/login",
            loginPayload,
            includeBodyOnSuccess: true);
        if (!loginResp.IsSuccessStatusCode || string.IsNullOrWhiteSpace(loginResp.Body)) return false;

        using var doc = JsonDocument.Parse(loginResp.Body);
        if (!doc.RootElement.TryGetProperty("accessToken", out var tokenProp)) return false;
        var token = tokenProp.GetString();
        if (string.IsNullOrEmpty(token)) return false;

        _http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // Parse token expiry from JWT payload (second base64 segment)
        _tokenExpiresAt = ParseJwtExpiry(token) ?? DateTimeOffset.UtcNow.AddMinutes(10);

        Console.WriteLine($"  Authenticated as: {_authEmail} (expires {_tokenExpiresAt:HH:mm:ss})");
        return true;
    }

    private async Task<MeasuredResponse> WaitForAuthEndpointAsync(
        string path,
        object payload,
        bool includeBodyOnSuccess = false)
    {
        Exception? lastException = null;
        MeasuredResponse? lastResponse = null;

        for (var attempt = 1; attempt <= 20; attempt++)
        {
            try
            {
                var response = await PostMeasured(path, payload, includeBodyOnSuccess);
                if (response.IsSuccessStatusCode || response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
                {
                    return response;
                }

                lastResponse = response;
            }
            catch (Exception ex)
            {
                lastException = ex;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        if (lastException is not null)
        {
            throw lastException;
        }

        return lastResponse ?? new MeasuredResponse(HttpStatusCode.ServiceUnavailable, 0);
    }

    /// <summary>
    /// Ensure the JWT token is still valid. Refreshes if within 60 seconds of expiry.
    /// </summary>
    public async Task EnsureAuthenticatedAsync()
    {
        if (_authEmail is null) return; // Not using auth
        if (DateTimeOffset.UtcNow.AddSeconds(60) < _tokenExpiresAt) return; // Still valid
        Console.WriteLine("  Token near expiry, refreshing...");
        await RefreshTokenAsync();
    }

    private static DateTimeOffset? ParseJwtExpiry(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2) return null;
            var payload = parts[1];
            // Pad base64
            payload = payload.Replace('-', '+').Replace('_', '/');
            switch (payload.Length % 4)
            {
                case 2: payload += "=="; break;
                case 3: payload += "="; break;
            }
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("exp", out var expProp))
            {
                return DateTimeOffset.FromUnixTimeSeconds(expProp.GetInt64());
            }
        }
        catch { /* ignore parse errors */ }
        return null;
    }

    // ---------- Commands ----------

    public Task<MeasuredResponse> CreateRoom(object payload) =>
        PostMeasured("/api/rooms", payload);

    public Task<MeasuredResponse> CreateWeatherForecast(object payload) =>
        PostMeasured(_weatherPostPath, payload);

    /// <summary>
    /// Auto-detect the correct weather POST path.
    /// Native template uses /api/inputweatherforecast, WASM samples use /api/weatherforecast.
    /// </summary>
    public async Task DetectWeatherEndpointAsync()
    {
        // Try WASM-style first
        var testPayload = new { forecastId = Guid.NewGuid(), location = "probe", date = DateTime.UtcNow.ToString("yyyy-MM-dd"), temperatureC = 0, summary = "probe" };
        var resp = await PostMeasured("/api/weatherforecast", testPayload);
        if (resp.IsSuccessStatusCode)
        {
            _weatherPostPath = "/api/weatherforecast";
            return;
        }
        // Try Native-style
        var resp2 = await PostMeasured("/api/inputweatherforecast", testPayload);
        if (resp2.IsSuccessStatusCode)
        {
            _weatherPostPath = "/api/inputweatherforecast";
            return;
        }
        // Fallback
        _weatherPostPath = "/api/weatherforecast";
    }

    public Task<MeasuredResponse> CreateReservationDraft(object payload) =>
        PostMeasured("/api/reservations/draft", payload);

    public Task<MeasuredResponse> CommitHold(Guid reservationId, object payload) =>
        PostMeasured($"/api/reservations/{reservationId}/hold", payload);

    public Task<MeasuredResponse> ConfirmReservation(Guid reservationId, object payload) =>
        PostMeasured($"/api/reservations/{reservationId}/confirm", payload);

    public Task<MeasuredResponse> CancelReservation(Guid reservationId, object payload) =>
        PostMeasured($"/api/reservations/{reservationId}/cancel", payload);

    public Task<MeasuredResponse> QuickReservation(object payload) =>
        PostMeasuredWithUniqueUser("/api/reservations/quick", payload);

    public Task<MeasuredResponse> CreateReservationDraftUniqueUser(object payload) =>
        PostMeasuredWithUniqueUser("/api/reservations/draft", payload);

    // ---------- Queries ----------

    public Task<MeasuredResponse> GetRooms(int pageSize = 100, bool includeBodyOnSuccess = false) =>
        GetMeasured($"/api/rooms?pageSize={pageSize}", includeBodyOnSuccess);

    public Task<MeasuredResponse> GetReservations(int pageNumber = 1, int pageSize = 100) =>
        GetMeasured($"/api/reservations?pageNumber={pageNumber}&pageSize={pageSize}");

    public Task<MeasuredResponse> GetReservationsByRoom(Guid roomId, int pageSize = 100) =>
        GetMeasured($"/api/reservations/by-room/{roomId}?pageSize={pageSize}");

    public Task<MeasuredResponse> GetWeatherCount() =>
        GetMeasured("/api/weatherforecast/count");

    public Task<MeasuredResponse> GetWeatherList(int pageSize = 100) =>
        GetMeasured($"/api/weatherforecast?pageSize={pageSize}");

    public async Task<List<Guid>> GetExistingRoomIdsAsync(int pageSize = 100)
    {
        var response = await GetRooms(pageSize, includeBodyOnSuccess: true);
        if (!response.IsSuccessStatusCode || string.IsNullOrWhiteSpace(response.Body))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(response.Body);
            var roomIds = new List<Guid>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.TryGetProperty("roomId", out var roomIdProp) &&
                    roomIdProp.TryGetGuid(out var roomId))
                {
                    roomIds.Add(roomId);
                }
            }
            return roomIds;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    // ---------- Helpers ----------

    private async Task<MeasuredResponse> PostMeasured(string path, object payload, bool includeBodyOnSuccess = false)
    {
        var json = JsonSerializer.Serialize(payload, JsonOpts);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var sw = Stopwatch.StartNew();
        using var resp = await _http.PostAsync(path, content);
        sw.Stop();
        var body = await ReadBodyIfNeededAsync(resp, includeBodyOnSuccess);
        return new MeasuredResponse(resp.StatusCode, sw.Elapsed.TotalMilliseconds, body);
    }

    /// <summary>
    /// POST with a unique X-Debug-User-Id per request to avoid UserMonthlyReservation tag contention.
    /// Each request appears as a different user.
    /// </summary>
    private async Task<MeasuredResponse> PostMeasuredWithUniqueUser(string path, object payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOpts);
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
            { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        // Copy Bearer token if set (for Native template auth)
        if (_http.DefaultRequestHeaders.Authorization != null)
        {
            request.Headers.Authorization = _http.DefaultRequestHeaders.Authorization;
        }
        request.Headers.Add("X-Debug-User-Id", Guid.NewGuid().ToString());
        request.Headers.Add("X-Debug-Display-Name", "BenchUser");
        var sw = Stopwatch.StartNew();
        using var resp = await _http.SendAsync(request);
        sw.Stop();
        var body = await ReadBodyIfNeededAsync(resp, includeBodyOnSuccess: false);
        return new MeasuredResponse(resp.StatusCode, sw.Elapsed.TotalMilliseconds, body);
    }

    private async Task<MeasuredResponse> GetMeasured(string path, bool includeBodyOnSuccess = false)
    {
        var sw = Stopwatch.StartNew();
        using var resp = await _http.GetAsync(path);
        sw.Stop();
        var body = await ReadBodyIfNeededAsync(resp, includeBodyOnSuccess);
        return new MeasuredResponse(resp.StatusCode, sw.Elapsed.TotalMilliseconds, body);
    }

    private static async Task<string?> ReadBodyIfNeededAsync(HttpResponseMessage response, bool includeBodyOnSuccess)
    {
        if (!response.IsSuccessStatusCode || includeBodyOnSuccess)
        {
            return await response.Content.ReadAsStringAsync();
        }

        return null;
    }
}

public sealed record MeasuredResponse(HttpStatusCode StatusCode, double Ms, string? Body = null)
{
    public bool IsSuccessStatusCode => (int)StatusCode is >= 200 and < 300;
};

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
