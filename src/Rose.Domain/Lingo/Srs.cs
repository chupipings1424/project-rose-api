namespace Rose.Domain.Lingo;

/// <summary>How well you remembered a card.</summary>
public enum ReviewGrade
{
    Again = 0,
    Hard = 1,
    Good = 2,
    Easy = 3,
}

/// <param name="Ease">How fast the gaps grow (2.5 = each gap is 2.5x the last).</param>
public sealed record SrsState(int Repetitions, int IntervalDays, double Ease, DateOnly DueOn, int Lapses)
{
    public const double StartEase = 2.5;

    public static SrsState New(DateOnly today) => new(0, 0, StartEase, today, 0);
}

/// <summary>
/// Spaced repetition, based on SM-2 (the algorithm behind Anki): every time you remember a
/// card the gap before you see it again grows, so you review it just before you would forget.
/// </summary>
public static class Srs
{
    public const double MinEase = 1.3;
    public const int MaxIntervalDays = 3650;

    public static SrsState Next(SrsState state, ReviewGrade grade, DateOnly today)
    {
        if (grade == ReviewGrade.Again)
        {
            // Forgotten: start over and see it again today.
            return state with
            {
                Repetitions = 0,
                IntervalDays = 0,
                Ease = Math.Max(MinEase, Math.Round(state.Ease - 0.2, 2)),
                DueOn = today,
                Lapses = state.Lapses + 1,
            };
        }

        var ease = Math.Max(MinEase, Math.Round(grade switch
        {
            ReviewGrade.Hard => state.Ease - 0.15,
            ReviewGrade.Easy => state.Ease + 0.15,
            _ => state.Ease,
        }, 2));

        var interval = state.Repetitions switch
        {
            0 => grade == ReviewGrade.Easy ? 4 : 1,
            1 => grade switch { ReviewGrade.Hard => 3, ReviewGrade.Easy => 8, _ => 6 },
            _ => Math.Max(state.IntervalDays + 1, (int)Math.Round(state.IntervalDays * grade switch
            {
                ReviewGrade.Hard => 1.2,
                ReviewGrade.Easy => ease * 1.3,
                _ => ease,
            })),
        };
        interval = Math.Min(interval, MaxIntervalDays);

        return new SrsState(state.Repetitions + 1, interval, ease, today.AddDays(interval), state.Lapses);
    }
}
