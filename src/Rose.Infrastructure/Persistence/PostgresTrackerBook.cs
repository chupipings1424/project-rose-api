using Microsoft.EntityFrameworkCore;
using Rose.Application.Abstractions;
using Rose.Domain.Budget;
using Rose.Domain.Common;
using Rose.Domain.Tracker;

namespace Rose.Infrastructure.Persistence;

/// <summary>
/// A unit of work over the tracker tables: rows are loaded once, changed in memory (tracked by
/// EF Core) and written in one transaction by <see cref="SaveAsync"/>. Disposing without saving
/// discards every change. Holds one user's rows; new rows are stamped with that owner. The reads apply the same clean-up rules the Excel version did.
/// </summary>
internal sealed class PostgresTrackerBook(
    RoseDbContext db, Guid owner, List<AccountRow> accounts, List<PaymentRow> payments, List<BudgetLineRow> lines, Action release)
    : ITrackerWorkbook
{
    private bool disposed;

    // ------------------------------------------------------------------ reads

    public IReadOnlyList<Account> Accounts() =>
        [.. accounts.Select(a => new Account(
            a.Title.Trim(), a.TotalAmount, Mappers.Iso(a.StartDate), Mappers.Iso(a.DueDate),
            a.Frequency.Trim(), a.Instalments, a.PaymentAmount, a.Notes) { Kind = AccountKind.Normalize(a.Kind) })];

    /// <summary>Sorted by date text, then time text (stable), like the original tracker.</summary>
    public IReadOnlyList<Payment> Payments() =>
        [.. payments
            .Select(p => new Payment(
                p.Id, p.Title.Trim(), p.Amount, Mappers.Iso(p.Date), p.Time,
                PaymentStatus.IsKnown(p.Status) ? p.Status : PaymentStatus.Unpaid, Mappers.Iso(p.DueDate), p.Note)
            {
                InBudget = p is { BudgetYear: { } year, BudgetMonth: { } month, BudgetAmount: { } amount }
                    ? new BudgetMark(year, month, amount)
                    : null,
            })
            .OrderBy(p => p.Date, StringComparer.Ordinal)
            .ThenBy(p => p.Time, StringComparer.Ordinal)];

    public IReadOnlyList<BudgetLine> BudgetLines() => [.. lines.Select(ToLine)];

    private static BudgetLine ToLine(BudgetLineRow row)
    {
        var amounts = BudgetRules.CleanAmounts(row.Amounts.Cast<object?>());
        return new BudgetLine(
            row.Id,
            row.Year,
            row.Section.Trim() is { Length: > 0 } section ? section : BudgetRules.Expense,
            row.Group.Trim(),
            row.Item.Trim(),
            amounts,
            Coerce.Round(amounts.Sum()),
            row.Notes);
    }

    // ------------------------------------------------------------------ payments

    public void AddAccount(Account account)
    {
        var row = new AccountRow
        {
            OwnerId = owner,
            Title = account.Title.Trim(),
            TotalAmount = account.TotalAmount,
            StartDate = Mappers.Date(account.StartDate),
            DueDate = Mappers.Date(account.DueDate),
            Frequency = account.Frequency.Trim(),
            Instalments = account.Instalments,
            PaymentAmount = account.PaymentAmount,
            Notes = account.Notes,
            Kind = AccountKind.Normalize(account.Kind),
        };
        db.Accounts.Add(row);
        accounts.Add(row);
    }

    public void AddPayment(Payment payment)
    {
        var row = new PaymentRow { Id = payment.Id, OwnerId = owner };
        Write(row, payment);
        db.Payments.Add(row);
        payments.Add(row);
    }

    public bool UpdatePayment(Payment payment)
    {
        if (FindPayment(payment.Id) is not { } row) return false;
        Write(row, payment);
        return true;
    }

    private static void Write(PaymentRow row, Payment payment)
    {
        row.Title = payment.Title.Trim();
        row.Amount = payment.Amount;
        row.Date = Mappers.Date(payment.Date);
        row.Time = Coerce.ToTimeText(payment.Time);
        row.Status = PaymentStatus.IsKnown(payment.Status) ? payment.Status : PaymentStatus.Unpaid;
        row.DueDate = Mappers.Date(payment.DueDate);
        row.Note = payment.Note;
    }

    public bool SetPaymentStatus(string id, string status)
    {
        if (FindPayment(id) is not { } row) return false;
        row.Status = PaymentStatus.IsKnown(status) ? status : PaymentStatus.Unpaid;
        return true;
    }

    public bool SetPaymentBudget(string id, BudgetMark? mark)
    {
        if (FindPayment(id) is not { } row) return false;
        row.BudgetYear = mark?.Year;
        row.BudgetMonth = mark?.Month;
        row.BudgetAmount = mark?.Amount;
        return true;
    }

    public bool DeletePayment(string id)
    {
        if (FindPayment(id) is not { } row) return false;
        Remove(payments, row);
        return true;
    }

    public int? DeleteAccount(string title)
    {
        var key = (title ?? "").Trim();
        if (key.Length == 0) return null;
        bool Same(string value) => string.Equals(value.Trim(), key, StringComparison.OrdinalIgnoreCase);

        var gone = accounts.Where(a => Same(a.Title)).ToList();
        if (gone.Count == 0) return null;
        foreach (var account in gone) Remove(accounts, account);
        var removed = payments.Where(p => Same(p.Title)).ToList();
        foreach (var payment in removed) Remove(payments, payment);
        return removed.Count;
    }

    private PaymentRow? FindPayment(string id) => payments.Find(p => p.Id == id);

    // ------------------------------------------------------------------ budget

    public void AddBudgetLine(BudgetLine line)
    {
        var row = new BudgetLineRow
        {
            OwnerId = owner,
            Id = line.Id,
            Year = line.Year,
            Section = line.Section.Trim(),
            Group = line.Group.Trim(),
            Item = line.Item.Trim(),
            Amounts = [.. Enumerable.Range(0, 12).Select(i => i < line.Amounts.Length ? line.Amounts[i] : 0)],
            Notes = line.Notes,
        };
        db.BudgetLines.Add(row);
        lines.Add(row);
    }

    public bool SetBudgetAmount(string id, int month, double value)
    {
        if (FindLine(id) is not { } row) return false;
        var amounts = (double[])row.Amounts.Clone();
        amounts[month] = Coerce.Round(value);
        row.Amounts = amounts; // a new array, so EF Core sees the change
        return true;
    }

    public bool UpdateBudgetLine(string id, string item, string group, string? notes, double[]? amounts)
    {
        if (FindLine(id) is not { } row) return false;
        row.Item = (item ?? "").Trim();
        row.Group = (group ?? "").Trim();
        if (notes is not null) row.Notes = notes;
        if (amounts is not null) row.Amounts = [.. amounts.Take(12)];
        return true;
    }

    /// <summary>Import "replace": overwrite the months and, if given, the notes.</summary>
    public void ReplaceBudgetAmounts(string id, double[] amounts, string? notes)
    {
        if (FindLine(id) is not { } row) return;
        row.Amounts = [.. amounts.Take(12)];
        if (notes is not null) row.Notes = notes;
    }

    public bool DeleteBudgetLine(string id)
    {
        if (FindLine(id) is not { } row) return false;
        Remove(lines, row);
        return true;
    }

    public int DeleteBudgetYear(int year)
    {
        var gone = lines.Where(l => l.Year == year).ToList();
        foreach (var line in gone) Remove(lines, line);
        return gone.Count;
    }

    private BudgetLineRow? FindLine(string id) => lines.Find(l => l.Id == id);

    // ------------------------------------------------------------------ save

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await db.SaveChangesAsync(cancellationToken);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        db.Dispose();
        release();
    }

    private void Remove<T>(List<T> list, T row)
        where T : class
    {
        list.Remove(row);
        db.Remove(row);
    }
}