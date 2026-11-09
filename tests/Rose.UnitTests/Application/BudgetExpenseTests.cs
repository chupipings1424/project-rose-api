using Rose.Application.Tracker;
using Rose.Domain.Budget;
using Rose.Domain.Common;
using Rose.UnitTests.Support;

namespace Rose.UnitTests.Application;

public sealed class BudgetExpenseTests : IDisposable
{
    private readonly MemoryTrackerBook _book = new();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 11, 5, 4, 0, 0, TimeSpan.Zero));

    public void Dispose() => _book.Dispose();

    private BudgetService Service() => new(new MemoryTrackerStore(_book), new FixedCurrentUser(TestUsers.Alice), _clock);

    private BudgetLine AddLine(string group, string item, int month, double amount, int year = 2026, string section = BudgetRules.Expense)
    {
        var amounts = new double[12];
        amounts[month] = amount;
        var line = new BudgetLine(Guid.NewGuid().ToString(), year, section, group, item, amounts, amount, "");
        _book.AddBudgetLine(line);
        return line;
    }

    private double[] AmountsOf(string id) => _book.BudgetLines().Single(l => l.Id == id).Amounts;

    [Fact]
    public async Task Adds_to_the_existing_line_for_this_month_ignoring_case()
    {
        var food = AddLine("Daily Living", "Groceries", month: 10, amount: 100);

        var added = await Service().AddExpenseAsync(new AddExpenseRequest(" groceries ", null, "50.25", null));

        Assert.Equal(new ExpenseAdded(food.Id, "Groceries", "Daily Living", 2026, 10, 150.25, false), added);
        Assert.Equal(150.25, AmountsOf(food.Id)[10]);
    }

    [Fact]
    public async Task Creates_a_new_item_in_Other_when_no_group_is_given()
    {
        var added = await Service().AddExpenseAsync(new AddExpenseRequest("Iced coffee", "", "50", null));

        Assert.True(added.CreatedLine);
        var line = _book.BudgetLines().Single();
        Assert.Equal((2026, BudgetRules.Expense, "Other", "Iced coffee"), (line.Year, line.Section, line.Group, line.Item));
        Assert.Equal(50, line.Amounts[10]);
    }

    [Fact]
    public async Task Uses_the_month_it_was_spent_not_today()
    {
        // Logged offline on 31 October, sent on 5 November: it belongs to October.
        var taxi = AddLine("Transportation", "Taxi", month: 9, amount: 20);

        var added = await Service().AddExpenseAsync(new AddExpenseRequest("Taxi", null, "30", "2026-10-31"));

        Assert.Equal(9, added.Month);
        Assert.Equal([0, 0, 0, 0, 0, 0, 0, 0, 0, 50, 0, 0], AmountsOf(taxi.Id));
    }

    [Fact]
    public async Task Prefers_the_asked_group_when_the_item_is_in_several()
    {
        AddLine("Home", "Repairs", month: 10, amount: 0);
        var car = AddLine("Transportation", "Repairs", month: 10, amount: 0);

        var added = await Service().AddExpenseAsync(new AddExpenseRequest("Repairs", "transportation", "75", null));

        Assert.Equal(car.Id, added.LineId);
    }

    [Fact]
    public async Task Leaves_revenue_and_other_years_alone()
    {
        AddLine("Income", "Bonus", month: 10, amount: 0, section: BudgetRules.Revenue);
        AddLine("Other", "Bonus", month: 10, amount: 0, year: 2025);

        var added = await Service().AddExpenseAsync(new AddExpenseRequest("Bonus", null, "10", null));

        Assert.True(added.CreatedLine);
        Assert.Equal(3, _book.BudgetLines().Count);
    }

    [Theory]
    [InlineData("", "10", null)]
    [InlineData("Taxi", "0", null)]
    [InlineData("Taxi", "-5", null)]
    [InlineData("Taxi", "10", "not a date")]
    public async Task Rejects_bad_input(string item, string amount, string? date) =>
        await Assert.ThrowsAsync<ValidationException>(() => Service().AddExpenseAsync(new AddExpenseRequest(item, null, amount, date)));
}