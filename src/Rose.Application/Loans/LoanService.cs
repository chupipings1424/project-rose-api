using Rose.Application.Abstractions;
using Rose.Application.Common;
using Rose.Domain.Common;
using Rose.Domain.Loans;

namespace Rose.Application.Loans;

/// <summary>
/// Adding, editing and removing loans. A singleton shared by all requests: every method holds one
/// user's loans exclusively while it reads or changes them. Progress is never stored - it is
/// worked out for today every time (see <see cref="LoanRules"/>).
/// </summary>
public sealed class LoanService(IRepository<Loan> repository, ILoanReportWriter reportWriter, TimeProvider clock) : IDisposable, IUserScopedCache
{
    private readonly RepositoryCache<Loan> cache = new(repository);

    /// <summary>Every loan with its progress today: running ones first (soonest finish first), then completed ones (latest finish first).</summary>
    public async Task<IReadOnlyList<LoanView>> ListAsync(Guid owner, CancellationToken ct = default)
    {
        var today = clock.Today();
        using var session = await cache.OpenAsync(owner, ct);
        return [.. session.Items
            .Select(l => LoanRules.Summarize(l, today))
            .OrderBy(l => l.Status == LoanStatus.Completed)
            .ThenBy(l => l.Status == LoanStatus.Completed ? -l.FinishDate.DayNumber : l.FinishDate.DayNumber)
            .ThenBy(l => l.Name, StringComparer.OrdinalIgnoreCase)];
    }

    public async Task<LoanView> CreateAsync(Guid owner, LoanRequest request, CancellationToken ct = default)
    {
        var input = Parse(request);
        var now = clock.GetUtcNow();
        var loan = new Loan
        {
            Id = Guid.NewGuid().ToString(),
            Name = input.Name,
            Type = input.Type,
            LoanDate = input.LoanDate,
            Term = input.Term,
            MonthlyAmortization = input.MonthlyAmortization,
            Amount = input.Amount,
            CreatedAt = now,
            UpdatedAt = now,
        };
        using var session = await cache.OpenAsync(owner, ct);
        session.Items.Add(loan);
        return await SaveAsync(session, loan, ct);
    }

    public async Task<LoanView> UpdateAsync(Guid owner, string id, LoanRequest request, CancellationToken ct = default)
    {
        var input = Parse(request);
        using var session = await cache.OpenAsync(owner, ct);
        var loan = Get(session, id);
        loan.Name = input.Name;
        loan.Type = input.Type;
        loan.LoanDate = input.LoanDate;
        loan.Term = input.Term;
        loan.MonthlyAmortization = input.MonthlyAmortization;
        loan.Amount = input.Amount;
        return await SaveAsync(session, loan, ct);
    }

    public async Task RemoveAsync(Guid owner, string id, CancellationToken ct = default)
    {
        using var session = await cache.OpenAsync(owner, ct);
        var loan = Get(session, id);
        await session.DeleteAsync(loan.Id, ct);
        session.Items.Remove(loan);
    }

    /// <summary>The Excel report: a summary sheet plus one schedule sheet per loan.</summary>
    public async Task<byte[]> ReportAsync(Guid owner, CancellationToken ct = default) =>
        reportWriter.Write(await ListAsync(owner, ct), clock.Today());

    /// <summary>Forgets a deleted user's cached loans.</summary>
    public void Forget(Guid owner) => cache.Forget(owner);

    public void Dispose() => cache.Dispose();

    private static LoanInput Parse(LoanRequest request) =>
        LoanRules.Parse(request.Name, request.Type, request.LoanDate, request.Term, request.MonthlyAmortization, request.Amount);

    private static Loan Get(RepositoryCache<Loan>.Session session, string id) =>
        session.Items.Find(l => l.Id == id) ?? throw new NotFoundException("That loan no longer exists.");

    private async Task<LoanView> SaveAsync(RepositoryCache<Loan>.Session session, Loan loan, CancellationToken ct)
    {
        loan.UpdatedAt = clock.GetUtcNow();
        await session.SaveAsync(loan, ct);
        return LoanRules.Summarize(loan, clock.Today());
    }
}