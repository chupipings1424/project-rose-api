using Rose.Application.Abstractions;
using Rose.Application.Common;
using Rose.Domain.Common;
using Rose.Domain.Lingo;

namespace Rose.Application.Lingo;

/// <summary>The ROSE Lingo use cases: translate offline, keep flashcards, review them and practise with quizzes.</summary>
public sealed class LingoService(ILingoRepository repository, ITranslator translator, TimeProvider clock)
{
    public const int MaxTranslateLength = 500;
    public const int HistorySize = 30;
    private const int LearnedAfter = 3;

    public static IReadOnlyList<LanguageInfo> AllLanguages() => Languages.All;

    public static IReadOnlyList<KanaEntry> KanaChart() => Kana.Chart;

    public static IReadOnlyList<HangulEntry> HangulChart() => Rose.Domain.Lingo.HangulChart.Chart;

    public TranslatorStatus TranslatorStatus() => translator.Status();

    public async Task<TranslationResult> TranslateAsync(TranslateRequest request, CancellationToken ct)
    {
        var text = (request.Text ?? "").Trim();
        if (text.Length == 0) throw new ValidationException("Type something to translate.");
        if (text.Length > MaxTranslateLength)
            throw new ValidationException($"Translate at most {MaxTranslateLength} characters at a time.");
        var from = Languages.Require(request.From, "from");
        var to = Languages.Require(request.To, "to");
        if (from == to) throw new ValidationException("Pick two different languages.");

        var translation = await translator.TranslateAsync(text, from, to, ct);
        return new TranslationResult(text, translation, from, to, Romanizer.Romanize(from, text), Romanizer.Romanize(to, translation));
    }

    public async Task<IReadOnlyList<CardDto>> CardsAsync(string? language, CancellationToken ct)
    {
        var code = language is null ? null : Languages.Require(language);
        var today = clock.Today();
        var cards = await repository.CardsAsync(code, ct);
        return cards.OrderByDescending(c => c.CreatedAt).Select(c => CardDto.From(c, today)).ToList();
    }

    public async Task<IReadOnlyList<CardDto>> DueAsync(string? language, CancellationToken ct)
    {
        var today = clock.Today();
        var cards = await repository.DueCardsAsync(Languages.Require(language), today, ct);
        return cards.OrderBy(c => c.DueOn).ThenBy(c => c.CreatedAt).Select(c => CardDto.From(c, today)).ToList();
    }

    public async Task<CardDto> CreateCardAsync(CardRequest request, CancellationToken ct)
    {
        var today = clock.Today();
        var card = new Card(request.Language ?? "", request.Term ?? "", request.Reading ?? "", request.Meaning ?? "", request.Notes ?? "", clock.GetUtcNow(), today);
        await EnsureUniqueAsync(card.Language, card.Term, null, ct);
        repository.Add(card);
        await repository.SaveChangesAsync(ct);
        return CardDto.From(card, today);
    }

    public async Task<CardDto> UpdateCardAsync(string id, CardRequest request, CancellationToken ct)
    {
        var card = await RequireCardAsync(id, ct);
        card.Edit(request.Term, request.Reading, request.Meaning, request.Notes);
        await EnsureUniqueAsync(card.Language, card.Term, card.Id, ct);
        await repository.SaveChangesAsync(ct);
        return CardDto.From(card, clock.Today());
    }

    public async Task DeleteCardAsync(string id, CancellationToken ct)
    {
        repository.Remove(await RequireCardAsync(id, ct));
        await repository.SaveChangesAsync(ct);
    }

    public async Task<CardDto> ReviewAsync(string id, ReviewRequest request, CancellationToken ct)
    {
        if (!Enum.TryParse<ReviewGrade>(request.Grade, ignoreCase: true, out var grade) || !Enum.IsDefined(grade)
            || int.TryParse(request.Grade, out _))
        {
            throw new ValidationException("grade must be again, hard, good or easy.");
        }

        var card = await RequireCardAsync(id, ct);
        var today = clock.Today();
        card.Review(grade, today);
        await repository.SaveChangesAsync(ct);
        return CardDto.From(card, today);
    }

    /// <summary>Adds the built-in words for a language, skipping any you already have.</summary>
    public async Task<StarterImportResult> ImportStarterAsync(string language, CancellationToken ct)
    {
        var code = Languages.Require(language);
        if (!StarterDecks.All.TryGetValue(code, out var words))
            throw new ValidationException("There is no starter deck for English - add your own words.");

        var existing = (await repository.CardsAsync(code, ct)).Select(c => c.Term).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var now = clock.GetUtcNow();
        var today = clock.Today();
        var added = 0;
        foreach (var word in words.Where(w => existing.Add(w.Term)))
        {
            repository.Add(new Card(code, word.Term, word.Reading, word.Meaning, word.Notes, now, today));
            added++;
        }
        await repository.SaveChangesAsync(ct);
        return new StarterImportResult(added, words.Count - added);
    }

