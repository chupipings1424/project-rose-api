using Rose.Application.Savings;
using Rose.Domain.Common;
using Rose.Domain.Savings;
using Rose.UnitTests.Support;

namespace Rose.UnitTests.Application;

public class SavingsServiceTests
{
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.Zero));
    private readonly MemoryDataFile<List<SavingsGoal>> _file = new([]);
    private readonly SavingsService _service;

    public SavingsServiceTests() => _service = new SavingsService(_file, _clock);

    private SavingsGoalView CreateLaptop() => _service.Create(new GoalRequest(" Laptop ", "\U0001F4BB", 50_000, "2027-06-30"));

    [Fact]
    public void Create_saves_a_clean_goal()
    {
        var goal = CreateLaptop();

        Assert.Equal("Laptop", goal.Name);
        Assert.Equal(new DateOnly(2027, 6, 30), goal.Deadline);
        Assert.Equal(0m, goal.Saved);
        Assert.Single(_file.Value);
        Assert.Equal(1, _file.Writes);
    }

    [Fact]
    public void Deposits_and_withdrawals_change_what_is_saved()
    {
        var goal = CreateLaptop();

        _service.AddEntry(goal.Id, new EntryRequest("deposit", 1000, null, "Salary"));
        var after = _service.AddEntry(goal.Id, new EntryRequest("withdraw", 250, null, null));

        Assert.Equal(750m, after.Saved);
        Assert.Equal(2, after.Entries.Count);
        Assert.Equal(-250m, after.Entries[0].Amount);
    }

    [Fact]
    public void Removing_a_deposit_may_not_leave_a_negative_balance()
    {
        var goal = CreateLaptop();
        var deposit = _service.AddEntry(goal.Id, new EntryRequest("deposit", 100, "2026-11-01", null)).Entries[0];
        var withdrawal = _service.AddEntry(goal.Id, new EntryRequest("withdraw", 60, null, null)).Entries[0];

        Assert.Throws<ValidationException>(() => _service.RemoveEntry(goal.Id, deposit.Id));

        _service.RemoveEntry(goal.Id, withdrawal.Id);
        Assert.Equal(0m, _service.RemoveEntry(goal.Id, deposit.Id).Saved);
    }

    [Fact]
    public void Update_keeps_the_history()
    {
        var goal = CreateLaptop();
        _service.AddEntry(goal.Id, new EntryRequest(null, 500, null, null));

        var updated = _service.Update(goal.Id, new GoalRequest("Gaming laptop", "", 400, null));

        Assert.Equal(GoalStatus.Done, updated.Status);
        Assert.Single(updated.Entries);
    }

    [Fact]
    public void Unknown_ids_are_reported()
    {
        Assert.Equal("That goal no longer exists.", Assert.Throws<NotFoundException>(() => _service.Remove("nope")).Message);
        var goal = CreateLaptop();
        Assert.Equal("That entry no longer exists.",
            Assert.Throws<NotFoundException>(() => _service.RemoveEntry(goal.Id, "nope")).Message);
    }
}