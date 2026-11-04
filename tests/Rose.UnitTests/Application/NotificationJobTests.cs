using Microsoft.Extensions.Logging.Abstractions;
using Rose.Application.Habits;
using Rose.Application.Notifications;
using Rose.Application.Reminders;
using Rose.Domain.Habits;
using Rose.Domain.Reminders;
using Rose.UnitTests.Support;

namespace Rose.UnitTests.Application;

/// <summary>The scheduler jobs: every user's reminders and habits reach only that user's devices and inbox.</summary>
public sealed class NotificationJobTests : IDisposable
{
    // 09:00 UTC = 10:00 in Berlin (CET).
    private static readonly DateTimeOffset Start = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
    private readonly FakeClock _clock = new(Start, Zones.Berlin);
    private readonly RecordingPush _sender = new();
    private readonly RecordingEmail _email = new();
    private readonly MemoryUserStore _store = new(TestUsers.AliceAccount, TestUsers.BobAccount);
    private readonly ReminderService _reminders;
    private readonly HabitService _habits;
    private readonly PushService _push;
    private readonly EmailNotifier _notifier;

    public NotificationJobTests()
    {
        _reminders = new ReminderService(new MemoryRepository<Reminder>(r => r.Id), _clock);
        _habits = new HabitService(new MemoryRepository<Habit>(h => h.Id), _clock);
        _push = new PushService(new MemoryRepository<PushDevice>(d => d.Subscription.Endpoint!), _sender, _clock, NullLogger<PushService>.Instance);
        _notifier = new EmailNotifier(_email, new PublicSite(new Uri("https://rose.example")), NullLogger<EmailNotifier>.Instance);
    }

    public void Dispose()
    {
        _reminders.Dispose();
        _habits.Dispose();
        _push.Dispose();
    }

    private ReminderRinger Ringer() =>
        new(_reminders, _push, TestUsers.Service(_store, clock: _clock), _notifier, _clock, NullLogger<ReminderRinger>.Instance);

    private HabitNudger Nudger() =>
        new(_habits, _push, TestUsers.Service(_store, clock: _clock), _notifier, _clock, NullLogger<HabitNudger>.Instance);

    private Task<int> Subscribe(Guid owner, string endpoint) =>
        _push.SubscribeAsync(owner, new PushSubscribeRequest(new PushSubscriptionInfo(endpoint, null, new PushKeys("p", "a")), "Phone"));

    [Fact]
    public async Task A_reminder_rings_on_its_owners_devices_and_is_emailed_once()
    {
        await Subscribe(TestUsers.Alice, "https://push.example/alice");
        await Subscribe(TestUsers.Bob, "https://push.example/bob");
        var reminder = await _reminders.CreateAsync(TestUsers.Alice, new ReminderRequest("Pay rent", Start.AddMinutes(1).ToString("O"), "none", null));
        var ringer = Ringer();

        _clock.Advance(TimeSpan.FromMinutes(2));
        await ringer.TickAsync();

        var push = Assert.Single(_sender.Sent);
        Assert.Equal("https://push.example/alice", push.Endpoint);
        var mail = Assert.Single(_email.Sent);
        Assert.Equal("alice@example.com", mail.To);
        Assert.Equal("\u23F0 Pay rent", mail.Subject);
        Assert.Contains("https://rose.example/projects/reminders", mail.Text, StringComparison.Ordinal);

        // Snoozed: it rings on the phone again, but the inbox only gets the first one.
        await _reminders.SnoozeAsync(TestUsers.Alice, reminder.Id, 5);
        _clock.Advance(TimeSpan.FromMinutes(6));
        await ringer.TickAsync();

        Assert.Equal(2, _sender.Sent.Count);
        Assert.Single(_email.Sent);
    }

    [Fact]
    public async Task A_reminder_is_emailed_even_without_any_device()
    {
        await _reminders.CreateAsync(TestUsers.Bob, new ReminderRequest("Call mum", Start.AddMinutes(1).ToString("O"), "none", null));

        _clock.Advance(TimeSpan.FromMinutes(2));
        await Ringer().TickAsync();

        Assert.Empty(_sender.Sent);
        Assert.Equal("bob@example.com", Assert.Single(_email.Sent).To);
    }

    [Fact]
    public async Task No_email_key_means_no_email()
    {
        _email.IsConfigured = false;
        await _reminders.CreateAsync(TestUsers.Alice, new ReminderRequest("Pay rent", Start.AddMinutes(1).ToString("O"), "none", null));

        _clock.Advance(TimeSpan.FromMinutes(2));
        await Ringer().TickAsync();

        Assert.Empty(_email.Sent);
    }

    [Fact]
    public async Task A_habit_nudge_is_pushed_and_emailed_to_its_owner()
    {
        var habit = await _habits.CreateAsync(TestUsers.Alice, new HabitRequest("Water", "", "count", 8, "glasses", "10:30"));
        await Subscribe(TestUsers.Alice, "https://push.example/alice");
        await Subscribe(TestUsers.Bob, "https://push.example/bob");
        var nudger = Nudger();

        _clock.Advance(TimeSpan.FromMinutes(30));
        await nudger.TickAsync();

        var (endpoint, payload) = Assert.Single(_sender.Sent);
        Assert.Equal("https://push.example/alice", endpoint);
        Assert.Contains("\"body\":\"0 / 8 glasses so far today\"", payload, StringComparison.Ordinal);
        Assert.Contains($"\"tag\":\"habit-{habit.Id}\"", payload, StringComparison.Ordinal);
        Assert.Contains("\"url\":\"/projects/habits\"", payload, StringComparison.Ordinal);
        Assert.Equal("alice@example.com", Assert.Single(_email.Sent).To);

        // A snoozed nudge comes back on the phone only.
        await _habits.SnoozeNudgeAsync(TestUsers.Alice, habit.Id, 10);
        _clock.Advance(TimeSpan.FromMinutes(11));
        await nudger.TickAsync();

        Assert.Equal(2, _sender.Sent.Count);
        Assert.Single(_email.Sent);
    }

    [Fact]
    public async Task A_device_that_signs_in_with_another_account_moves_to_it()
    {
        await Subscribe(TestUsers.Alice, "https://push.example/shared");
        await Subscribe(TestUsers.Bob, "https://push.example/shared");

        Assert.Equal(0, await _push.CountAsync(TestUsers.Alice));
        Assert.Equal(1, await _push.CountAsync(TestUsers.Bob));
    }

    [Fact]
    public async Task Email_text_is_escaped_in_the_html()
    {
        await _reminders.CreateAsync(TestUsers.Alice, new ReminderRequest("<b>rent</b>", Start.AddMinutes(1).ToString("O"), "none", null));

        _clock.Advance(TimeSpan.FromMinutes(2));
        await Ringer().TickAsync();

        var mail = Assert.Single(_email.Sent);
        Assert.Contains("&lt;b&gt;rent&lt;/b&gt;", mail.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>rent</b>", mail.Html, StringComparison.Ordinal);
    }
}