using Rose.Application.Savings;
using Rose.Domain.Common;
using Rose.Domain.Savings;
using Rose.UnitTests.Support;

namespace Rose.UnitTests.Application;

public sealed class SavingsServiceTests : IDisposable
{
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.Zero));
    private readonly MemoryRepository<SavingsGoal> _repo = new(g => g.Id);
    private readonly SavingsService _service;

    public SavingsServiceTests() => _service = new SavingsService(_repo, _clock);

    public void Dispose() => _service.Dispose();

    private Task<SavingsGoalView> CreateLaptop() =>
        _service.CreateAsync(new GoalRequest(" Laptop ", "\U0001F4BB", 50_000, "2027-06-30"));

    [Fact]
    public async Task Create_saves_a_clean_goal()
    {
        var goal = await CreateLaptop();

        Assert.Equal("Laptop", goal.Name);
        Assert.Equal(new DateOnly(2027, 6, 30), goal.Deadline);
        Assert.Equal(0m, goal.Saved);
        Assert.Single(_repo.Stored);
        Assert.Equal(1, _repo.Writes);
    }

    [Fact]
    public async Task Deposits_and_withdrawals_change_what_is_saved()
    {
        var goal = await CreateLaptop();

        await _service.AddEntryAsync(goal.Id, new EntryRequest("deposit", 1000, null, "Salary"));
        var after = await _service.AddEntryAsync(goal.Id, new EntryRequest("withdraw", 250, null, null));

        Assert.Equal(750m, after.Saved);
        Assert.Equal(2, after.Entries.Count);
        Assert.Equal(-250m, after.Entries[0].Amount);
    }

    [Fact]
    public async Task Removing_a_deposit_may_not_leave_a_negative_balance()
    {
        var goal = await CreateLaptop();
        var deposit = (await _service.AddEntryAsync(goal.Id, new EntryRequest("deposit", 100, "2026-11-01", null))).Entries[0];
        var withdrawal = (await _service.AddEntryAsync(goal.Id, new EntryRequest("withdraw", 60, null, null))).Entries[0];

        await Assert.ThrowsAsync<ValidationException>(() => _service.RemoveEntryAsync(goal.Id, deposit.Id));

        await _service.RemoveEntryAsync(goal.Id, withdrawal.Id);
        Assert.Equal(0m, (await _service.RemoveEntryAsync(goal.Id, deposit.Id)).Saved);
    }

    [Fact]
    public async Task Update_keeps_the_history()
    {
        var goal = await CreateLaptop();
        await _service.AddEntryAsync(goal.Id, new EntryRequest(null, 500, null, null));

        var updated = await _service.UpdateAsync(goal.Id, new GoalRequest("Gaming laptop", "", 400, null));

        Assert.Equal(GoalStatus.Done, updated.Status);
        Assert.Single(updated.Entries);
    }

    [Fact]
    public async Task Unknown_ids_are_reported()
    {
        Assert.Equal("That goal no longer exists.",
            (await Assert.ThrowsAsync<NotFoundException>(() => _service.RemoveAsync("nope"))).Message);
        var goal = await CreateLaptop();
        Assert.Equal("That entry no longer exists.",
            (await Assert.ThrowsAsync<NotFoundException>(() => _service.RemoveEntryAsync(goal.Id, "nope"))).Message);
    }
}