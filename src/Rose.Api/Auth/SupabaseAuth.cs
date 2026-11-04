using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Rose.Application.Abstractions;
using Rose.Application.Users;

namespace Rose.Api.Auth;

/// <summary>
/// Supabase Auth signs the user in (email + password) and hands the browser a JWT. This API only
/// verifies that token - signature, issuer, audience, expiry - and that it belongs to a ROSE account.
/// </summary>
internal static class SupabaseAuth
{
    public const string Audience = "authenticated";

    /// <summary>Only the admins (Access:AdminEmails), e.g. the accounts page.</summary>
    public const string AdminPolicy = "Admin";

    public static IServiceCollection AddSupabaseAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SupabaseOptions>()
            .Bind(configuration.GetSection(SupabaseOptions.Section))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<AccessOptions>()
            .Bind(configuration.GetSection(AccessOptions.Section))
            .Validate(o => o.AdminEmails.Any(e => !string.IsNullOrWhiteSpace(e)), "Access:AdminEmails needs at least one email (env var Access__AdminEmails__0).")
            .ValidateOnStart();
        services.AddSingleton(sp => new AccessPolicy(sp.GetRequiredService<IOptions<AccessOptions>>().Value.AdminEmails));
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();

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
            .SetFallbackPolicy(RoseUser(adminOnly: false))
            .AddPolicy(AdminPolicy, RoseUser(adminOnly: true));
        services.AddScoped<IAuthorizationHandler, RoseUserHandler>();
        return services;
    }

    private static AuthorizationPolicy RoseUser(bool adminOnly) =>
        new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireClaim("role", Audience)
            .AddRequirements(new RoseUserRequirement(adminOnly))
            .Build();

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

/// <summary>
/// The token must belong to a ROSE account (admins always have one). Someone with a temporary
/// password may only reach endpoints marked <see cref="AllowPendingPasswordChangeAttribute"/>.
/// </summary>
internal sealed record RoseUserRequirement(bool AdminOnly) : IAuthorizationRequirement;

internal sealed class RoseUserHandler(UserService users, IHttpContextAccessor http) : AuthorizationHandler<RoseUserRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, RoseUserRequirement requirement)
    {
        if (!Guid.TryParse(context.User.FindFirst("sub")?.Value, out var id) || http.HttpContext is not { } request) return;

        var account = AccountItem.Get(request) ?? await users.ResolveAsync(id, context.User.FindFirst("email")?.Value, request.RequestAborted);
        if (account is null) return;
        AccountItem.Set(request, account);

        if (requirement.AdminOnly && !account.IsAdmin) return;
        if (account.User.MustChangePassword && request.GetEndpoint()?.Metadata.GetMetadata<AllowPendingPasswordChangeAttribute>() is null) return;
        context.Succeed(requirement);
    }
}