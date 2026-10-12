using Rose.Application.Abstractions;
using Rose.Application.Common;
using Rose.Domain.Budget;
using Rose.Domain.Common;

namespace Rose.Application.Tracker;

/// <summary>The yearly budget (Revenue / Expense lines with twelve monthly amounts).</summary>
public sealed class BudgetService(ITrackerWorkbookStore store, TimeProvider clock)
{
    public async Task<BudgetView> GetAsync(string? year, CancellationToken ct = default)
    {
        using var book = await store.OpenAsync(ct);
        return View(book, year);
    }

    public async Task<BudgetLineCreated> AddLineAsync(AddBudgetLineRequest request, CancellationToken ct = default)
    {
        var item = (request.Item ?? "").Trim();
        if (item.Length == 0) throw new ValidationException("An item name is required.");
        var section = BudgetRules.CanonicalSection(request.Section);
        var group = (request.Group ?? "").Trim();
        if (group.Length == 0) group = BudgetRules.DefaultGroups[section][0];
        var year = YearOrCurrent(request.Year);
        var amounts = BudgetRules.CleanAmounts(request.Amounts);

        using var book = await store.OpenAsync(ct);
        if (book.BudgetLines().Any(l => l.Matches(year, section, group, item)))
            throw new ConflictException(BudgetRules.DuplicateMessage(item, group, year));

        var line = new BudgetLine(PaymentTrackerService.NewId(), year, section, group, item, amounts, Coerce.Round(amounts.Sum()), request.Notes ?? "");
        book.AddBudgetLine(line);
        book.Save();
        return new BudgetLineCreated(line.Id, View(book, request.Year));
    }

    public async Task<BudgetChanged> UpdateLineAsync(string id, UpdateBudgetLineRequest request, CancellationToken ct = default)
    {
        using var book = await store.OpenAsync(ct);
        bool found;
        if (request.Month is not null)
        {
            var month = BudgetRules.MonthIndex(request.Month);
            found = book.SetBudgetAmount(id, month, Coerce.Round(Coerce.ToDouble(request.Value)));
        }
        else
        {
            var line = book.BudgetLines().FirstOrDefault(l => l.Id == id);
            found = line is not null;
            if (line is not null)
            {
                var item = string.IsNullOrWhiteSpace(request.Item) ? line.Item : request.Item.Trim();
                var group = string.IsNullOrWhiteSpace(request.Group) ? line.Group : request.Group.Trim();
                // Renaming must not collide with another line in the same group and year.
                if (book.BudgetLines().Any(l => l.Id != id && l.Matches(line.Year, line.Section, group, item)))
                    throw new ConflictException(BudgetRules.DuplicateMessage(item, group, line.Year));
                book.UpdateBudgetLine(id, item, group, request.Notes,
                    request.Amounts is null ? null : BudgetRules.CleanAmounts(request.Amounts));
            }
        }
        if (!found) throw new NotFoundException("Budget line not found.");
        book.Save();
        return new BudgetChanged(true, View(book, request.Year));
    }

    public async Task<BudgetChanged> DeleteLineAsync(string id, string? year, CancellationToken ct = default)
    {
        using var book = await store.OpenAsync(ct);
        if (!book.DeleteBudgetLine(id)) throw new NotFoundException("Budget line not found.");
        book.Save();
        return new BudgetChanged(true, View(book, year));
    }

    /// <summary>Deletes every line of a year in one save. Answers with the current year's budget.</summary>
    public async Task<BudgetYearDeleted> DeleteYearAsync(int year, CancellationToken ct = default)
    {
        if (year <= 0) throw new ValidationException("A valid year is required.");
        using var book = await store.OpenAsync(ct);
        var removed = book.DeleteBudgetYear(year);
        if (removed == 0) throw new NotFoundException($"No budget lines found for {year}.");
        book.Save();
        return new BudgetYearDeleted(true, removed, View(book, null));
    }

    /// <summary>Clones a year's items (optionally with amounts) into another year, skipping ones already there.</summary>
    public async Task<BudgetYearCopied> CopyYearAsync(CopyBudgetYearRequest request, CancellationToken ct = default)
    {
        var from = Coerce.ToInt(request.From);
        var to = Coerce.ToInt(request.To);
        if (from == to) throw new ValidationException("Source and target years must differ.");

        using var book = await store.OpenAsync(ct);
        var lines = book.BudgetLines();
        var source = lines.Where(l => l.Year == from).ToList();
        if (source.Count == 0) throw new ValidationException($"No budget lines found for {from}.");

        var copied = 0;
        foreach (var line in source)
        {
            if (lines.Any(l => l.Matches(to, line.Section, line.Group, line.Item))) continue;
            var amounts = request.IncludeAmounts ? line.Amounts : new double[12];
            book.AddBudgetLine(line with
            {
                Id = PaymentTrackerService.NewId(),
                Year = to,
                Amounts = amounts,
                Total = Coerce.Round(amounts.Sum()),
            });
            copied++;
        }
        book.Save();
        return new BudgetYearCopied(copied, View(book, request.To));
    }

    private int YearOrCurrent(string? year)
    {
        var current = clock.Today().Year;
        var parsed = Coerce.ToInt(year, current);
        return parsed == 0 ? current : parsed;
    }

    private BudgetView View(ITrackerWorkbook book, string? year) =>
        BudgetView.Build(YearOrCurrent(year), book.BudgetLines(), clock.Today().Year);
}