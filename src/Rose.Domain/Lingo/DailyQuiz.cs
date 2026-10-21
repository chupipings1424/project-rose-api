namespace Rose.Domain.Lingo;

/// <summary>Meaning: see the word, pick its meaning. Term: see the meaning, pick the word. Listen: hear it, pick its meaning.</summary>
public enum QuizKind
{
    Meaning,
    Term,
    Listen,
}

/// <param name="Answer">Index of the right choice. The quiz is personal practice, so the browser checks it.</param>
public sealed record QuizQuestion(
    int Number, string CardId, QuizKind Kind, string Prompt, string Reading, IReadOnlyList<string> Choices, int Answer);

/// <summary>Five questions a day, built from your own cards.</summary>
public static class DailyQuiz
{
    public const int Size = 5;
    public const int MinCards = 4;

    /// <summary>
    /// The same day and language always give the same quiz (until your cards change), so
    /// reloading the page does not reshuffle it. Weak cards (forgotten before, low ease) come first.
    /// </summary>
    public static IReadOnlyList<QuizQuestion> Build(IReadOnlyList<Card> cards, string language, DateOnly day)
    {
        if (cards.Count < MinCards) return [];
        var random = new Random(StableSeed($"{day:yyyy-MM-dd}|{language}"));

        var picked = cards
            .OrderBy(c => c.Id, StringComparer.Ordinal)
            .Select(c => (Card: c, Shuffle: random.Next()))
            .OrderByDescending(x => x.Card.Lapses > 0 || x.Card.Repetitions == 0)
            .ThenBy(x => x.Card.Ease)
            .ThenBy(x => x.Shuffle)
            .Take(Size)
            .Select(x => x.Card)
            .ToList();

        var kinds = Enum.GetValues<QuizKind>();
        return picked.Select((card, i) =>
        {
            var kind = kinds[(i + random.Next(kinds.Length)) % kinds.Length];
            var right = kind == QuizKind.Term ? card.Term : card.Meaning;
            var wrong = cards
                .Where(c => c.Id != card.Id)
                .Select(c => kind == QuizKind.Term ? c.Term : c.Meaning)
                .Where(text => !string.Equals(text, right, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(_ => random.Next())
                .Take(3);
            var choices = wrong.Append(right).OrderBy(_ => random.Next()).ToList();
            var prompt = kind == QuizKind.Term ? card.Meaning : card.Term;
            var reading = kind == QuizKind.Term ? "" : card.Reading;
            return new QuizQuestion(i + 1, card.Id, kind, prompt, reading, choices, choices.IndexOf(right));
        }).ToList();
    }

    /// <summary>
    /// string.GetHashCode() changes every time the app starts (a defence against hash flooding),
    /// so a seed that must stay the same uses its own hash: FNV-1a.
    /// </summary>
    internal static int StableSeed(string text)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var c in text) hash = (hash ^ c) * 16777619u;
            return (int)hash;
        }
    }
}

public static class Streak
{
    /// <summary>Days in a row with a finished quiz, ending today (or yesterday, if today's is not done yet).</summary>
    public static int Count(IEnumerable<DateOnly> days, DateOnly today)
    {
        var set = days.ToHashSet();
        var day = set.Contains(today) ? today : today.AddDays(-1);
        var count = 0;
        while (set.Contains(day))
        {
            count++;
            day = day.AddDays(-1);
        }
        return count;
    }
}
