using System.Globalization;
using Rose.Application.Abstractions;
using Rose.Application.Common;
using Rose.Domain.Budget;
using Rose.Domain.Common;
using Rose.Domain.Tracker;

namespace Rose.Application.Tracker;

/// <summary>Titles (instalment plans) and their payments.</summary>
public sealed class PaymentTrackerService(ITrackerWorkbookStore store, TimeProvider clock)
{
    public async Task<HomeStats> HomeStatsAsync(CancellationToken ct = default)
    {
        using var book = await store.OpenAsync(ct);
        var accounts = book.Accounts();
        var payments = book.Payments();
        var today = clock.Today();

        double total = 0;
        int paidCount = 0, paymentCount = 0;
        string? nextDue = null;
        foreach (var account in accounts)
        {
            var info = AccountSummary.For(account.Title, accounts, payments, today);
            total += info.TotalAmount;
            paidCount += info.PaidCount;
            paymentCount += info.PaymentCount;
            if (info.NextDueDate.Length > 0 && (nextDue is null || string.CompareOrdinal(info.NextDueDate, nextDue) < 0))
                nextDue = info.NextDueDate;
        }

        var budget = BudgetView.Build(today.Year, book.BudgetLines(), today.Year);
        return new HomeStats(
            store.WorkbookPath,
            new HomePayments(accounts.Count, Coerce.Round(total), paidCount, paymentCount, nextDue),
            new HomeBudget(budget.Year, budget.TotalRevenue, budget.TotalExpenses, budget.Net, budget.LineCount));
    }

    public async Task<IReadOnlyList<Account>> AccountsAsync(CancellationToken ct = default)
    {
        using var book = await store.OpenAsync(ct);
        return book.Accounts();
    }

    /// <summary>Without instalments: just the title. With instalments: the title plus one Unpaid payment per instalment.</summary>
    public async Task<IReadOnlyList<Account>> CreateAccountAsync(CreateAccountRequest request, CancellationToken ct = default)
    {
        var title = (request.Title ?? "").Trim();
        if (title.Length == 0) throw new ValidationException("Title is required.");
        var amount = request.Amount ?? request.TotalAmount;
        var frequency = string.IsNullOrEmpty(request.Frequency) ? "Monthly" : request.Frequency;

        using var book = await store.OpenAsync(ct);
        if (string.IsNullOrEmpty(request.Instalments))
        {
            EnsureNewTitle(book, title);
            book.AddAccount(new Account(
                title, Coerce.ToDouble(amount), Coerce.Iso(request.StartDate), Coerce.Iso(request.DueDate),
                frequency, 0, 0, request.Notes ?? ""));
        }
        else
        {
            var plan = Schedule.Build(amount, request.Instalments, frequency, request.StartDate, request.AmountMode, clock.Today());
            EnsureNewTitle(book, title);
            book.AddAccount(new Account(
                title, plan.Total, plan.FirstDue, plan.FirstDue, plan.Frequency, plan.Instalments, plan.PerPayment,
                request.Notes ?? ""));
            for (var i = 0; i < plan.Instalments; i++)
            {
                book.AddPayment(new Payment(
                    NewId(), title, plan.Amounts[i], plan.Dates[i], Coerce.ToTimeText("09:00"), PaymentStatus.Unpaid,
                    plan.Dates[i], $"Instalment {i + 1} of {plan.Instalments}"));
            }
        }
        book.Save();
        return book.Accounts();
    }

    private static void EnsureNewTitle(ITrackerWorkbook book, string title)
    {
        if (book.Accounts().Any(a => string.Equals(a.Title, title, StringComparison.OrdinalIgnoreCase)))
            throw new ConflictException($"An account titled '{title}' already exists.");
    }

    public SchedulePreview Preview(SchedulePreviewRequest request) =>
        Schedule.Build(
            request.Amount ?? request.TotalAmount,
            request.Instalments,
            string.IsNullOrEmpty(request.Frequency) ? "Monthly" : request.Frequency,
            request.StartDate,
            request.AmountMode,
            clock.Today());

    public async Task<AccountSummary> SummaryAsync(string title, CancellationToken ct = default)
    {
        using var book = await store.OpenAsync(ct);
        return AccountSummary.For(title, book.Accounts(), book.Payments(), clock.Today());
    }

    public async Task<PaymentCreated> AddPaymentAsync(AddPaymentRequest request, CancellationToken ct = default)
    {
        var title = (request.Title ?? "").Trim();
        if (title.Length == 0) throw new ValidationException("Title is required.");
        var now = clock.GetLocalNow();
        var status = string.IsNullOrEmpty(request.Status) ? PaymentStatus.Paid : request.Status;
        var payment = new Payment(
            NewId(),
            title,
            Coerce.ToDouble(request.Amount),
            Coerce.Iso(string.IsNullOrEmpty(request.Date) ? Coerce.IsoDate(DateOnly.FromDateTime(now.DateTime)) : request.Date),
            Coerce.ToTimeText(string.IsNullOrEmpty(request.Time) ? now.ToString("HH:mm", CultureInfo.InvariantCulture) : request.Time),
            PaymentStatus.IsKnown(status) ? status : PaymentStatus.Unpaid,
            Coerce.Iso(request.DueDate),
            request.Note ?? "");

        using var book = await store.OpenAsync(ct);
        book.AddPayment(payment);
        book.Save();
        return new PaymentCreated(payment.Id, AccountSummary.For(title, book.Accounts(), book.Payments(), clock.Today()));
    }

    public async Task<PaymentUpdated> SetPaymentStatusAsync(string id, UpdatePaymentRequest request, CancellationToken ct = default)
    {
        if (!PaymentStatus.IsKnown(request.Status))
            throw new ValidationException("status must be one of ['Paid', 'Unpaid']");
        using var book = await store.OpenAsync(ct);
        if (!book.SetPaymentStatus(id, request.Status!)) throw new NotFoundException("Payment not found.");
        book.Save();
        return new PaymentUpdated(true, AccountSummary.For(request.Title ?? "", book.Accounts(), book.Payments(), clock.Today()));
    }

    public async Task DeletePaymentAsync(string id, CancellationToken ct = default)
    {
        using var book = await store.OpenAsync(ct);
        if (!book.DeletePayment(id)) throw new NotFoundException("Payment not found.");
        book.Save();
    }

    /// <summary>Deletes a title and every payment recorded under it, in one save.</summary>
    public async Task<AccountDeleted> DeleteAccountAsync(string title, CancellationToken ct = default)
    {
        using var book = await store.OpenAsync(ct);
        var removed = book.DeleteAccount(title) ?? throw new NotFoundException("Title not found.");
        book.Save();
        return new AccountDeleted(true, removed, book.Accounts());
    }

    public ServerNow Now()
    {
        var now = clock.GetLocalNow();
        return new ServerNow(
            now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            now.ToString("HH:mm", CultureInfo.InvariantCulture));
    }

    internal static string NewId() => Guid.NewGuid().ToString();
}