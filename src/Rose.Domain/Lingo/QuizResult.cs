namespace Rose.Domain.Lingo;

/// <summary>A finished daily quiz: one per language per day. The streak is counted from these.</summary>
public sealed class QuizResult
{
    private QuizResult()
    {
    }

    public QuizResult(DateOnly day, string language, int score, int total, DateTimeOffset completedAt)
    {
        Day = day;
        Language = Languages.Require(language);
        Total = total;
        Score = Math.Clamp(score, 0, total);
        CompletedAt = completedAt;
    }

    public int Id { get; private set; }
    public DateOnly Day { get; private set; }
    public string Language { get; private set; } = "";
    public int Score { get; private set; }
    public int Total { get; private set; }
    public DateTimeOffset CompletedAt { get; private set; }
}
