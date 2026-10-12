using System.Reflection;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Rose.Api.Auth;
using Rose.Api.Infrastructure;
using Rose.Api.Reminders;
using Rose.Application;
using Rose.Application.Common;
using Rose.Infrastructure;
using Rose.Infrastructure.Reminders;

var builder = WebApplication.CreateBuilder(args);

// Your secrets (Auth:AccessToken) live here, never in git.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

builder.Services.AddRoseApplication();
builder.Services.AddRoseInfrastructure(builder.Configuration);

// ---- auth: access token -> signed session token (JWT) --------------------
builder.Services.AddOptions<AuthOptions>()
    .Bind(builder.Configuration.GetSection(AuthOptions.Section))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<IAlarmCallback, AlarmCallback>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<TokenService>((options, tokens) =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = AuthOptions.Issuer,
            ValidAudience = AuthOptions.Audience,
            IssuerSigningKey = tokens.SigningKey,
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    });
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(AuthController.LoginRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.OnRejected = async (context, ct) =>
    {
        var problems = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problems.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context.HttpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Detail = "Too many attempts - wait a minute and try again.",
            },
        });
    };
});

// ---- HTTP ------------------------------------------------------------------
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<RoseExceptionHandler>();
builder.Services.AddControllers().AddJsonOptions(options =>
{
    // snake_case, exactly like the old Flask API, so the React types stay the same.
    options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.JsonSerializerOptions.Converters.Add(new LenientStringConverter());
    options.JsonSerializerOptions.Converters.Add(new IsoDateTimeOffsetConverter());
});
builder.Services.AddHealthChecks();
builder.Services.AddHostedService<ReminderWorker>();

// ---- Swagger -------------------------------------------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Project ROSE API",
        Version = "v1",
        Description = "Payment Tracker and Reminders for Project ROSE. Click **Authorize**, then call "
            + "`POST /api/auth/login` with your access token and paste the `access_token` it returns.",
    });
    options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml"));
    options.SupportNonNullableReferenceTypes();
    options.SchemaFilter<RequiredPropertiesSchemaFilter>();
    options.CustomSchemaIds(type => type.FullName!.Replace('+', '.').Split('.').Last());

    var bearer = new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "The access_token from POST /api/auth/login.",
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
    };
    options.AddSecurityDefinition("Bearer", bearer);
    options.AddSecurityRequirement(new OpenApiSecurityRequirement { [bearer] = [] });
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.DocumentTitle = "Project ROSE API";
        options.EnablePersistAuthorization();
    });
}

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapControllers();
app.MapGet("/", () => Results.Redirect(app.Environment.IsDevelopment() ? "/swagger" : "/health"))
    .AllowAnonymous()
    .ExcludeFromDescription();

await app.RunAsync();

/// <summary>Exposed for WebApplicationFactory in the integration tests.</summary>
public partial class Program;