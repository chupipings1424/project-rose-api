using Microsoft.EntityFrameworkCore;
using Rose.Application.Abstractions;
using Rose.Domain.Lingo;

namespace Rose.Infrastructure.Lingo;

/// <summary><see cref="ILingoRepository"/> on top of EF Core. Each LINQ query below becomes SQL.</summary>
internal sealed class EfLingoRepository(LingoDbContext db) : ILingoRepository
{
    public Task<List<Card>> CardsAsync(string? language, CancellationToken ct) =>
        db.Cards.Where(c => language == null || c.Language == language).ToListAsync(ct);

    public Task<List<Card>> DueCardsAsync(string language, DateOnly today, CancellationToken ct) =>
        db.Cards.Where(c => c.Language == language && c.DueOn <= today).ToListAsync(ct);

    public Task<Card?> FindCardAsync(string id, CancellationToken ct) =>
        db.Cards.FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<bool> HasTermAsync(string language, string term, string? exceptId, CancellationToken ct) =>
        // COLLATE NOCASE: "hallo" and "Hallo" count as the same word (SQLite folds A-Z only).
        db.Cards.AnyAsync(c => c.Language == language && c.Id != exceptId && EF.Functions.Collate(c.Term, "NOCASE") == term, ct);

    public void Add(Card card) => db.Cards.Add(card);

    public void Remove(Card card) => db.Cards.Remove(card);

    public Task<List<QuizResult>> QuizResultsAsync(string? language, CancellationToken ct) =>
        db.QuizResults.Where(r => language == null || r.Language == language).ToListAsync(ct);

    public void Add(QuizResult result) => db.QuizResults.Add(result);

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
