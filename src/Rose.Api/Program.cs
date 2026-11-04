using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi.Models;
using Rose.Api.Auth;
using Rose.Api.Infrastructure;
using Rose.Api.Scheduling;
using Rose.Application;
using Rose.Application.Common;
using Rose.Infrastructure;
using Rose.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Local secrets (Supabase settings, connection string) live here, never in git. It goes right
// after the other JSON files, so environment variables (Render) and test settings still win.
var sources = builder.Configuration.Sources;
sources.Insert(
    sources.ToList().FindLastIndex(source => source is JsonConfigurationSource) + 1,
    new JsonConfigurationSource { Path = "appsettings.Local.json", Optional = true, ReloadOnChange = true });

builder.Services.AddRoseApplication();
builder.Services.AddRoseInfrastructure(builder.Configuration);
builder.Services.AddSupabaseAuth(builder.Configuration);

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
builder.Services.AddHostedService<SchedulerWorker>();

// ---- Swagger -------------------------------------------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Project ROSE API",
        Version = "v1",
        Description = "Payment Tracker, Budget, Reminders, Savings Goals and Habits for Project ROSE. Sign in to the React app, "
            + "copy the Supabase access token (DevTools > Application > Local Storage), click **Authorize** and paste it.",
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
        Description = "A Supabase access token for an allowed email.",
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
    };
    options.AddSecurityDefinition("Bearer", bearer);
    options.AddSecurityRequirement(new OpenApiSecurityRequirement { [bearer] = [] });
});

var app = builder.Build();

// Create / upgrade the tables before anything can touch them.
await RoseDatabase.InitializeAsync(app.Services);

app.UseExceptionHandler();
app.UseStatusCodePages();
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers[HeaderNames.XContentTypeOptions] = "nosniff";
    headers[HeaderNames.XFrameOptions] = "DENY";
    headers["Referrer-Policy"] = "no-referrer";
    await next();
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.DocumentTitle = "Project ROSE API";
        options.EnablePersistAuthorization();
    });
}

// In the container the built React app sits in wwwroot and this server hosts it.
// Locally Vite serves the UI and proxies /api here, so there is no wwwroot.
var hasUi = app.Environment.WebRootPath is { } webRoot && File.Exists(Path.Combine(webRoot, "index.html"));
if (hasUi)
{
    app.UseDefaultFiles(); // "/" -> /index.html
    app.UseStaticFiles(new StaticFileOptions
    {
        // Vite fingerprints everything in /assets, so those can be cached forever; the rest
        // (index.html, the service worker, the manifest) must always be revalidated.
        OnPrepareResponse = context => context.Context.Response.Headers.CacheControl =
            context.Context.Request.Path.StartsWithSegments("/assets", StringComparison.Ordinal)
                ? "public, max-age=31536000, immutable"
                : "no-cache",
        ContentTypeProvider = new FileExtensionContentTypeProvider { Mappings = { [".webmanifest"] = "application/manifest+json" } },
    });
}

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapControllers();
if (hasUi)
{
    // Client-side routes (/projects/budget...) all load index.html; React Router takes it from there.
    // Unknown /api/* addresses still get a real 404 instead of a web page.
    app.MapFallbackToFile("{*path:nonfile:regex(^(?!api/).*$)}", "index.html", new StaticFileOptions
    {
        OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache",
    })
        .AllowAnonymous()
        .ExcludeFromDescription();
}
else
{
    app.MapGet("/", () => Results.Redirect(app.Environment.IsDevelopment() ? "/swagger" : "/health"))
        .AllowAnonymous()
        .ExcludeFromDescription();
}

await app.RunAsync();

/// <summary>Exposed for WebApplicationFactory in the integration tests.</summary>
public partial class Program;