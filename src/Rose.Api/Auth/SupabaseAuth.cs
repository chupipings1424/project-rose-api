using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Rose.Api.Auth;

/// <summary>
/// Supabase Auth signs the user in (email + password) and hands the browser a JWT. This API only
/// verifies that token - signature, issuer, audience, expiry - and that the email is allowed.
/// </summary>
internal static class SupabaseAuth
{
    public const string Audience = "authenticated";

    public static IServiceCollection AddSupabaseAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SupabaseOptions>()
            .Bind(configuration.GetSection(SupabaseOptions.Section))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<SupabaseOptions>>((options, supabase) =>
            {
                var settings = supabase.Value;
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = settings.Issuer,
                    ValidAudience = Audience,
                    ClockSkew = TimeSpan.FromMinutes(1),
                    NameClaimType = "email",
                    RoleClaimType = "role",
                };
                if (settings.JwtSecret is { Length: > 0 } secret)
                {
                    options.TokenValidationParameters.IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
                }
                else
                {
                    // Supabase publishes its public signing keys; they are cached and refreshed automatically.
                    options.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                        $"{settings.Issuer}/.well-known/jwks.json",
                        new JwksRetriever(),
                        new HttpDocumentRetriever { RequireHttps = true });
                }
            });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireClaim("role", Audience)
                .AddRequirements(new AllowedEmailRequirement())
                .Build());
        services.AddSingleton<IAuthorizationHandler, AllowedEmailHandler>();
        return services;
    }

    /// <summary>Reads a bare JWKS document (Supabase has no full OpenID discovery document).</summary>
    private sealed class JwksRetriever : IConfigurationRetriever<OpenIdConnectConfiguration>
    {
        public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(
            string address, IDocumentRetriever retriever, CancellationToken cancel)
        {
            var keys = new JsonWebKeySet(await retriever.GetDocumentAsync(address, cancel));
            var configuration = new OpenIdConnectConfiguration();
            foreach (var key in keys.GetSigningKeys()) configuration.SigningKeys.Add(key);
            return configuration;
        }
    }
}

/// <summary>The signed-in email must be on Supabase:AllowedEmails.</summary>
internal sealed class AllowedEmailRequirement : IAuthorizationRequirement;

internal sealed class AllowedEmailHandler(IOptionsMonitor<SupabaseOptions> options) : AuthorizationHandler<AllowedEmailRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AllowedEmailRequirement requirement)
    {
        var email = context.User.FindFirst("email")?.Value;
        if (email is { Length: > 0 }
            && options.CurrentValue.AllowedEmails.Any(allowed => string.Equals(allowed.Trim(), email, StringComparison.OrdinalIgnoreCase)))
        {
            context.Succeed(requirement);
        }
        return Task.CompletedTask;
    }
}