    /// <summary>A new quiz every time you ask: random cards, or the ones you picked.</summary>
    public async Task<QuizDto> QuizAsync(QuizRequest request, CancellationToken ct)
    {
        var code = Languages.Require(request.Language);
        var picked = request.CardIds ?? [];
        var mode = picked.Count > 0 ? QuizMode.Custom : QuizMode.Random;
        if (mode == QuizMode.Random && !Quiz.Sizes.Contains(request.Count ?? 0))
            throw new ValidationException($"count must be {string.Join(", ", Quiz.Sizes)}.");
        if (picked.Count > Quiz.MaxQuestions)
            throw new ValidationException($"Pick at most {Quiz.MaxQuestions} cards.");

        var deck = await repository.CardsAsync(code, ct);
        IReadOnlyList<Card> asked;
        if (mode == QuizMode.Random)
        {
            asked = Quiz.PickRandom(deck, request.Count!.Value, Random.Shared);
        }
        else
        {
            var ids = picked.ToHashSet(StringComparer.Ordinal);
            asked = deck.Where(c => ids.Contains(c.Id)).ToList();
            if (asked.Count != ids.Count) throw new ValidationException("Some of the picked cards no longer exist - pick again.");
        }

        var questions = Quiz.Build(deck, asked, Random.Shared);
        return new QuizDto(
            code,
            mode.ToString().ToLowerInvariant(),
            questions.Select(q => new QuizQuestionDto(q.Number, q.CardId, q.Kind.ToString().ToLowerInvariant(), q.Prompt, q.Reading, q.Choices, q.Answer)).ToList(),
            Math.Max(0, Quiz.MinCards - deck.Count));
    }

    public async Task<QuizResultDto> SubmitQuizAsync(QuizSubmitRequest request, CancellationToken ct)
    {
        var code = Languages.Require(request.Language);
        if (!Enum.TryParse<QuizMode>(request.Mode ?? nameof(QuizMode.Random), ignoreCase: true, out var mode)
            || !Enum.IsDefined(mode) || int.TryParse(request.Mode, out _))
        {
            throw new ValidationException("mode must be random or custom.");
        }
        var results = request.Results ?? [];
        if (results.Count is 0 or > Quiz.MaxQuestions)
            throw new ValidationException($"Send between 1 and {Quiz.MaxQuestions} answers.");
        if (results.Any(r => string.IsNullOrWhiteSpace(r.CardId)) || results.Select(r => r.CardId).Distinct().Count() != results.Count)
            throw new ValidationException("Each answer needs a different card_id.");

        // A wrong answer means the card was forgotten: it comes back in today's review.
        var today = clock.Today();
        foreach (var wrong in results.Where(r => !r.Correct))
        {
            var card = await repository.FindCardAsync(wrong.CardId!, ct);
            if (card is not null && card.Language == code) card.Review(ReviewGrade.Again, today);
        }

        var result = new QuizResult(today, code, mode, results.Count(r => r.Correct), results.Count, clock.GetUtcNow());
        repository.Add(result);
        await repository.SaveChangesAsync(ct);
        return QuizResultDto.From(result);
    }

    /// <summary>Your latest quiz scores in a language, newest first.</summary>
    public async Task<IReadOnlyList<QuizResultDto>> QuizHistoryAsync(string? language, CancellationToken ct)
    {
        var code = Languages.Require(language);
        var results = await repository.QuizResultsAsync(code, ct);
        // SQLite cannot sort DateTimeOffset columns, so the (small) list is sorted here.
        return results.OrderByDescending(r => r.CompletedAt).ThenByDescending(r => r.Id).Take(HistorySize).Select(QuizResultDto.From).ToList();
    }

    public async Task<LingoStats> StatsAsync(string? language, CancellationToken ct)
    {
        var code = language is null ? null : Languages.Require(language);
        var today = clock.Today();
        var cards = await repository.CardsAsync(code, ct);
        return new LingoStats(cards.Count, cards.Count(c => c.IsDue(today)), cards.Count(c => c.Repetitions >= LearnedAfter));
    }

    private async Task<Card> RequireCardAsync(string id, CancellationToken ct) =>
        await repository.FindCardAsync(id, ct) ?? throw new NotFoundException("That card no longer exists.");

    private async Task EnsureUniqueAsync(string language, string term, string? exceptId, CancellationToken ct)
    {
        if (await repository.HasTermAsync(language, term, exceptId, ct))
            throw new ConflictException($"You already have a card for \"{term}\".");
    }
}
