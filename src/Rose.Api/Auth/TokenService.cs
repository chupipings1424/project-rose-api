using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Rose.Infrastructure;

namespace Rose.Api.Auth;

/// <summary>Checks the access token and issues signed session tokens (JWTs).</summary>
public sealed class TokenService(IOptions<AuthOptions> auth, IOptions<RoseOptions> rose, TimeProvider clock)
{
    private readonly Lazy<SymmetricSecurityKey> key = new(() => LoadKey(auth.Value, rose.Value));

    public SymmetricSecurityKey SigningKey => key.Value;

    /// <summary>Constant-time comparison, so response timing reveals nothing about the token.</summary>
    public bool IsValidAccessToken(string? candidate) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(candidate ?? "")),
            SHA256.HashData(Encoding.UTF8.GetBytes(auth.Value.AccessToken)));

    public LoginResponse CreateSession() => Create("rose-user", auth.Value.SessionLifetime);

    public LoginResponse Create(string subject, TimeSpan lifetime)
    {
        var now = clock.GetUtcNow();
        var expires = now + lifetime;
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = AuthOptions.Issuer,
            Audience = AuthOptions.Audience,
            Subject = new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, subject)]),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
        });
        return new LoginResponse(token, "Bearer", expires);
    }

    private static SymmetricSecurityKey LoadKey(AuthOptions auth, RoseOptions rose)
    {
        if (auth.SigningKey is { Length: > 0 } configured) return new SymmetricSecurityKey(Convert.FromBase64String(configured));

        // Saved next to the data so logins survive a restart. Delete the file to log every device out.
        var file = Path.Combine(rose.DataDirectory, "session-signing.key");
        if (File.Exists(file)) return new SymmetricSecurityKey(Convert.FromBase64String(File.ReadAllText(file).Trim()));
        var bytes = RandomNumberGenerator.GetBytes(64);
        Directory.CreateDirectory(rose.DataDirectory);
        File.WriteAllText(file, Convert.ToBase64String(bytes));
        return new SymmetricSecurityKey(bytes);
    }
}

/// <summary>A signed session token. Send it back in the Authorization header as a Bearer token.</summary>
public sealed record LoginResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAt);

public sealed record LoginRequest(string? Token);

public sealed record SessionInfo(bool Authenticated, DateTimeOffset? ExpiresAt);
