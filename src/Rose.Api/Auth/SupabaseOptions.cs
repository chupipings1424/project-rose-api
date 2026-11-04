using System.ComponentModel.DataAnnotations;

namespace Rose.Api.Auth;

/// <summary>The "Supabase" section: which Supabase project signs users in.</summary>
public sealed class SupabaseOptions
{
    public const string Section = "Supabase";

    /// <summary>The project URL, e.g. https://abcd1234.supabase.co.</summary>
    [Required]
    [Url]
    public string Url { get; set; } = "";

    /// <summary>The publishable (anon) key. Public by design; the React app needs it to sign in.</summary>
    public string PublishableKey { get; set; } = "";

    /// <summary>
    /// Optional legacy HS256 JWT secret. Leave empty for projects that use the newer signing keys:
    /// the public keys are then fetched from the project's JWKS endpoint.
    /// </summary>
    public string? JwtSecret { get; set; }

    // SecretKey (for the admin page) is read by Rose.Infrastructure's SupabaseAdminOptions.

    public string Issuer => $"{Url.TrimEnd('/')}/auth/v1";
}