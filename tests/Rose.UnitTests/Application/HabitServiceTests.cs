using Microsoft.Extensions.Logging.Abstractions;
using Rose.Application.Abstractions;
using Rose.Application.Habits;
using Rose.Application.Reminders;
using Rose.Domain.Common;
using Rose.Domain.Habits;
using Rose.UnitTests.Support;

namespace Rose.UnitTests.Application;

public sealed class HabitServiceTests : IDisposable
{
    // 09:00 UTC = 10:00 in Berlin (CET).
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero), Zones.Berlin);
    private readonly MemoryRepository<Habit> _repo = new(h => h.Id);
    private readonly HabitService _service;

    public HabitServiceTests() => _service = new HabitService(_repo, _clock);

    public void Dispose() => _service.Dispose();

    private static readonly DateOnly Today = new(2026, 11, 10);

    private Task<HabitView> CreateWater(string? remindAt = null) =>
        _service.CreateAsync(new HabitRequest("Water", "", "count", 8, "glasses", remindAt));

    [Fact]
    public async Task Create_saves_a_clean_habit()
    {
        var habit = await CreateWater("21:00");

        Assert.Equal("Water", habit.Name);
        Assert.Equal("21:00", habit.RemindAt);
        Assert.Equal(0, habit.CurrentStreak);
        Assert.Single(_repo.Stored);
        Assert.Equal(1, _repo.Writes);
    }

    [Fact]
    public async Task Setting_a_day_is_idempotent_and_updates_the_streak()
    {
        var habit = await CreateWater();

        await _service.SetDayAsync(habit.Id, "2026-11-09", 8);
        await _service.SetDayAsync(habit.Id, "2026-11-10", 8);
        var after = await _service.SetDayAsync(habit.Id, "2026-11-10", 8);

        Assert.Equal(2, after.CurrentStreak);
        Assert.True(after.DoneToday);
        Assert.Equal(2, _repo.Stored[0].Log.Count);
    }

    [Fact]
    public async Task Old_days_cannot_be_changed()
    {
        var habit = await CreateWater();
        await Assert.ThrowsAsync<ValidationException>(() => _service.SetDayAsync(habit.Id, "2026-11-03", 1));
    }

    [Fact]
    public Task Unknown_habits_are_not_found() =>
        Assert.ThrowsAsync<NotFoundException>(() => _service.SetDayAsync("nope", "2026-11-10", 1));

    [Fact]
    public async Task Nudge_buttons_complete_dismiss_and_snooze()
    {
        var habit = await CreateWater("11:00");

        var snoozed = await _service.SnoozeNudgeAsync(habit.Id, 15);
        Assert.Equal(_clock.Now.AddMinutes(15), _repo.Stored[0].SnoozedUntil);
        Assert.Equal(Today, _repo.Stored[0].NudgedOn);

        await _service.DismissNudgeAsync(habit.Id);
        Assert.Null(_repo.Stored[0].SnoozedUntil);

        var done = await _service.CompleteTodayAsync(habit.Id);
        Assert.Equal(8, done.Today);
        Assert.True(done.DoneToday);
        Assert.Equal(snoozed.Id, done.Id);
    }

    [Fact]
    public async Task Collect_nudges_uses_local_time_and_saves()
    {
        await CreateWater("10:30"); // local time is 10:00
        Assert.Empty(await _service.CollectNudgesAsync());

        _clock.Advance(TimeSpan.FromMinutes(30));
        var due = Assert.Single(await _service.CollectNudgesAsync());
        Assert.Equal("Water", due.Name);
        Assert.Empty(await _service.CollectNudgesAsync());
        Assert.Equal(Today, _repo.Stored[0].NudgedOn);
    }

    [Fact]
    public async Task Nudger_pushes_the_habit_to_every_device()
    {
        var habit = await CreateWater("10:00");
        _repo.Stored[0].NudgedOn = null; // created exactly at 10:00, so it would otherwise start tomorrow
        var sender = new RecordingPush();
        using var push = new PushService(new MemoryRepository<PushDevice>(d => d.Subscription.Endpoint!), sender, _clock, NullLogger<PushService>.Instance);
        await push.SubscribeAsync(new PushSubscribeRequest(
            new PushSubscriptionInfo("https://push.example/1", null, new PushKeys("p", "a")), "Phone"));
        var nudger = new HabitNudger(_service, push, _clock, NullLogger<HabitNudger>.Instance);

        await nudger.TickAsync();

        var payload = Assert.Single(sender.Payloads);
        Assert.Contains("\"body\":\"0 / 8 glasses so far today\"", payload, StringComparison.Ordinal);
        Assert.Contains($"\"tag\":\"habit-{habit.Id}\"", payload, StringComparison.Ordinal);
        Assert.Contains("\"url\":\"/projects/habits\"", payload, StringComparison.Ordinal);
    }

    private sealed class RecordingPush : IPushSender
    {
        public List<string> Payloads { get; } = [];
        public string PublicKey => "key";

        public Task SendAsync(PushSubscriptionInfo subscription, string payload, CancellationToken cancellationToken)
        {
            Payloads.Add(payload);
            return Task.CompletedTask;
        }
    }
}