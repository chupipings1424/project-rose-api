namespace Rose.Domain.Lingo;

/// <summary>Meaning: see the word, pick its meaning. Term: see the meaning, pick the word. Listen: hear it, pick its meaning.</summary>
public enum QuizKind
{
    Meaning,
    Term,
    Listen,
}

/// <summary>Random: "Take a quiz" picks the cards. Custom: "Make a quiz" with the cards you chose.</summary>
public enum QuizMode
{
    Random,
    Custom,
}

/// <param name="Answer">Index of the right choice. The quiz is personal practice, so the browser checks it.</param>
public sealed record QuizQuestion(
    int Number, string CardId, QuizKind Kind, string Prompt, string Reading, IReadOnlyList<string> Choices, int Answer);

/// <summary>A practice quiz on demand, as often as you like, built from your own cards.</summary>
public static class Quiz
{
    /// <summary>A question needs the right answer plus three wrong ones, so four cards at least.</summary>
    public const int MinCards = 4;
    public const int MaxQuestions = 50;
    public static readonly IReadOnlyList<int> Sizes = [5, 10, 20];

    /// <summary>
    /// One question per card in <paramref name="asked"/> (in a shuffled order). Wrong choices come
    /// from the whole <paramref name="deck"/>, so even a quiz on two cards has four choices.
    /// The <see cref="Random"/> is passed in, so tests can use a fixed seed.
    /// </summary>
    public static IReadOnlyList<QuizQuestion> Build(IReadOnlyList<Card> deck, IReadOnlyList<Card> asked, Random random)
    {
        if (deck.Count < MinCards) return [];
        var kinds = Enum.GetValues<QuizKind>();

        return asked
            .OrderBy(_ => random.Next())
            .Take(MaxQuestions)
            .Select((card, i) =>
            {
                var kind = kinds[random.Next(kinds.Length)];
                var right = kind == QuizKind.Term ? card.Term : card.Meaning;
                var wrong = deck
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
            })
            .ToList();
    }

    /// <summary>"Take a quiz": <paramref name="count"/> random cards from the deck (all of them if the deck is smaller).</summary>
    public static IReadOnlyList<Card> PickRandom(IReadOnlyList<Card> deck, int count, Random random) =>
        deck.OrderBy(_ => random.Next()).Take(count).ToList();
}
