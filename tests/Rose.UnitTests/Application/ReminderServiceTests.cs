using Rose.Application.Reminders;
using Rose.Domain.Common;
using Rose.Domain.Reminders;
using Rose.UnitTests.Support;

namespace Rose.UnitTests.Application;

public class ReminderServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 12, 6, 0, 0, TimeSpan.Zero);
    private readonly FakeClock _clock = new(Start, Zones.Berlin);
    private readonly MemoryDataFile<List<Reminder>> _file = new([]);
    private readonly ReminderService _service;

    public ReminderServiceTests() => _service = new ReminderService(_file, _clock);

    private Reminder CreateIn(TimeSpan delay, string repeat = RepeatRule.None) =>
        _service.Create(new ReminderRequest("Pay rent", (Start + delay).ToString("O"), repeat, null));

    [Fact]
    public void Create_saves_and_lists_soonest_first()
    {
        CreateIn(TimeSpan.FromHours(2));
        var sooner = CreateIn(TimeSpan.FromHours(1));

        Assert.Equal(sooner.Id, _service.List()[0].Id);
        Assert.Equal(2, _file.Value.Count);
    }

    [Fact]
    public void List_returns_copies_that_cannot_change_the_store()
    {
        CreateIn(TimeSpan.FromHours(1));

        _service.List()[0].Text = "hacked";

        Assert.Equal("Pay rent", _service.List()[0].Text);
    }

    [Fact]
    public void Tick_rings_due_reminders_once()
    {
        CreateIn(TimeSpan.FromMinutes(1));

        _clock.Advance(TimeSpan.FromMinutes(2));
        var fired = _service.Tick();
        var again = _service.Tick();

        Assert.Single(fired);
        Assert.Empty(again);
        Assert.True(_service.List()[0].Ringing);
    }

    [Fact]
    public void Snooze_rings_again_later()
    {
        var reminder = CreateIn(TimeSpan.FromMinutes(1));
        _clock.Advance(TimeSpan.FromMinutes(2));
        _service.Tick();

        var snoozed = _service.Snooze(reminder.Id, null);
        Assert.False(snoozed.Ringing);
        Assert.Equal(_clock.Now.AddMinutes(10), snoozed.SnoozedUntil);

        _clock.Advance(TimeSpan.FromMinutes(11));
        Assert.Single(_service.Tick());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1441)]
    public void Snooze_limits_the_delay(double minutes)
    {
        var reminder = CreateIn(TimeSpan.FromMinutes(1));

        var error = Assert.Throws<ValidationException>(() => _service.Snooze(reminder.Id, minutes));
        Assert.Equal("Snooze between 1 minute and 24 hours.", error.Message);
    }

    [Fact]
    public void Complete_finishes_a_one_off_but_keeps_a_repeating_one()
    {
        var once = CreateIn(TimeSpan.FromMinutes(1));
        var daily = CreateIn(TimeSpan.FromMinutes(1), RepeatRule.Daily);

        Assert.True(_service.Complete(once.Id).Done);
        Assert.False(_service.Complete(daily.Id).Done);
    }

    [Fact]
    public void Changing_the_time_lets_a_fired_reminder_ring_again()
    {
        var reminder = CreateIn(TimeSpan.FromMinutes(1));
        _clock.Advance(TimeSpan.FromMinutes(2));
        _service.Tick();

        _service.Update(reminder.Id, new ReminderRequest("Pay rent", _clock.Now.AddMinutes(5).ToString("O"), "none", null));
        _clock.Advance(TimeSpan.FromMinutes(6));

        Assert.Single(_service.Tick());
    }

    [Fact]
    public void Unknown_ids_are_reported()
    {
        var error = Assert.Throws<NotFoundException>(() => _service.Remove("nope"));
        Assert.Equal("That reminder no longer exists.", error.Message);
    }
}