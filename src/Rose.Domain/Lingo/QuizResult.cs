namespace Rose.Domain.Lingo;

/// <summary>A finished practice quiz. Take as many as you like; each one is kept for your history.</summary>
public sealed class QuizResult
{
    private QuizResult()
    {
    }

    public QuizResult(DateOnly day, string language, QuizMode mode, int score, int total, DateTimeOffset completedAt)
    {
        Day = day;
        Language = Languages.Require(language);
        Mode = mode;
        Total = total;
        Score = Math.Clamp(score, 0, total);
        CompletedAt = completedAt;
    }

    public int Id { get; private set; }
    public DateOnly Day { get; private set; }
    public string Language { get; private set; } = "";
    public QuizMode Mode { get; private set; }
    public int Score { get; private set; }
    public int Total { get; private set; }
    public DateTimeOffset CompletedAt { get; private set; }
}
