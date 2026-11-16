using Rose.Application.Tracker;
using Rose.Domain.Budget;
using Rose.Domain.Tracker;
using Rose.UnitTests.Support;

namespace Rose.UnitTests.Application;

public sealed class PaymentBudgetSyncTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 11, 5);
    private readonly MemoryTrackerBook _book = new();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 11, 5, 4, 0, 0, TimeSpan.Zero));

    private Payment Pay(string id, string title, double amount, string date, string status = PaymentStatus.Paid, string due = "")
    {
        var payment = new Payment(id, title, amount, date, "9:00am", status, due, "");
        _book.AddPayment(payment);
        return payment;
    }

    private BudgetLine AddLine(string section, string group, string item, int month = 0, double amount = 0, int year = 2026)
    {
        var amounts = new double[12];
        amounts[month] = amount;
        var line = new BudgetLine(Guid.NewGuid().ToString(), year, section, group, item, amounts, amount, "");
        _book.AddBudgetLine(line);
        return line;
    }

    public void Dispose() => _book.Dispose();

    private PaymentTrackerService Service() => new(new MemoryTrackerStore(_book), new FixedCurrentUser(TestUsers.Alice), _clock);

    [Fact]
    public void A_paid_bill_creates_a_bills_line()
    {
        _book.AddAccount(new Account("Water", 0, "", "", "Monthly", 0, 0, "") { Kind = AccountKind.Bill });
        Pay("p1", "water", 480, "2026-03-10");

        PaymentBudgetSync.Reconcile(_book, Today);

        var line = _book.Line("water");
        Assert.Equal(BudgetRules.BillsGroup, line.Group);
        Assert.Equal(480, line.Amounts[2]);
    }

    [Fact]
    public void A_paid_bill_adds_on_top_of_the_budget_line_with_the_same_name()
    {
        _book.AddAccount(new Account("Water", 0, "", "", "Monthly", 0, 0, "") { Kind = AccountKind.Bill });
        AddLine(BudgetRules.Expense, "Home", "Water", month: 2, amount: 500);
        Pay("p1", "Water", 480, "2026-03-10");

        PaymentBudgetSync.Reconcile(_book, Today);

        var line = Assert.Single(_book.Lines);
        Assert.Equal(("Home", 980d), (line.Group, line.Amounts[2]));
    }

    [Fact]
    public void A_paid_payment_creates_a_loans_line_in_its_month()
    {
        Pay("p1", "Car", 100, "2026-03-10");

        var changed = PaymentBudgetSync.Reconcile(_book, Today);

        Assert.Equal(1, changed);
        var line = _book.Line("Car");
        Assert.Equal((2026, BudgetRules.Expense, BudgetRules.LoansGroup), (line.Year, line.Section, line.Group));
        Assert.Equal(100, line.Amounts[2]);
        Assert.Equal(100, line.Amounts.Sum());
        Assert.Equal(new BudgetMark(2026, 2, 100), _book.Payment("p1").InBudget);
    }

    [Fact]
    public void Payments_add_to_an_existing_expense_line_with_the_same_name()
    {
        AddLine(BudgetRules.Revenue, "Income", "Car", month: 2, amount: 7);
        AddLine(BudgetRules.Expense, "Home", "car", month: 2, amount: 40);
        Pay("p1", "Car", 100, "2026-03-10");
        Pay("p2", "Car", 50, "2026-03-25");

        PaymentBudgetSync.Reconcile(_book, Today);

        Assert.Equal(2, _book.Lines.Count);
        Assert.Equal(190, _book.Lines.Single(l => l.Section == BudgetRules.Expense).Amounts[2]);
        Assert.Equal(7, _book.Lines.Single(l => l.Section == BudgetRules.Revenue).Amounts[2]);
    }

    [Fact]
    public void Reconciling_again_changes_nothing()
    {
        Pay("p1", "Car", 100, "2026-03-10");
        PaymentBudgetSync.Reconcile(_book, Today);

        Assert.Equal(0, PaymentBudgetSync.Reconcile(_book, Today));
        Assert.Equal(100, _book.Line("Car").Amounts[2]);
    }

    [Fact]
    public void Unpaid_and_zero_payments_add_nothing()
    {
        Pay("p1", "Car", 100, "2026-03-10", PaymentStatus.Unpaid);
        Pay("p2", "Car", 0, "2026-03-10");

        Assert.Equal(0, PaymentBudgetSync.Reconcile(_book, Today));
        Assert.Empty(_book.Lines);
    }

    [Fact]
    public void The_month_comes_from_the_date_then_the_due_date_then_today()
    {
        Assert.Equal(new BudgetMark(2026, 0, 5), PaymentBudgetSync.Wanted(Pay("a", "X", 5, "2026-01-31", due: "2026-02-01"), Today));
        Assert.Equal(new BudgetMark(2027, 1, 5), PaymentBudgetSync.Wanted(Pay("b", "X", 5, "", due: "2027-02-01"), Today));
        Assert.Equal(new BudgetMark(2026, 10, 5), PaymentBudgetSync.Wanted(Pay("c", "X", 5, ""), Today));
    }

    [Fact]
    public void Unpaying_takes_back_exactly_what_was_added_even_after_a_manual_edit()
    {
        Pay("p1", "Car", 100, "2026-03-10");
        PaymentBudgetSync.Reconcile(_book, Today);
        _book.SetBudgetAmount(_book.Line("Car").Id, 2, 500);

        _book.SetPaymentStatus("p1", PaymentStatus.Unpaid);
        PaymentBudgetSync.Reconcile(_book, Today);

        Assert.Equal(400, _book.Line("Car").Amounts[2]);
        Assert.Null(_book.Payment("p1").InBudget);
    }

    [Fact]
    public void Taking_back_never_goes_below_zero_or_recreates_a_deleted_line()
    {
        Pay("p1", "Car", 100, "2026-03-10");
        Pay("p2", "Bike", 100, "2026-03-10");
        PaymentBudgetSync.Reconcile(_book, Today);
        _book.SetBudgetAmount(_book.Line("Car").Id, 2, 30);
        _book.DeleteBudgetLine(_book.Line("Bike").Id);

        _book.SetPaymentStatus("p1", PaymentStatus.Unpaid);
        _book.SetPaymentStatus("p2", PaymentStatus.Unpaid);
        PaymentBudgetSync.Reconcile(_book, Today);

        Assert.Equal(0, _book.Line("Car").Amounts[2]);
        Assert.DoesNotContain(_book.Lines, l => l.Item == "Bike");
    }

    [Fact]
    public async Task Logging_and_unpaying_a_payment_updates_the_budget()
    {
        _book.AddAccount(new Account("Car", 1000, "", "", "Monthly", 0, 0, ""));
        var service = Service();

        var created = await service.AddPaymentAsync(new AddPaymentRequest("Car", "250", "2026-11-02", "10:00", "Paid", "", ""));
        Assert.Equal(250, _book.Line("Car").Amounts[10]);

        await service.SetPaymentStatusAsync(created.Id, new UpdatePaymentRequest("Unpaid", "Car"));
        Assert.Equal(0, _book.Line("Car").Amounts[10]);
    }

    [Fact]
    public async Task Deleting_a_payment_or_title_takes_its_amount_out_of_the_budget()
    {
        _book.AddAccount(new Account("Car", 1000, "", "", "Monthly", 0, 0, ""));
        var service = Service();
        var first = await service.AddPaymentAsync(new AddPaymentRequest("Car", "100", "2026-11-02", "", "Paid", "", ""));
        await service.AddPaymentAsync(new AddPaymentRequest("Car", "40", "2026-11-03", "", "Paid", "", ""));

        await service.DeletePaymentAsync(first.Id);
        Assert.Equal(40, _book.Line("Car").Amounts[10]);

        await service.DeleteAccountAsync("Car");
        Assert.Equal(0, _book.Line("Car").Amounts[10]);
    }

    [Fact]
    public async Task Payments_that_were_paid_before_the_sync_existed_are_added_once_on_the_next_read()
    {
        Pay("old", "Car", 75, "2026-06-01");
        var service = Service();

        await service.HomeStatsAsync();
        await service.HomeStatsAsync();

        Assert.Equal(75, _book.Line("Car").Amounts[5]);
        Assert.Equal(1, _book.Saves);
    }

    [Fact]
    public void Imported_payments_are_marked_counted_without_touching_the_budget()
    {
        var imported = Pay("i1", "Car", 75, "2026-06-01");

        PaymentBudgetSync.MarkCounted(_book, [imported], Today);

        Assert.Empty(_book.Lines);
        Assert.Equal(0, PaymentBudgetSync.Reconcile(_book, Today));
    }
}