using Rose.Application.Reminders;
using Rose.Domain.Common;
using Rose.Domain.Reminders;
using Rose.UnitTests.Support;

namespace Rose.UnitTests.Application;

public sealed class ReminderServiceTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 10, 12, 6, 0, 0, TimeSpan.Zero);
    private readonly FakeClock _clock = new(Start, Zones.Berlin);
    private readonly MemoryRepository<Reminder> _repo = new(r => r.Id);
    private readonly ReminderService _service;

    public ReminderServiceTests() => _service = new ReminderService(_repo, _clock);

    public void Dispose() => _service.Dispose();

    private Task<Reminder> CreateIn(TimeSpan delay, string repeat = RepeatRule.None) =>
        _service.CreateAsync(new ReminderRequest("Pay rent", (Start + delay).ToString("O"), repeat, null));

    [Fact]
    public async Task Create_saves_and_lists_soonest_first()
    {
        await CreateIn(TimeSpan.FromHours(2));
        var sooner = await CreateIn(TimeSpan.FromHours(1));

        Assert.Equal(sooner.Id, (await _service.ListAsync())[0].Id);
        Assert.Equal(2, _repo.Stored.Count);
    }

    [Fact]
    public async Task List_returns_copies_that_cannot_change_the_store()
    {
        await CreateIn(TimeSpan.FromHours(1));

        (await _service.ListAsync())[0].Text = "hacked";

        Assert.Equal("Pay rent", (await _service.ListAsync())[0].Text);
    }

    [Fact]
    public async Task Tick_rings_due_reminders_once()
    {
        await CreateIn(TimeSpan.FromMinutes(1));

        _clock.Advance(TimeSpan.FromMinutes(2));
        var fired = await _service.TickAsync();
        var again = await _service.TickAsync();

        Assert.Single(fired);
        Assert.Empty(again);
        Assert.True((await _service.ListAsync())[0].Ringing);
    }

    [Fact]
    public async Task Snooze_rings_again_later()
    {
        var reminder = await CreateIn(TimeSpan.FromMinutes(1));
        _clock.Advance(TimeSpan.FromMinutes(2));
        await _service.TickAsync();

        var snoozed = await _service.SnoozeAsync(reminder.Id, null);
        Assert.False(snoozed.Ringing);
        Assert.Equal(_clock.Now.AddMinutes(10), snoozed.SnoozedUntil);

        _clock.Advance(TimeSpan.FromMinutes(11));
        Assert.Single(await _service.TickAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1441)]
    public async Task Snooze_limits_the_delay(double minutes)
    {
        var reminder = await CreateIn(TimeSpan.FromMinutes(1));

        var error = await Assert.ThrowsAsync<ValidationException>(() => _service.SnoozeAsync(reminder.Id, minutes));
        Assert.Equal("Snooze between 1 minute and 24 hours.", error.Message);
    }

    [Fact]
    public async Task Complete_finishes_a_one_off_but_keeps_a_repeating_one()
    {
        var once = await CreateIn(TimeSpan.FromMinutes(1));
        var daily = await CreateIn(TimeSpan.FromMinutes(1), RepeatRule.Daily);

        Assert.True((await _service.CompleteAsync(once.Id)).Done);
        Assert.False((await _service.CompleteAsync(daily.Id)).Done);
    }

    [Fact]
    public async Task Changing_the_time_lets_a_fired_reminder_ring_again()
    {
        var reminder = await CreateIn(TimeSpan.FromMinutes(1));
        _clock.Advance(TimeSpan.FromMinutes(2));
        await _service.TickAsync();

        await _service.UpdateAsync(reminder.Id, new ReminderRequest("Pay rent", _clock.Now.AddMinutes(5).ToString("O"), "none", null));
        _clock.Advance(TimeSpan.FromMinutes(6));

        Assert.Single(await _service.TickAsync());
    }

    [Fact]
    public async Task Unknown_ids_are_reported()
    {
        var error = await Assert.ThrowsAsync<NotFoundException>(() => _service.RemoveAsync("nope"));
        Assert.Equal("That reminder no longer exists.", error.Message);
    }

    [Fact]
    public async Task A_failed_save_reloads_from_the_store()
    {
        var reminder = await CreateIn(TimeSpan.FromHours(1));
        using var failing = new ReminderService(new FailingRepository(_repo), _clock);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            failing.UpdateAsync(reminder.Id, new ReminderRequest("Changed", Start.AddHours(3).ToString("O"), "none", null)));

        Assert.Equal("Pay rent", (await failing.ListAsync())[0].Text);
    }

    /// <summary>Reads work, every write fails - like a database that went away mid-request.</summary>
    private sealed class FailingRepository(MemoryRepository<Reminder> inner) : Rose.Application.Abstractions.IRepository<Reminder>
    {
        public async Task<List<Reminder>> LoadAllAsync(CancellationToken cancellationToken = default) =>
            [.. (await inner.LoadAllAsync(cancellationToken)).Select(r => r.Copy())];

        public Task SaveAsync(IReadOnlyCollection<Reminder> items, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("database down");

        public Task DeleteAsync(string id, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("database down");
    }
}