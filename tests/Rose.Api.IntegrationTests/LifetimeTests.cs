using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Rose.Api.Lifetime;

namespace Rose.Api.IntegrationTests;

public sealed class PresenceTrackerTests
{
    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void Is_not_idle_before_any_page_connected()
    {
        var tracker = new PresenceTracker(new ManualClock());
        Assert.Null(tracker.IdleFor());
    }

    [Fact]
    public void Counts_idle_time_only_after_the_last_page_leaves()
    {
        var clock = new ManualClock();
        var tracker = new PresenceTracker(clock);
        var first = tracker.Connect();
        var second = tracker.Connect();

        first.Dispose();
        first.Dispose(); // twice is harmless
        Assert.Equal(1, tracker.Connections);
        Assert.Null(tracker.IdleFor());

        second.Dispose();
        clock.Now += TimeSpan.FromSeconds(21);
        Assert.Equal(TimeSpan.FromSeconds(21), tracker.IdleFor());
    }

    [Fact]
    public void A_reload_reconnects_and_resets_the_idle_timer()
    {
        var clock = new ManualClock();
        var tracker = new PresenceTracker(clock);
        tracker.Connect().Dispose();
        clock.Now += TimeSpan.FromSeconds(1);

        using var reloaded = tracker.Connect();

        Assert.Null(tracker.IdleFor());
    }
}

public sealed class LifetimeTests : IClassFixture<RoseApiFactory>
{
    private readonly RoseApiFactory factory;

    public LifetimeTests(RoseApiFactory factory) => this.factory = factory;

    [Fact]
    public async Task Info_is_public_and_shutdown_is_off_by_default()
    {
        var info = await factory.CreateClient().GetAsync("/api/system/info");

        Assert.Equal(HttpStatusCode.OK, info.StatusCode);
        var body = await info.JsonAsync();
        Assert.False(body.GetProperty("shutdown_enabled").GetBoolean());
        Assert.Equal(20, body.GetProperty("idle_timeout_seconds").GetDouble());
    }

    [Fact]
    public async Task Shutdown_is_refused_when_not_started_by_the_launcher()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/system/shutdown");
        request.Headers.Add(SystemController.ClientHeader, "1");

        var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Presence_stream_counts_an_open_page_until_it_closes()
    {
        var tracker = factory.Services.GetRequiredService<PresenceTracker>();
        var client = factory.CreateClient();

        using (var response = await client.GetAsync("/api/system/presence", HttpCompletionOption.ResponseHeadersRead))
        {
            Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
            await using var stream = await response.Content.ReadAsStreamAsync();
            var buffer = new byte[16];
            Assert.True(await stream.ReadAsync(buffer) > 0); // the first ping arrived
            Assert.Equal(1, tracker.Connections);
        }

        await WaitUntil(() => tracker.Connections == 0);
        Assert.NotNull(tracker.IdleFor());
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++) await Task.Delay(50);
        Assert.True(condition());
    }
}

/// <summary>A host with closing turned on, as the launcher runs it.</summary>
public sealed class ShutdownEnabledTests : IDisposable
{
    private readonly RoseApiFactory baseFactory = new();
    private readonly WebApplicationFactory<Program> factory;

    public ShutdownEnabledTests() =>
        factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Shutdown:Enabled"] = "true",
            })));

    [Fact]
    public async Task Shutdown_needs_the_client_header()
    {
        var response = await factory.CreateClient().PostAsync("/api/system/shutdown", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Shutdown_answers_then_stops_the_application()
    {
        var stopping = factory.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/system/shutdown");
        request.Headers.Add(SystemController.ClientHeader, "1");

        var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        for (var i = 0; i < 100 && !stopping.IsCancellationRequested; i++) await Task.Delay(50);
        Assert.True(stopping.IsCancellationRequested);
    }

    public void Dispose()
    {
        factory.Dispose();
        baseFactory.Dispose();
    }
}