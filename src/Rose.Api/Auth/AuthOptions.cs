using System.ComponentModel.DataAnnotations;

namespace Rose.Api.Auth;

/// <summary>The "Auth" section. Keep <see cref="AccessToken"/> out of git: use appsettings.Local.json or user-secrets.</summary>
public sealed class AuthOptions
{
    public const string Section = "Auth";
    public const string Issuer = "project-rose-api";
    public const string Audience = "project-rose";

    /// <summary>The access token you type on the ROSE login page.</summary>
    [Required(ErrorMessage = "Set Auth:AccessToken (appsettings.Local.json, user-secrets or the Auth__AccessToken variable).")]
    [MinLength(8)]
    public string AccessToken { get; set; } = "";

    /// <summary>Optional base64 HMAC key for signing sessions. When empty one is generated and saved in the data folder.</summary>
    public string? SigningKey { get; set; }

    /// <summary>How long a login lasts.</summary>
    [Range(typeof(TimeSpan), "00:05:00", "30.00:00:00")]
    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromHours(12);
}