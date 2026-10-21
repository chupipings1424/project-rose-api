using Rose.Domain.Lingo;

namespace Rose.Application.Lingo;

/// <summary>Translate <c>text</c> from one language code to another (ja, ko, de, en).</summary>
public sealed record TranslateRequest(string? Text, string? From, string? To);

/// <param name="TextReading">Romaji / romanization of the text, when it can be worked out.</param>
/// <param name="TranslationReading">Romaji / romanization of the translation, when it can be worked out.</param>
public sealed record TranslationResult(string Text, string Translation, string From, string To, string TextReading, string TranslationReading);

/// <summary>Create or edit a card. Leave <c>reading</c> empty to have it filled in. <c>language</c> is ignored on edit.</summary>
public sealed record CardRequest(string? Language, string? Term, string? Reading, string? Meaning, string? Notes);

public sealed record CardDto(
    string Id,
    string Language,
    string Term,
    string Reading,
    string Meaning,
    string Notes,
    DateTimeOffset CreatedAt,
    int Repetitions,
    int IntervalDays,
    double Ease,
    DateOnly DueOn,
    int Lapses,
    DateOnly? LastReviewedOn,
    bool IsDue)
{
    public static CardDto From(Card card, DateOnly today) => new(
        card.Id, card.Language, card.Term, card.Reading, card.Meaning, card.Notes, card.CreatedAt,
        card.Repetitions, card.IntervalDays, card.Ease, card.DueOn, card.Lapses, card.LastReviewedOn, card.IsDue(today));
}

/// <summary><c>grade</c>: again, hard, good or easy.</summary>
public sealed record ReviewRequest(string? Grade);

/// <param name="Kind">meaning (pick the meaning), term (pick the word) or listen (hear it, pick the meaning).</param>
/// <param name="Answer">Index of the right choice.</param>
public sealed record QuizQuestionDto(int Number, string CardId, string Kind, string Prompt, string Reading, IReadOnlyList<string> Choices, int Answer);

/// <param name="CardsNeeded">How many more cards this language needs before there can be a quiz (0 = enough).</param>
/// <param name="Completed">Today's result, if the quiz is already done.</param>
public sealed record QuizDto(DateOnly Day, string Language, IReadOnlyList<QuizQuestionDto> Questions, int CardsNeeded, QuizResultDto? Completed);

public sealed record QuizAnswer(string? CardId, bool Correct);

/// <summary>What you got right and wrong. Wrong cards go back into today's review.</summary>
public sealed record QuizSubmitRequest(string? Language, List<QuizAnswer>? Results);

public sealed record QuizResultDto(DateOnly Day, string Language, int Score, int Total, DateTimeOffset CompletedAt)
{
    public static QuizResultDto From(QuizResult result) =>
        new(result.Day, result.Language, result.Score, result.Total, result.CompletedAt);
}

/// <param name="Learned">Cards remembered at least 3 times in a row.</param>
/// <param name="Streak">Days in a row with a finished quiz (any language).</param>
public sealed record LingoStats(int Cards, int Due, int Learned, int Streak, bool QuizDoneToday);

public sealed record StarterImportResult(int Added, int Skipped);
