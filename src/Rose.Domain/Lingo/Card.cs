using Rose.Domain.Common;

namespace Rose.Domain.Lingo;

/// <summary>
/// One flashcard: a word or phrase in the language you are learning, its reading and its meaning,
/// plus where it is in the spaced-repetition schedule. Stored in lingo.db (SQLite).
/// </summary>
public sealed class Card
{
    public const int MaxTextLength = 200;

    // EF Core uses this constructor when it reads a row; the setters are private, so only
    // the methods below can change a card.
    private Card()
    {
    }

    public Card(string language, string term, string reading, string meaning, string notes, DateTimeOffset createdAt, DateOnly today)
    {
        Id = Guid.NewGuid().ToString();
        Language = Languages.Require(language);
        CreatedAt = createdAt;
        Edit(term, reading, meaning, notes);
        Apply(SrsState.New(today));
    }

    public string Id { get; private set; } = "";
    public string Language { get; private set; } = "";
    public string Term { get; private set; } = "";

    /// <summary>Latin letters: romaji or Korean romanization. Filled in for you when it can be.</summary>
    public string Reading { get; private set; } = "";
    public string Meaning { get; private set; } = "";
    public string Notes { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }

    public int Repetitions { get; private set; }
    public int IntervalDays { get; private set; }
    public double Ease { get; private set; }
    public DateOnly DueOn { get; private set; }
    public int Lapses { get; private set; }
    public DateOnly? LastReviewedOn { get; private set; }

    public bool IsDue(DateOnly today) => DueOn <= today;

    public void Edit(string? term, string? reading, string? meaning, string? notes)
    {
        Term = Required(term, "term");
        Meaning = Required(meaning, "meaning");
        Notes = Limit((notes ?? "").Trim(), "notes", 500);
        var typed = Limit((reading ?? "").Trim(), "reading", MaxTextLength);
        Reading = typed.Length > 0 ? typed : Romanizer.Romanize(Language, Term);
    }

    public void Review(ReviewGrade grade, DateOnly today)
    {
        if (!Enum.IsDefined(grade)) throw new ValidationException("grade must be again, hard, good or easy.");
        Apply(Srs.Next(new SrsState(Repetitions, IntervalDays, Ease, DueOn, Lapses), grade, today));
        LastReviewedOn = today;
    }

    private void Apply(SrsState state)
    {
        Repetitions = state.Repetitions;
        IntervalDays = state.IntervalDays;
        Ease = state.Ease;
        DueOn = state.DueOn;
        Lapses = state.Lapses;
    }

    private static string Required(string? value, string field)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0) throw new ValidationException($"{char.ToUpperInvariant(field[0])}{field[1..]} is required.");
        return Limit(text, field, MaxTextLength);
    }

    private static string Limit(string text, string field, int max) =>
        text.Length <= max ? text : throw new ValidationException($"{field} can be at most {max} characters.");
}
