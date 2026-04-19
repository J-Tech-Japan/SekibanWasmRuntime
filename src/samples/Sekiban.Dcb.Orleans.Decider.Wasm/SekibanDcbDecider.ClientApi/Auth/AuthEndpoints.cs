using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace SekibanDcbDecider.ClientApi.Auth;

// Minimal self-contained /auth/* surface so the Blazor (cookie) and Next.js (token) frontends
// in this sample can log in without pulling in an Orleans Identity ApiService. Matches the
// Swift sample's approach: HMAC-SHA256 JWT + PBKDF2 password hashing, users seeded in-memory at
// startup. Benchmarks still rely on the X-Debug-* header auth handler — this file does not
// touch that path.

public static class AuthEndpoints
{
    public const string SessionCookieName = "sekiban_session";
    public const long DefaultSessionTtlSeconds = 60L * 60 * 24 * 7; // 7 days
    public const long RefreshMaxAgeSeconds = DefaultSessionTtlSeconds * 2;

    public static IServiceCollection AddSampleAuth(this IServiceCollection services, IConfiguration configuration)
    {
        var signingKey = configuration["SEKIBAN_AUTH_SIGNING_KEY"]
            ?? "sekiban-cs-wasm-dev-secret-change-me-in-production";
        services.AddSingleton(new JwtCodec(signingKey));
        services.AddSingleton<AuthUserStore>();
        return services;
    }

    public static void MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/auth").WithTags("Authentication");

