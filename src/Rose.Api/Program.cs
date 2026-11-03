using System.Reflection;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Rose.Api.Auth;
using Rose.Api.Desktop;
using Rose.Api.Infrastructure;
using Rose.Api.Lifetime;
using Rose.Api.Reminders;
using Rose.Api.Scheduling;
using Rose.Application;
using Rose.Application.Common;
using Rose.Infrastructure;
using Rose.Infrastructure.Reminders;

// ROSE.exe (the packaged build) reads appsettings*.json and data\ next to itself,
// wherever it is started from. Normal builds keep the default (the working folder).
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = DesktopApp.HasEmbeddedUi ? AppContext.BaseDirectory : null,
});

// Your secrets (Auth:AccessToken) live here, never in git.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

using var desktop = DesktopApp.HasEmbeddedUi ? new DesktopApp(builder.Configuration) : null;
if (desktop is { AlreadyRunning: true })
{
    desktop.OpenBrowser();
    return;
}
if (desktop is not null)
{
    // ROSE.exe always closes itself with its last tab (or the power button).
    builder.Configuration.AddInMemoryCollection([new("Shutdown:Enabled", "true")]);
}

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
builder.Services.AddHostedService<SchedulerWorker>();

// ---- lifetime: close ROSE when its last page closes (or the power button) --
builder.Services.AddOptions<ShutdownOptions>()
    .Bind(builder.Configuration.GetSection(ShutdownOptions.Section))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<PresenceTracker>();
builder.Services.AddHostedService<ShutdownWatchdog>();
// ---- Swagger -------------------------------------------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Project ROSE API",
        Version = "v1",
        Description = "Payment Tracker, Budget, Reminders, Savings Goals and Habits for Project ROSE. Click **Authorize**, then call "
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

// ROSE.exe serves the React app itself; everything else proxies /api through Vite.
var ui = desktop is null ? null : new StaticFileOptions { FileProvider = DesktopApp.UiFiles() };
if (ui is not null)
{
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = ui.FileProvider }); // "/" -> /index.html
    app.UseStaticFiles(ui);
}

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapControllers();
if (ui is not null)
{
    // Client-side routes (/projects/budget...) all load index.html; React Router takes it from there.
    // Unknown /api/* addresses still get a real 404 instead of a web page.
    app.MapFallbackToFile("{*path:nonfile:regex(^(?!api/).*$)}", "index.html", ui)
        .AllowAnonymous()
        .ExcludeFromDescription();
}
else
{
    app.MapGet("/", () => Results.Redirect(app.Environment.IsDevelopment() ? "/swagger" : "/health"))
        .AllowAnonymous()
        .ExcludeFromDescription();
}

if (desktop is null) await app.RunAsync();
else await desktop.RunAsync(app);

/// <summary>Exposed for WebApplicationFactory in the integration tests.</summary>
public partial class Program;