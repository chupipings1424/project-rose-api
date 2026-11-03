using Microsoft.Extensions.Logging.Abstractions;
using Rose.Application.Abstractions;
using Rose.Application.Habits;
using Rose.Application.Reminders;
using Rose.Domain.Common;
using Rose.Domain.Habits;
using Rose.UnitTests.Support;

namespace Rose.UnitTests.Application;

public class HabitServiceTests
{
    // 09:00 UTC = 10:00 in Berlin (CET).
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero), Zones.Berlin);
    private readonly MemoryDataFile<List<Habit>> _file = new([]);
    private readonly HabitService _service;

    public HabitServiceTests() => _service = new HabitService(_file, _clock);

    private static readonly DateOnly Today = new(2026, 11, 10);

    private HabitView CreateWater(string? remindAt = null) =>
        _service.Create(new HabitRequest("Water", "", "count", 8, "glasses", remindAt));

    [Fact]
    public void Create_saves_a_clean_habit()
    {
        var habit = CreateWater("21:00");

        Assert.Equal("Water", habit.Name);
        Assert.Equal("21:00", habit.RemindAt);
        Assert.Equal(0, habit.CurrentStreak);
        Assert.Single(_file.Value);
        Assert.Equal(1, _file.Writes);
    }

    [Fact]
    public void Setting_a_day_is_idempotent_and_updates_the_streak()
    {
        var habit = CreateWater();

        _service.SetDay(habit.Id, "2026-11-09", 8);
        _service.SetDay(habit.Id, "2026-11-10", 8);
        var after = _service.SetDay(habit.Id, "2026-11-10", 8);

        Assert.Equal(2, after.CurrentStreak);
        Assert.True(after.DoneToday);
        Assert.Equal(2, _file.Value[0].Log.Count);
    }

    [Fact]
    public void Old_days_cannot_be_changed()
    {
        var habit = CreateWater();
        Assert.Throws<ValidationException>(() => _service.SetDay(habit.Id, "2026-11-03", 1));
    }

    [Fact]
    public void Unknown_habits_are_not_found() =>
        Assert.Throws<NotFoundException>(() => _service.SetDay("nope", "2026-11-10", 1));

    [Fact]
    public void Nudge_buttons_complete_dismiss_and_snooze()
    {
        var habit = CreateWater("11:00");

        var snoozed = _service.SnoozeNudge(habit.Id, 15);
        Assert.Equal(_clock.Now.AddMinutes(15), _file.Value[0].SnoozedUntil);
        Assert.Equal(Today, _file.Value[0].NudgedOn);

        _service.DismissNudge(habit.Id);
        Assert.Null(_file.Value[0].SnoozedUntil);

        var done = _service.CompleteToday(habit.Id);
        Assert.Equal(8, done.Today);
        Assert.True(done.DoneToday);
        Assert.Equal(snoozed.Id, done.Id);
    }

    [Fact]
    public void Collect_nudges_uses_local_time_and_saves()
    {
        CreateWater("10:30"); // local time is 10:00
        Assert.Empty(_service.CollectNudges());

        _clock.Advance(TimeSpan.FromMinutes(30));
        var due = Assert.Single(_service.CollectNudges());
        Assert.Equal("Water", due.Name);
        Assert.Empty(_service.CollectNudges());
        Assert.Equal(Today, _file.Value[0].NudgedOn);
    }

    [Fact]
    public async Task Nudger_shows_the_popup_with_the_habit_callback()
    {
        var habit = CreateWater("10:00");
        _file.Value[0].NudgedOn = null; // created exactly at 10:00, so it would otherwise start tomorrow
        var alarm = new RecordingAlarm();
        var push = new PushService(new MemoryDataFile<List<PushDevice>>([]), new NoPush(), _clock, NullLogger<PushService>.Instance);
        var nudger = new HabitNudger(_service, push, alarm, _clock, NullLogger<HabitNudger>.Instance);

        await nudger.TickAsync();

        var shown = Assert.Single(alarm.Shown);
        Assert.Equal($"/api/habits/items/{habit.Id}/nudge", shown.CallbackPath);
        Assert.Equal("HABIT CHECK-IN", shown.Header);
        Assert.Equal("0 / 8 glasses so far today", shown.Subtitle);
    }

    private sealed class RecordingAlarm : IDesktopAlarm
    {
        public List<DesktopAlarmRequest> Shown { get; } = [];
        public bool Enabled => true;
        public void Show(DesktopAlarmRequest request) => Shown.Add(request);
    }

    private sealed class NoPush : IPushSender
    {
        public string PublicKey => "key";
        public Task SendAsync(PushSubscriptionInfo subscription, string payload, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}