        group.MapPost("/login", (Delegate)LoginAsync).AllowAnonymous();
        group.MapPost("/register", (Delegate)RegisterAsync).AllowAnonymous();
        group.MapPost("/logout", (Delegate)LogoutAsync).AllowAnonymous();
        group.MapPost("/refresh", (Delegate)RefreshAsync).AllowAnonymous();
        group.MapGet("/me", (Delegate)GetMeAsync).AllowAnonymous();
        group.MapGet("/status", (Delegate)GetStatusAsync).AllowAnonymous();
    }

    // ------------------------------------------------------------------
    // Route handlers
    // ------------------------------------------------------------------

    private static async Task<IResult> LoginAsync(
        [FromBody] LoginRequest body,
        [FromServices] AuthUserStore store,
        [FromServices] JwtCodec codec,
        HttpContext http)
    {
        if (string.IsNullOrWhiteSpace(body.Email) || string.IsNullOrWhiteSpace(body.Password))
        {
            return Results.BadRequest(new { error = "email and password are required" });
        }

        var user = store.Authenticate(body.Email, body.Password);
        if (user is null)
        {
            return Results.Json(new { error = "invalid email or password" }, statusCode: StatusCodes.Status401Unauthorized);
        }

        var useCookies = body.UseCookies ?? true;
        var (token, expiresAt) = codec.Issue(user);

        if (useCookies)
        {
            http.Response.Headers.Append("Set-Cookie",
                $"{SessionCookieName}={token}; Max-Age={DefaultSessionTtlSeconds}; Path=/; HttpOnly; SameSite=Lax");
            return Results.Ok(UserInfoResponse.From(user));
        }

        var isoExpires = DateTimeOffset.FromUnixTimeSeconds(expiresAt).UtcDateTime
            .ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        return Results.Ok(new TokenResponse(token, token, isoExpires, isoExpires));
    }

    private static async Task<IResult> RegisterAsync(
        [FromBody] RegisterRequest body,
        [FromServices] AuthUserStore store,
        [FromServices] JwtCodec codec,
        HttpContext http)
    {
        if (string.IsNullOrWhiteSpace(body.Email) || string.IsNullOrWhiteSpace(body.Password))
        {
            return Results.BadRequest(new { error = "email and password are required" });
        }

        try
        {
            var user = store.Register(body.Email, body.Password, body.DisplayName);
            var (token, _) = codec.Issue(user);
            http.Response.Headers.Append("Set-Cookie",
                $"{SessionCookieName}={token}; Max-Age={DefaultSessionTtlSeconds}; Path=/; HttpOnly; SameSite=Lax");
            return Results.Ok(UserInfoResponse.From(user));
        }
        catch (InvalidOperationException ex) when (ex.Message == "email already registered")
        {
            return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static Task<IResult> LogoutAsync(HttpContext http)
    {
        http.Response.Headers.Append("Set-Cookie",
            $"{SessionCookieName}=; Max-Age=0; Path=/; HttpOnly; SameSite=Lax");
        return Task.FromResult(Results.NoContent());
    }

    private static async Task<IResult> RefreshAsync(
        [FromBody] RefreshRequest body,
        [FromServices] AuthUserStore store,
        [FromServices] JwtCodec codec)
    {
        var token = !string.IsNullOrEmpty(body.RefreshToken) ? body.RefreshToken : body.AccessToken;
        if (string.IsNullOrWhiteSpace(token))
        {
            return Results.BadRequest(new { error = "token required" });
        }

        var claims = codec.VerifyAllowingExpired(token);
        if (claims is null)
        {
            return Results.Json(new { error = "invalid refresh token" }, statusCode: StatusCodes.Status401Unauthorized);
        }

        var ageSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - claims.Iat;
        if (ageSeconds > RefreshMaxAgeSeconds)
        {
            return Results.Json(new { error = "refresh token too old" }, statusCode: StatusCodes.Status401Unauthorized);
        }

        var user = store.FindById(claims.Sub) ?? new AuthUser(claims.Sub, claims.Email, claims.Name ?? claims.Email);
        var (newToken, expiresAt) = codec.Issue(user);
        var isoExpires = DateTimeOffset.FromUnixTimeSeconds(expiresAt).UtcDateTime
            .ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        return Results.Ok(new TokenResponse(newToken, newToken, isoExpires, isoExpires));
    }

    private static Task<IResult> GetMeAsync(
        HttpContext http,
        [FromServices] JwtCodec codec,
        [FromServices] AuthUserStore store)
    {
        var claims = ExtractClaims(http, codec);
        if (claims is null)
        {
            return Task.FromResult(Results.Json(new { error = "not authenticated" },
                statusCode: StatusCodes.Status401Unauthorized));
        }
        var user = store.FindById(claims.Sub) ?? new AuthUser(claims.Sub, claims.Email, claims.Name ?? claims.Email);
        return Task.FromResult(Results.Ok(UserInfoResponse.From(user)));
    }

    private static Task<IResult> GetStatusAsync(
        HttpContext http,
        [FromServices] JwtCodec codec,
        [FromServices] AuthUserStore store)
    {
        var claims = ExtractClaims(http, codec);
        if (claims is null)
        {
            return Task.FromResult(Results.Ok(new UserInfoResponse("", "", null, Array.Empty<string>(), false)));
        }
        var user = store.FindById(claims.Sub) ?? new AuthUser(claims.Sub, claims.Email, claims.Name ?? claims.Email);
        return Task.FromResult(Results.Ok(UserInfoResponse.From(user)));
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static AuthTokenClaims? ExtractClaims(HttpContext http, JwtCodec codec)
    {
        // Prefer Authorization: Bearer (used by the Next.js BFF), then fall back to the
        // `sekiban_session` cookie (used by the Blazor frontend).
        var auth = http.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrEmpty(auth) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = auth.Substring("Bearer ".Length).Trim();
            return codec.Verify(token);
        }
        if (http.Request.Cookies.TryGetValue(SessionCookieName, out var cookieToken))
        {
            return codec.Verify(cookieToken);
        }
        return null;
    }

    // ------------------------------------------------------------------
    // DTOs
    // ------------------------------------------------------------------

    public sealed record LoginRequest(
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("password")] string Password,
        [property: JsonPropertyName("useCookies")] bool? UseCookies);

    public sealed record RegisterRequest(
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("password")] string Password,
        [property: JsonPropertyName("displayName")] string? DisplayName);

    public sealed record RefreshRequest(
        [property: JsonPropertyName("accessToken")] string? AccessToken,
        [property: JsonPropertyName("refreshToken")] string? RefreshToken);

    public sealed record TokenResponse(
        [property: JsonPropertyName("accessToken")] string AccessToken,
        [property: JsonPropertyName("refreshToken")] string RefreshToken,
        [property: JsonPropertyName("accessTokenExpires")] string AccessTokenExpires,
        [property: JsonPropertyName("refreshTokenExpires")] string RefreshTokenExpires);

    public sealed record UserInfoResponse(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("displayName")] string? DisplayName,
        [property: JsonPropertyName("roles")] IList<string> Roles,
        [property: JsonPropertyName("isAuthenticated")] bool IsAuthenticated)
    {
        public static UserInfoResponse From(AuthUser user) =>
            new(user.Id, user.Email, user.DisplayName, new[] { "Admin", "User" }, true);
    }
}

// ----------------------------------------------------------------------
// In-memory user store. Seeded with four sample accounts to match every
// other language sample's Quick Login buttons.
// ----------------------------------------------------------------------

public sealed record AuthUser(string Id, string Email, string DisplayName)
{
    internal string PasswordHash { get; init; } = string.Empty;
}

public sealed class AuthUserStore
{
    private readonly ConcurrentDictionary<string, AuthUser> _byEmail = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, AuthUser> _byId = new(StringComparer.Ordinal);
    private static readonly string[] SampleEmails = { "user1@example.com", "user2@example.com", "user3@example.com", "admin@example.com" };
    private const string SamplePassword = "Sekiban1234%";

    public AuthUserStore()
    {
        foreach (var email in SampleEmails)
        {
            var display = email.StartsWith("admin", StringComparison.OrdinalIgnoreCase) ? "Administrator"
                : $"User {email[4]}";
            var user = new AuthUser(Guid.NewGuid().ToString(), email, display)
            {
                PasswordHash = PasswordHasher.Hash(SamplePassword)
            };
            _byEmail[email] = user;
            _byId[user.Id] = user;
        }
    }

