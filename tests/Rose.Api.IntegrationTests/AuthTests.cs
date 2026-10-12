using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Rose.Api.IntegrationTests;

public sealed class AuthTests(RoseApiFactory factory) : IClassFixture<RoseApiFactory>
{
    [Fact]
    public async Task Health_is_public()
    {
        var response = await factory.CreateClient().GetAsync(new Uri("/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/payment-tracker/accounts")]
    [InlineData("/api/reminders/items")]
    [InlineData("/api/auth/session")]
    public async Task Everything_else_needs_a_session(string path)
    {
        var response = await factory.CreateClient().GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_wrong_token_is_refused()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { token = "nope" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Access denied - that token is not valid.", await response.DetailAsync());
    }

    [Fact]
    public async Task The_right_token_returns_a_working_session()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        var session = await (await client.GetAsync(new Uri("/api/auth/session", UriKind.Relative))).JsonAsync();

        Assert.True(session.GetProperty("authenticated").GetBoolean());
        Assert.True(session.GetProperty("expires_at").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddHours(11));
    }

    [Fact]
    public async Task A_tampered_session_token_is_refused()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var token = client.DefaultRequestHeaders.Authorization!.Parameter!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token[..^4] + "AAAA");

        var response = await client.GetAsync(new Uri("/api/payment-tracker/accounts", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Swagger_describes_the_api()
    {
        var doc = await (await factory.CreateClient().GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative))).JsonAsync();

        Assert.Equal("Project ROSE API", doc.GetProperty("info").GetProperty("title").GetString());
        Assert.True(doc.GetProperty("paths").TryGetProperty("/api/payment-tracker/import/budget", out _));
        Assert.True(doc.GetProperty("components").GetProperty("securitySchemes").TryGetProperty("Bearer", out _));
    }
}

/// <summary>Its own host, so the login attempts here never throttle the other tests.</summary>
public sealed class RateLimitTests : IDisposable
{
    private readonly RoseApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Login_is_throttled_after_ten_attempts_a_minute()
    {
        var client = _factory.CreateClient();
        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 11; i++)
            codes.Add((await client.PostAsJsonAsync("/api/auth/login", new { token = "guess" })).StatusCode);

        Assert.All(codes.Take(10), c => Assert.Equal(HttpStatusCode.Unauthorized, c));
        Assert.Equal(HttpStatusCode.TooManyRequests, codes[10]);
    }
}