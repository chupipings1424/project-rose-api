using Rose.Application.Habits;
using Rose.Application.Reminders;
using Rose.Application.Today;
using Rose.Domain.Budget;
using Rose.Domain.Habits;
using Rose.Domain.Reminders;
using Rose.Domain.Tracker;
using Rose.UnitTests.Support;

namespace Rose.UnitTests.Application;

public sealed class TodayServiceTests : IDisposable
{
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 11, 5, 4, 0, 0, TimeSpan.Zero));
    private readonly MemoryTrackerBook _book = new();
    private readonly ReminderService _reminders;
    private readonly HabitService _habits;
    private readonly TodayService _service;

    public TodayServiceTests()
    {
        _reminders = new ReminderService(new MemoryRepository<Reminder>(r => r.Id), _clock);
        _habits = new HabitService(new MemoryRepository<Habit>(h => h.Id), _clock);
        _service = new TodayService(new MemoryTrackerStore(_book), _reminders, _habits, new FixedCurrentUser(TestUsers.Alice), _clock);
    }

    public void Dispose()
    {
        _reminders.Dispose();
        _habits.Dispose();
        _book.Dispose();
    }

    private void Payment(string id, string due, string status = PaymentStatus.Unpaid) =>
        _book.AddPayment(new Payment(id, "Car", 100, due, "9:00am", status, due, ""));

    [Fact]
    public async Task Lists_overdue_and_soon_due_unpaid_payments_earliest_first()
    {
        Payment("late", "2026-11-01");
        Payment("soon", "2026-11-12");
        Payment("later", "2026-11-13");
        Payment("paid", "2026-11-06", PaymentStatus.Paid);

        var today = await _service.GetAsync();

        Assert.Equal("2026-11-05", today.Date);
        Assert.Equal(["late", "soon"], today.Payments.Select(p => p.Id));
        Assert.Equal([-4, 7], today.Payments.Select(p => p.DaysUntilDue));
    }

    [Fact]
    public async Task Shows_this_months_budget_including_paid_payments()
    {
        var income = new double[12];
        income[10] = 1000;
        _book.AddBudgetLine(new BudgetLine("r", 2026, BudgetRules.Revenue, "Income", "Salary", income, 1000, ""));
        Payment("paid", "2026-11-02", PaymentStatus.Paid);

        var budget = (await _service.GetAsync()).Budget;

        Assert.Equal((2026, 10, "Nov"), (budget.Year, budget.Month, budget.MonthName));
        Assert.Equal((1000, 100, 900), (budget.Revenue, budget.Expenses, budget.Left));
    }

    [Fact]
    public async Task Lists_reminders_due_by_today_and_every_habit()
    {
        await _reminders.CreateAsync(TestUsers.Alice, new ReminderRequest("Call bank", "2026-11-05T15:00:00Z", "none", null));
        await _reminders.CreateAsync(TestUsers.Alice, new ReminderRequest("Dentist", "2026-11-06T09:00:00Z", "none", null));
        var water = await _habits.CreateAsync(TestUsers.Alice, new HabitRequest("Water", "", "count", 8, "glasses", null));
        await _habits.SetDayAsync(TestUsers.Alice, water.Id, "2026-11-05", 3);

        var today = await _service.GetAsync();

        var reminder = Assert.Single(today.Reminders);
        Assert.Equal("Call bank", reminder.Text);
        Assert.False(reminder.Overdue);
        var habit = Assert.Single(today.Habits);
        Assert.Equal((3, 8, false), (habit.Done, habit.Target, habit.DoneToday));
    }
}