    public AuthUser? Authenticate(string email, string password)
    {
        if (!_byEmail.TryGetValue(email, out var user)) return null;
        return PasswordHasher.Verify(password, user.PasswordHash) ? user : null;
    }

    public AuthUser Register(string email, string password, string? displayName)
    {
        var user = new AuthUser(Guid.NewGuid().ToString(), email, displayName ?? email)
        {
            PasswordHash = PasswordHasher.Hash(password)
        };
        if (!_byEmail.TryAdd(email, user))
        {
            throw new InvalidOperationException("email already registered");
        }
        _byId[user.Id] = user;
        return user;
    }

    public AuthUser? FindById(string id) => _byId.TryGetValue(id, out var u) ? u : null;
}

// ----------------------------------------------------------------------
// PBKDF2 password hashing — format: pbkdf2$<iters>$<salt-b64>$<hash-b64>
// Same shape the Swift sample uses, so both samples speak the same wire.
// ----------------------------------------------------------------------

public static class PasswordHasher
{
    private const int Iterations = 50_000;
    private const int KeyLength = 32;
    private const int SaltLength = 16;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var derived = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeyLength);
        return $"pbkdf2${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(derived)}";
    }

    public static bool Verify(string password, string encoded)
    {
        var parts = encoded.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2") return false;
        if (!int.TryParse(parts[1], out var iters)) return false;
        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }
        var derived = Rfc2898DeriveBytes.Pbkdf2(password, salt, iters, HashAlgorithmName.SHA256, KeyLength);
        return CryptographicOperations.FixedTimeEquals(derived, expected);
    }
}

// ----------------------------------------------------------------------
// JWT codec — minimal HMAC-SHA256 implementation (header.payload.sig).
// ----------------------------------------------------------------------

public sealed record AuthTokenClaims(
    [property: JsonPropertyName("sub")] string Sub,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("iat")] long Iat,
    [property: JsonPropertyName("exp")] long Exp);

public sealed class JwtCodec
{
    private readonly byte[] _key;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private static readonly byte[] HeaderSegment = Base64UrlEncode(Encoding.UTF8.GetBytes(@"{""alg"":""HS256"",""typ"":""JWT""}"));

    public JwtCodec(string signingKey)
    {
        var bytes = Encoding.UTF8.GetBytes(signingKey.Trim());
        if (bytes.Length == 0) throw new ArgumentException("signing key must be non-empty", nameof(signingKey));
        var material = new List<byte>(bytes);
        while (material.Count < 32) material.AddRange(bytes);
        _key = material.ToArray();
    }

    public (string Token, long ExpiresAt) Issue(AuthUser user)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var exp = now + AuthEndpoints.DefaultSessionTtlSeconds;
        var claims = new AuthTokenClaims(user.Id, user.Email, user.DisplayName, now, exp);
        return (IssueFromClaims(claims), exp);
    }

    private string IssueFromClaims(AuthTokenClaims claims)
    {
        var payloadJson = JsonSerializer.SerializeToUtf8Bytes(claims, JsonOpts);
        var payloadSegment = Base64UrlEncode(payloadJson);
        var signingInput = Encoding.UTF8.GetBytes($"{Encoding.UTF8.GetString(HeaderSegment)}.{Encoding.UTF8.GetString(payloadSegment)}");
        using var hmac = new HMACSHA256(_key);
        var sig = hmac.ComputeHash(signingInput);
        return $"{Encoding.UTF8.GetString(HeaderSegment)}.{Encoding.UTF8.GetString(payloadSegment)}.{Encoding.UTF8.GetString(Base64UrlEncode(sig))}";
    }

    public AuthTokenClaims? Verify(string token)
    {
        var claims = VerifySignatureAndDecode(token);
        if (claims is null) return null;
        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= claims.Exp) return null;
        return claims;
    }

    public AuthTokenClaims? VerifyAllowingExpired(string token) => VerifySignatureAndDecode(token);

    private AuthTokenClaims? VerifySignatureAndDecode(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 3) return null;
        byte[] expectedSig;
        try
        {
            expectedSig = Base64UrlDecode(parts[2]);
        }
        catch (FormatException)
        {
            return null;
        }
        var signingInput = Encoding.UTF8.GetBytes($"{parts[0]}.{parts[1]}");
        using var hmac = new HMACSHA256(_key);
        var actualSig = hmac.ComputeHash(signingInput);
        if (!CryptographicOperations.FixedTimeEquals(actualSig, expectedSig)) return null;
        try
        {
            var payloadBytes = Base64UrlDecode(parts[1]);
            return JsonSerializer.Deserialize<AuthTokenClaims>(payloadBytes, JsonOpts);
        }
        catch
        {
            return null;
        }
    }

    private static byte[] Base64UrlEncode(byte[] data)
    {
        var s = Convert.ToBase64String(data).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return Encoding.ASCII.GetBytes(s);
    }

    private static byte[] Base64UrlDecode(string segment)
    {
        var s = segment.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }
}
