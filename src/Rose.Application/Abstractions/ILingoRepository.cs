using Rose.Domain.Lingo;

namespace Rose.Application.Abstractions;

/// <summary>
/// Where ROSE Lingo keeps its cards and quiz results. The use cases only see this interface;
/// Rose.Infrastructure implements it with EF Core + SQLite, the tests could use anything.
/// </summary>
public interface ILingoRepository
{
    /// <summary>Every card, or only one language's when <paramref name="language"/> is set.</summary>
    Task<List<Card>> CardsAsync(string? language, CancellationToken ct);

    /// <summary>Cards whose review date is today or earlier.</summary>
    Task<List<Card>> DueCardsAsync(string language, DateOnly today, CancellationToken ct);

    Task<Card?> FindCardAsync(string id, CancellationToken ct);

    Task<bool> HasTermAsync(string language, string term, string? exceptId, CancellationToken ct);

    void Add(Card card);

    void Remove(Card card);

    Task<List<QuizResult>> QuizResultsAsync(string? language, CancellationToken ct);

    Task<QuizResult?> FindQuizResultAsync(DateOnly day, string language, CancellationToken ct);

    void Add(QuizResult result);

    /// <summary>Writes every change made since the last save in one transaction (unit of work).</summary>
    Task SaveChangesAsync(CancellationToken ct);
}
