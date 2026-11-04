using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Rose.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Rose.Api.IntegrationTests;

/// <summary>
/// Hosts the real API in memory against its own fresh PostgreSQL database (in a throw-away
/// Docker container), with test-only Supabase settings and no background scheduler.
/// Nothing touches your real data. Requires Docker Desktop to be running.
/// </summary>
public sealed class RoseApiFactory : WebApplicationFactory<Program>
{
    public const string SupabaseUrl = "https://test-project.supabase.co";
    public const string JwtSecret = "integration-tests-only-secret-0123456789abcdef";
    public const string AllowedEmail = "rose@example.com";

    private readonly string _connectionString = PostgresServer.NewDatabase();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Rose", _connectionString);
        builder.UseSetting("Supabase:Url", SupabaseUrl);
        builder.UseSetting("Supabase:PublishableKey", "sb_publishable_test");
        builder.UseSetting("Supabase:JwtSecret", JwtSecret);
        builder.UseSetting("Supabase:AllowedEmails:0", AllowedEmail);
        builder.UseSetting("Reminders:SchedulerEnabled", "false");
    }

    /// <summary>A Supabase-style access token, signed with the test secret.</summary>
    public static string Token(
        string email = AllowedEmail, string issuer = $"{SupabaseUrl}/auth/v1", DateTime? expires = null, string role = "authenticated")
    {
        var expiry = expires ?? DateTime.UtcNow.AddHours(1);
        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = "authenticated",
            IssuedAt = expiry.AddHours(-1),
            NotBefore = expiry.AddHours(-1),
            Expires = expiry,
            Subject = new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString()), new Claim("email", email), new Claim("role", role)]),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret)), SecurityAlgorithms.HmacSha256),
        });
    }

    /// <summary>A client signed in as the allowed user.</summary>
    public Task<HttpClient> CreateAuthenticatedClientAsync() => Task.FromResult(CreateClientWith(Token()));

    public HttpClient CreateClientWith(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>A DbContext on this factory's database, to check what was really stored.</summary>
    public RoseDbContext Database()
    {
        _ = Server; // make sure the host (and so the migrations) ran
        return Services.GetRequiredService<IDbContextFactory<RoseDbContext>>().CreateDbContext();
    }
}

/// <summary>
/// One PostgreSQL server for the whole test run; every factory gets its own database in it.
/// Normally a throw-away Docker container; set ROSE_TEST_POSTGRES to a connection string to use
/// a server you already have instead (the user needs permission to create databases).
/// </summary>
internal static class PostgresServer
{
    private static readonly Lazy<string> Server = new(() =>
    {
        var external = Environment.GetEnvironmentVariable("ROSE_TEST_POSTGRES");
        if (!string.IsNullOrWhiteSpace(external)) return external;

        var container = new PostgreSqlBuilder("postgres:17-alpine").Build();
        container.StartAsync().GetAwaiter().GetResult();
        return container.GetConnectionString();
    });

    /// <summary>A connection string to a new, empty database (the API's migrations create it).</summary>
    public static string NewDatabase() =>
        new NpgsqlConnectionStringBuilder(Server.Value)
        {
            Database = $"rose_{Guid.NewGuid():N}",
        }.ConnectionString;
}

internal static class HttpExtensions
{
    public static async Task<JsonElement> JsonAsync(this HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    public static async Task<string> DetailAsync(this HttpResponseMessage response) =>
        (await response.JsonAsync()).GetProperty("detail").GetString()!;
}