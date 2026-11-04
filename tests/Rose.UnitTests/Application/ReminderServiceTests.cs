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
        _service.CreateAsync(TestUsers.Alice, new ReminderRequest("Pay rent", (Start + delay).ToString("O"), repeat, null));

    [Fact]
    public async Task Create_saves_and_lists_soonest_first()
    {
        await CreateIn(TimeSpan.FromHours(2));
        var sooner = await CreateIn(TimeSpan.FromHours(1));

        Assert.Equal(sooner.Id, (await _service.ListAsync(TestUsers.Alice))[0].Id);
        Assert.Equal(2, _repo.Stored.Count);
    }

    [Fact]
    public async Task List_returns_copies_that_cannot_change_the_store()
    {
        await CreateIn(TimeSpan.FromHours(1));

        (await _service.ListAsync(TestUsers.Alice))[0].Text = "hacked";

        Assert.Equal("Pay rent", (await _service.ListAsync(TestUsers.Alice))[0].Text);
    }

    [Fact]
    public async Task Tick_rings_due_reminders_once()
    {
        await CreateIn(TimeSpan.FromMinutes(1));

        _clock.Advance(TimeSpan.FromMinutes(2));
        var fired = await _service.TickAsync(TestUsers.Alice);
        var again = await _service.TickAsync(TestUsers.Alice);

        Assert.Single(fired);
        Assert.Empty(again);
        Assert.True((await _service.ListAsync(TestUsers.Alice))[0].Ringing);
    }

    [Fact]
    public async Task Snooze_rings_again_later()
    {
        var reminder = await CreateIn(TimeSpan.FromMinutes(1));
        _clock.Advance(TimeSpan.FromMinutes(2));
        await _service.TickAsync(TestUsers.Alice);

        var snoozed = await _service.SnoozeAsync(TestUsers.Alice, reminder.Id, null);
        Assert.False(snoozed.Ringing);
        Assert.Equal(_clock.Now.AddMinutes(10), snoozed.SnoozedUntil);

        _clock.Advance(TimeSpan.FromMinutes(11));
        Assert.Single(await _service.TickAsync(TestUsers.Alice));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1441)]
    public async Task Snooze_limits_the_delay(double minutes)
    {
        var reminder = await CreateIn(TimeSpan.FromMinutes(1));

        var error = await Assert.ThrowsAsync<ValidationException>(() => _service.SnoozeAsync(TestUsers.Alice, reminder.Id, minutes));
        Assert.Equal("Snooze between 1 minute and 24 hours.", error.Message);
    }

    [Fact]
    public async Task Complete_finishes_a_one_off_but_keeps_a_repeating_one()
    {
        var once = await CreateIn(TimeSpan.FromMinutes(1));
        var daily = await CreateIn(TimeSpan.FromMinutes(1), RepeatRule.Daily);

        Assert.True((await _service.CompleteAsync(TestUsers.Alice, once.Id)).Done);
        Assert.False((await _service.CompleteAsync(TestUsers.Alice, daily.Id)).Done);
    }

    [Fact]
    public async Task Changing_the_time_lets_a_fired_reminder_ring_again()
    {
        var reminder = await CreateIn(TimeSpan.FromMinutes(1));
        _clock.Advance(TimeSpan.FromMinutes(2));
        await _service.TickAsync(TestUsers.Alice);

        await _service.UpdateAsync(TestUsers.Alice, reminder.Id, new ReminderRequest("Pay rent", _clock.Now.AddMinutes(5).ToString("O"), "none", null));
        _clock.Advance(TimeSpan.FromMinutes(6));

        Assert.Single(await _service.TickAsync(TestUsers.Alice));
    }

    [Fact]
    public async Task Unknown_ids_are_reported()
    {
        var error = await Assert.ThrowsAsync<NotFoundException>(() => _service.RemoveAsync(TestUsers.Alice, "nope"));
        Assert.Equal("That reminder no longer exists.", error.Message);
    }

    [Fact]
    public async Task Each_user_sees_only_their_own_reminders()
    {
        var alices = await CreateIn(TimeSpan.FromMinutes(1));

        Assert.Empty(await _service.ListAsync(TestUsers.Bob));
        await Assert.ThrowsAsync<NotFoundException>(() => _service.SnoozeAsync(TestUsers.Bob, alices.Id, null));
        await Assert.ThrowsAsync<NotFoundException>(() => _service.RemoveAsync(TestUsers.Bob, alices.Id));

        _clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Empty(await _service.TickAsync(TestUsers.Bob));
        Assert.Single(await _service.TickAsync(TestUsers.Alice));
        Assert.Empty(_repo.StoredFor(TestUsers.Bob));
    }

    [Fact]
    public async Task A_ring_after_a_snooze_is_marked_as_one()
    {
        var reminder = await CreateIn(TimeSpan.FromMinutes(1));
        _clock.Advance(TimeSpan.FromMinutes(2));
        Assert.False(Assert.Single(await _service.TickAsync(TestUsers.Alice)).IsSnoozeRing);

        await _service.SnoozeAsync(TestUsers.Alice, reminder.Id, 5);
        _clock.Advance(TimeSpan.FromMinutes(6));

        Assert.True(Assert.Single(await _service.TickAsync(TestUsers.Alice)).IsSnoozeRing);
    }

    [Fact]
    public async Task A_failed_save_reloads_from_the_store()
    {
        var reminder = await CreateIn(TimeSpan.FromHours(1));
        using var failing = new ReminderService(new FailingRepository(_repo), _clock);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            failing.UpdateAsync(TestUsers.Alice, reminder.Id, new ReminderRequest("Changed", Start.AddHours(3).ToString("O"), "none", null)));

        Assert.Equal("Pay rent", (await failing.ListAsync(TestUsers.Alice))[0].Text);
    }

    /// <summary>Reads work, every write fails - like a database that went away mid-request.</summary>
    private sealed class FailingRepository(MemoryRepository<Reminder> inner) : Rose.Application.Abstractions.IRepository<Reminder>
    {
        public async Task<List<Reminder>> LoadAllAsync(Guid owner, CancellationToken cancellationToken = default) =>
            [.. (await inner.LoadAllAsync(owner, cancellationToken)).Select(r => r.Copy())];

        public Task SaveAsync(Guid owner, IReadOnlyCollection<Reminder> items, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("database down");

        public Task DeleteAsync(Guid owner, string id, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("database down");
    }
}