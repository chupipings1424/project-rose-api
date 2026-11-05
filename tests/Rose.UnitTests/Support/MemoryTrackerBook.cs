using Rose.Application.Abstractions;
using Rose.Domain.Budget;
using Rose.Domain.Tracker;

namespace Rose.UnitTests.Support;

/// <summary>An in-memory <see cref="ITrackerWorkbook"/>: one user's titles, payments and budget lines.</summary>
internal sealed class MemoryTrackerBook : ITrackerWorkbook
{
    public List<Account> AccountList { get; } = [];
    public List<Payment> PaymentList { get; } = [];
    public List<BudgetLine> Lines { get; } = [];
    public int Saves { get; private set; }

    public IReadOnlyList<Account> Accounts() => [.. AccountList];

    public IReadOnlyList<Payment> Payments() => [.. PaymentList];

    public IReadOnlyList<BudgetLine> BudgetLines() => [.. Lines.Select(l => l with { Amounts = (double[])l.Amounts.Clone() })];

    public BudgetLine Line(string item) => Lines.Single(l => string.Equals(l.Item, item, StringComparison.OrdinalIgnoreCase));

    public Payment Payment(string id) => PaymentList.Single(p => p.Id == id);

    public void AddAccount(Account account) => AccountList.Add(account);

    public void AddPayment(Payment payment) => PaymentList.Add(payment);

    public bool UpdatePayment(Payment payment) => ReplacePayment(payment.Id, old => payment with { InBudget = old.InBudget });

    public bool SetPaymentStatus(string id, string status) => ReplacePayment(id, p => p with { Status = status });

    public bool SetPaymentBudget(string id, BudgetMark? mark) => ReplacePayment(id, p => p with { InBudget = mark });

    public bool DeletePayment(string id) => PaymentList.RemoveAll(p => p.Id == id) > 0;

    public int? DeleteAccount(string title)
    {
        if (AccountList.RemoveAll(a => string.Equals(a.Title, title, StringComparison.OrdinalIgnoreCase)) == 0) return null;
        return PaymentList.RemoveAll(p => p.HasTitle(title));
    }

    public void AddBudgetLine(BudgetLine line) => Lines.Add(line);

    public bool SetBudgetAmount(string id, int month, double value) => ReplaceLine(id, l =>
    {
        var amounts = (double[])l.Amounts.Clone();
        amounts[month] = value;
        return l with { Amounts = amounts, Total = amounts.Sum() };
    });

    public bool UpdateBudgetLine(string id, string item, string group, string? notes, double[]? amounts) =>
        ReplaceLine(id, l => l with { Item = item, Group = group, Notes = notes ?? l.Notes, Amounts = amounts ?? l.Amounts });

    public void ReplaceBudgetAmounts(string id, double[] amounts, string? notes) =>
        ReplaceLine(id, l => l with { Amounts = amounts, Notes = notes ?? l.Notes });

    public bool DeleteBudgetLine(string id) => Lines.RemoveAll(l => l.Id == id) > 0;

    public int DeleteBudgetYear(int year) => Lines.RemoveAll(l => l.Year == year);

    public Task SaveAsync(CancellationToken cancellationToken = default)
    {
        Saves++;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
    }

    private bool ReplacePayment(string id, Func<Payment, Payment> change)
    {
        var index = PaymentList.FindIndex(p => p.Id == id);
        if (index < 0) return false;
        PaymentList[index] = change(PaymentList[index]);
        return true;
    }

    private bool ReplaceLine(string id, Func<BudgetLine, BudgetLine> change)
    {
        var index = Lines.FindIndex(l => l.Id == id);
        if (index < 0) return false;
        Lines[index] = change(Lines[index]);
        return true;
    }
}

/// <summary>Hands out the same <see cref="MemoryTrackerBook"/> every time (disposing it does nothing).</summary>
internal sealed class MemoryTrackerStore(MemoryTrackerBook book) : ITrackerWorkbookStore
{
    public string StorageName => "memory";

    public Task<ITrackerWorkbook> OpenAsync(Guid owner, CancellationToken cancellationToken = default) =>
        Task.FromResult<ITrackerWorkbook>(book);

    public Task<byte[]> ExportAsync(Guid owner, CancellationToken cancellationToken = default) => Task.FromResult(Array.Empty<byte>());
}

internal sealed record FixedCurrentUser(Guid Id) : ICurrentUser;