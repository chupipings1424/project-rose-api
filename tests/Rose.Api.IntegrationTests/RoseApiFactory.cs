using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Rose.Api.IntegrationTests;

/// <summary>
/// Hosts the real API in memory with its own temp data folder, a test token and no
/// background scheduler or desktop popups - nothing touches your real files.
/// </summary>
public sealed class RoseApiFactory : WebApplicationFactory<Program>
{
    public const string AccessToken = "integration-test-token";

    public string DataDirectory { get; } = Directory.CreateTempSubdirectory("rose-api-tests-").FullName;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Rose:DataDirectory"] = DataDirectory,
            ["Auth:AccessToken"] = AccessToken,
            ["Reminders:SchedulerEnabled"] = "false",
            ["Reminders:DesktopAlarms"] = "false",
        }));
    }

    /// <summary>A client that already logged in.</summary>
    public async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { token = AccessToken });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body.GetProperty("access_token").GetString());
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { Directory.Delete(DataDirectory, recursive: true); }
        catch (IOException) { }
    }
}

internal static class HttpExtensions
{
    public static async Task<JsonElement> JsonAsync(this HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    public static async Task<string> DetailAsync(this HttpResponseMessage response) =>
        (await response.JsonAsync()).GetProperty("detail").GetString()!;
}