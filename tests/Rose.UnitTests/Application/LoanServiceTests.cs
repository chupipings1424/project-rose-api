using Rose.Application.Abstractions;
using Rose.Application.Loans;
using Rose.Domain.Common;
using Rose.Domain.Loans;
using Rose.UnitTests.Support;

namespace Rose.UnitTests.Application;

public sealed class LoanServiceTests : IDisposable
{
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 11, 11, 9, 0, 0, TimeSpan.Zero));
    private readonly MemoryRepository<Loan> _repo = new(l => l.Id);
    private readonly RecordingReportWriter _report = new();
    private readonly LoanService _service;

    public LoanServiceTests() => _service = new LoanService(_repo, _report, _clock);

    public void Dispose() => _service.Dispose();

    private Task<LoanView> Add(string name, string loanDate, int term = 24) =>
        _service.CreateAsync(TestUsers.Alice, new LoanRequest(name, "Coop", loanDate, term, 500, 10_000));

    [Fact]
    public async Task Create_saves_the_loan_and_returns_todays_progress()
    {
        var loan = await Add(" Coop loan ", "2026-04-02");

        Assert.Equal("Coop loan", loan.Name);
        Assert.Equal(7, loan.PaymentsMade);
        Assert.Equal(8_500m, loan.Balance);
        Assert.Single(_repo.Stored);
    }

    [Fact]
    public async Task Progress_moves_by_itself_as_the_days_pass()
    {
        await Add("Coop", "2026-04-02");

        _clock.Advance(TimeSpan.FromDays(30)); // 2026-12-11, past the Dec 2 due date
        var loan = Assert.Single(await _service.ListAsync(TestUsers.Alice));

        Assert.Equal(8, loan.PaymentsMade);
        Assert.Equal(8_000m, loan.Balance);
    }

    [Fact]
    public async Task List_puts_running_loans_first_and_finished_ones_last()
    {
        await Add("Done", "2020-01-01", 12);
        await Add("Long", "2026-01-01", 60);
        await Add("Short", "2026-01-01", 12);

        var names = (await _service.ListAsync(TestUsers.Alice)).Select(l => l.Name);

        Assert.Equal(["Short", "Long", "Done"], names);
    }

    [Fact]
    public async Task Update_and_remove_change_only_that_loan()
    {
        var loan = await Add("Coop", "2026-04-02");
        await Add("SSS", "2026-01-10");

        var changed = await _service.UpdateAsync(TestUsers.Alice, loan.Id, new LoanRequest("Coop", "Coop", "2026-04-02", 12, 1000, 10_000));
        Assert.Equal(12_000m, changed.TotalPayable);
        Assert.Equal(5_000m, changed.Balance);

        await _service.RemoveAsync(TestUsers.Alice, loan.Id);
        Assert.Equal("SSS", Assert.Single(_repo.Stored).Name);
        var ex = await Assert.ThrowsAsync<NotFoundException>(() => _service.RemoveAsync(TestUsers.Alice, loan.Id));
        Assert.Equal("That loan no longer exists.", ex.Message);
    }

    [Fact]
    public async Task Loans_are_private_to_their_owner()
    {
        var loan = await Add("Coop", "2026-04-02");

        Assert.Empty(await _service.ListAsync(TestUsers.Bob));
        await Assert.ThrowsAsync<NotFoundException>(() => _service.RemoveAsync(TestUsers.Bob, loan.Id));
    }

    [Fact]
    public async Task Report_writes_the_listed_loans_as_of_today()
    {
        await Add("Coop", "2026-04-02");

        var bytes = await _service.ReportAsync(TestUsers.Alice);

        Assert.Equal([1, 2, 3], bytes);
        Assert.Equal(new DateOnly(2026, 11, 11), _report.AsOf);
        Assert.Equal("Coop", Assert.Single(_report.Loans!).Name);
    }

    private sealed class RecordingReportWriter : ILoanReportWriter
    {
        public IReadOnlyList<LoanView>? Loans { get; private set; }
        public DateOnly AsOf { get; private set; }

        public byte[] Write(IReadOnlyList<LoanView> loans, DateOnly asOf)
        {
            Loans = loans;
            AsOf = asOf;
            return [1, 2, 3];
        }
    }
}