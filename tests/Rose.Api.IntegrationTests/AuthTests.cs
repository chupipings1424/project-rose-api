using System.Net;
using System.Net.Http.Headers;

namespace Rose.Api.IntegrationTests;

public sealed class AuthTests(RoseApiFactory factory) : IClassFixture<RoseApiFactory>
{
    private static Uri Url(string path) => new(path, UriKind.Relative);

    [Fact]
    public async Task Health_is_public_and_checks_the_database()
    {
        var response = await factory.CreateClient().GetAsync(Url("/health"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Config_is_public_and_tells_the_ui_where_supabase_is()
    {
        var config = await (await factory.CreateClient().GetAsync(Url("/api/config"))).JsonAsync();

        Assert.Equal(RoseApiFactory.SupabaseUrl, config.GetProperty("supabase_url").GetString());
        Assert.Equal("sb_publishable_test", config.GetProperty("supabase_key").GetString());
    }

    [Theory]
    [InlineData("/api/payment-tracker/accounts")]
    [InlineData("/api/reminders/items")]
    [InlineData("/api/auth/session")]
    public async Task Everything_else_needs_a_token(string path)
    {
        var response = await factory.CreateClient().GetAsync(Url(path));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_valid_token_returns_the_session()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        var session = await (await client.GetAsync(Url("/api/auth/session"))).JsonAsync();

        Assert.True(session.GetProperty("authenticated").GetBoolean());
        Assert.Equal(RoseApiFactory.AllowedEmail, session.GetProperty("email").GetString());
        Assert.True(session.GetProperty("expires_at").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddMinutes(50));
    }

    [Fact]
    public async Task Allowed_emails_match_case_insensitively()
    {
        var client = factory.CreateClientWith(RoseApiFactory.Token(email: "ROSE@Example.com"));

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Url("/api/reminders/items"))).StatusCode);
    }

    [Fact]
    public async Task Another_supabase_user_is_forbidden()
    {
        var client = factory.CreateClientWith(RoseApiFactory.Token(email: "stranger@example.com"));

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Url("/api/reminders/items"))).StatusCode);
    }

    [Fact]
    public async Task An_anonymous_supabase_token_is_forbidden()
    {
        var client = factory.CreateClientWith(RoseApiFactory.Token(role: "anon"));

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Url("/api/reminders/items"))).StatusCode);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("issuer")]
    [InlineData("tampered")]
    public async Task Bad_tokens_are_refused(string problem)
    {
        var token = problem switch
        {
            "expired" => RoseApiFactory.Token(expires: DateTime.UtcNow.AddMinutes(-5)),
            "issuer" => RoseApiFactory.Token(issuer: "https://someone-else.supabase.co/auth/v1"),
            _ => RoseApiFactory.Token()[..^4] + "AAAA",
        };
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Url("/api/payment-tracker/accounts"))).StatusCode);
    }

    [Fact]
    public async Task Swagger_describes_the_api()
    {
        var doc = await (await factory.CreateClient().GetAsync(Url("/swagger/v1/swagger.json"))).JsonAsync();

        Assert.Equal("Project ROSE API", doc.GetProperty("info").GetProperty("title").GetString());
        Assert.True(doc.GetProperty("paths").TryGetProperty("/api/payment-tracker/import/budget", out _));
        Assert.False(doc.GetProperty("paths").TryGetProperty("/api/auth/login", out _));
        Assert.True(doc.GetProperty("components").GetProperty("securitySchemes").TryGetProperty("Bearer", out _));
    }
}