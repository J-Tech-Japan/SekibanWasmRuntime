namespace SekibanDcbDecider.ApiService.Auth;

/// <summary>
///     JWT configuration settings.
///     Configure in appsettings.json under "Jwt" section.
/// </summary>
public class JwtSettings
{
    public const string SectionName = "Jwt";
    public const string PlaceholderSecretKey = "CHANGE_ME_IN_USER_SECRETS_OR_ENVIRONMENT_32_CHARS_MIN";

    /// <summary>
    ///     Secret key for signing tokens (minimum 32 characters)
    /// </summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>
    ///     Token issuer (typically the API server URL)
    /// </summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>
    ///     Token audience (typically the client applications)
    /// </summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    ///     Access token expiration in minutes
    /// </summary>
    public int AccessTokenExpirationMinutes { get; set; } = 15;

    /// <summary>
    ///     Refresh token expiration in days
    /// </summary>
    public int RefreshTokenExpirationDays { get; set; } = 7;
}
