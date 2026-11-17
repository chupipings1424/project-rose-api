namespace Rose.Domain.Health;

/// <summary>What the calorie maths needs to know about the person. Every field is optional until filled in.</summary>
public sealed class HealthProfile
{
    /// <summary>The owner's id as text: there is exactly one per person.</summary>
    public required string Id { get; init; }
    public double? HeightCm { get; set; }
    public int? BirthYear { get; set; }

    /// <summary><see cref="HealthSex.Male"/> or <see cref="HealthSex.Female"/>, or empty when not given.</summary>
    public string Sex { get; set; } = "";
    public double? GoalKg { get; set; }
    public required DateTimeOffset UpdatedAt { get; set; }
}

public static class HealthSex
{
    public const string Male = "male";
    public const string Female = "female";
}

/// <summary>One day's log: what was eaten, the fast that was finished, the walk and (sometimes) the weight.</summary>
public sealed class HealthDay
{
    public required string Id { get; init; }
    public required DateOnly Date { get; init; }

    /// <summary>Weighed on the scale. Days without one get an estimate (see <see cref="HealthRules"/>).</summary>
    public double? WeightKg { get; set; }
    public double WalkKm { get; set; }

    /// <summary>Hours of the fast that was completed: 16 means 16:8. Null = no fast logged.</summary>
    public int? FastingHours { get; set; }
    public List<HealthMeal> Meals { get; set; } = [];
    public required DateTimeOffset UpdatedAt { get; set; }

    public bool IsEmpty => WeightKg is null && WalkKm == 0 && FastingHours is null && Meals.Count == 0;
}

public sealed record HealthMeal(string Id, string Name, int Kcal, DateTimeOffset At);

/// <summary>A validated day log (everything except the meals).</summary>
public sealed record HealthDayInput(double? WeightKg, double WalkKm, int? FastingHours);

/// <summary>A validated meal.</summary>
public sealed record HealthMealInput(string Name, int Kcal);

/// <summary>A validated profile.</summary>
public sealed record HealthProfileInput(double? HeightCm, int? BirthYear, string Sex, double? GoalKg);

public sealed record HealthProfileView(double? HeightCm, int? BirthYear, int? Age, string Sex, double? GoalKg, bool Complete);

public sealed record HealthMealView(string Id, string Name, int Kcal, DateTimeOffset At);

/// <summary>
/// One calendar day with everything worked out. <see cref="KcalOut"/> and <see cref="Balance"/> are
/// null when the profile is incomplete; <see cref="Balance"/> is also null when no meal was logged.
/// </summary>
public sealed record HealthDayView(
    DateOnly Date,
    bool Logged,
    IReadOnlyList<HealthMealView> Meals,
    int KcalIn,
    double WalkKm,
    int WalkKcal,
    int? Bmr,
    int? KcalOut,
    int? Balance,
    int? FastingHours,
    string? FastingLabel,
    double? WeightKg,
    bool WeightEstimated);

public sealed record HealthWeekView(
    int DaysLogged,
    int AverageKcalIn,
    int? AverageKcalOut,
    int? TotalBalance,
    double WalkKm,
    int WalkKcal,
    int FastsDone);

public sealed record HealthOverview(
    DateOnly Today,
    HealthProfileView Profile,
    HealthDayView TodayLog,
    double? LatestWeightKg,
    bool LatestWeightEstimated,
    double? Bmi,
    string? BmiLabel,
    double? ToGoalKg,
    int FastingStreak,
    HealthWeekView Week,
    IReadOnlyList<HealthDayView> Days);