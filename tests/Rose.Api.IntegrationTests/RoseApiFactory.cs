using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rose.Application.Abstractions;
using Rose.Domain.Common;

namespace Rose.Api.IntegrationTests;

/// <summary>
/// Hosts the real API in memory with its own temp data folder, a test token and no
/// background scheduler, desktop popups or real translator - nothing touches your real files
/// or the internet.
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
        builder.ConfigureTestServices(services => services.AddSingleton<ITranslator, FakeTranslator>());
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
        // Pooled SQLite connections keep lingo.db open; release them so the folder can be deleted.
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(DataDirectory, recursive: true); }
        catch (IOException) { }
    }
}

/// <summary>
/// Answers "[to] text"; translating "broken" fails like a corrupt model and "uninstalled" like a missing one.
/// </summary>
internal sealed class FakeTranslator : ITranslator
{
    public Task<string> TranslateAsync(string text, string from, string to, CancellationToken ct) => text switch
    {
        "broken" => throw new UpstreamException("The offline translator failed on that text - try a shorter sentence."),
        "uninstalled" => throw new UnavailableException("The offline translator isn't installed yet - run setup-lingo.cmd."),
        _ => Task.FromResult(to == "ja" && text == "thank you" ? "ありがとう" : $"[{to}] {text}"),
    };

    public TranslatorStatus Status() => new(Installed: true, Loaded: false, Engine: "fake", Message: "Ready (offline).");
}

internal static class HttpExtensions
{
    public static async Task<JsonElement> JsonAsync(this HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    public static async Task<string> DetailAsync(this HttpResponseMessage response) =>
        (await response.JsonAsync()).GetProperty("detail").GetString()!;
}