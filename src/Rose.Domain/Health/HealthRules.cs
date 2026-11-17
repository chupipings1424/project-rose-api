using System.Globalization;
using Rose.Domain.Common;

namespace Rose.Domain.Health;

/// <summary>
/// The health maths - estimates, not medical advice. Pure functions: "today" is always passed in.
/// <list type="bullet">
/// <item>BMR (calories burned at rest): Mifflin-St Jeor, 10 x kg + 6.25 x cm - 5 x age + 5 (male) or - 161 (female).</item>
/// <item>Calories out: BMR x 1.2 (an office-type day) plus walking at 0.6 kcal per kg per km.</item>
/// <item>Weight on a day without a weigh-in: the day before plus (in - out) / 7,700 kcal per kg.
/// A day without any meal logged counts as "even" (it is unknown, not a fast).</item>
/// </list>
/// </summary>
public static class HealthRules
{
    public const double KcalPerKg = 7700;
    public const double ActivityFactor = 1.2;
    public const double WalkKcalPerKgKm = 0.6;

    /// <summary>Used for the walking calories until the first weigh-in.</summary>
    public const double DefaultWeightKg = 70;

    public const int MinFastingHours = 12;
    public const int MaxFastingHours = 23;
    public const int MaxKcal = 10_000;
    public const int MaxMealName = 80;
    public const int DefaultRange = 30;
    public const int MaxRange = 366;

    public static DateOnly ParseDate(string? text, DateOnly today)
    {
        if (!DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new ValidationException("The date must look like 2026-11-16.");
        if (date > today) throw new ValidationException("You can't log a day that hasn't happened yet.");
        if (date < today.AddYears(-10)) throw new ValidationException("That date is too long ago.");
        return date;
    }

    public static HealthDayInput ParseDay(double? weightKg, double? walkKm, int? fastingHours)
    {
        if (weightKg is { } w && (double.IsNaN(w) || w < 20 || w > 400)) throw new ValidationException("Weight must be between 20 and 400 kg.");
        var km = walkKm ?? 0;
        if (double.IsNaN(km) || km < 0 || km > 100) throw new ValidationException("Walking must be between 0 and 100 km.");
        if (fastingHours is { } h && (h < MinFastingHours || h > MaxFastingHours))
            throw new ValidationException($"A fast must be {MinFastingHours} to {MaxFastingHours} hours (16 = 16:8).");
        return new HealthDayInput(weightKg is { } kg ? Math.Round(kg, 2) : null, Math.Round(km, 2), fastingHours);
    }

    public static HealthMealInput ParseMeal(string? name, int? kcal)
    {
        var text = (name ?? "").Trim();
        if (text.Length == 0) throw new ValidationException("Say what you ate.");
        if (text.Length > MaxMealName) throw new ValidationException($"Keep the food under {MaxMealName} characters.");
        if (kcal is not { } k || k < 0 || k > MaxKcal) throw new ValidationException($"Calories must be between 0 and {MaxKcal:N0}.");
        return new HealthMealInput(text, k);
    }

    public static HealthProfileInput ParseProfile(double? heightCm, int? birthYear, string? sex, double? goalKg, DateOnly today)
    {
        if (heightCm is { } h && (double.IsNaN(h) || h < 50 || h > 250)) throw new ValidationException("Height must be between 50 and 250 cm.");
        if (birthYear is { } y && (y < 1900 || y > today.Year - 5)) throw new ValidationException("That birth year doesn't look right.");
        var s = (sex ?? "").Trim().ToLowerInvariant();
        if (s is not ("" or HealthSex.Male or HealthSex.Female)) throw new ValidationException("Sex must be male or female (used for the calorie formula).");
        if (goalKg is { } g && (double.IsNaN(g) || g < 20 || g > 400)) throw new ValidationException("Goal weight must be between 20 and 400 kg.");
        return new HealthProfileInput(heightCm, birthYear, s, goalKg);
    }

    /// <summary>16 -> "16:8", 23 -> "23:1 (OMAD)".</summary>
    public static string FastingLabel(int hours) => hours == 23 ? "23:1 (OMAD)" : $"{hours}:{24 - hours}";

    public static int? Age(HealthProfile? profile, DateOnly on) => profile?.BirthYear is { } y ? on.Year - y : null;

    public static bool IsComplete(HealthProfile? profile) =>
        profile is { HeightCm: not null, BirthYear: not null } && profile.Sex.Length > 0;

    /// <summary>Calories burned at rest (Mifflin-St Jeor), or null without a complete profile and a weight.</summary>
    public static double? Bmr(HealthProfile? profile, double? weightKg, DateOnly on)
    {
        if (!IsComplete(profile) || weightKg is not { } w) return null;
        var bmr = 10 * w + 6.25 * profile!.HeightCm!.Value - 5 * Age(profile, on)!.Value;
        return bmr + (profile.Sex == HealthSex.Male ? 5 : -161);
    }

    public static int WalkKcal(double? weightKg, double km) =>
        (int)Math.Round(WalkKcalPerKgKm * (weightKg ?? DefaultWeightKg) * km);

    public static double? Bmi(double? weightKg, double? heightCm) =>
        weightKg is { } w && heightCm is { } h and > 0 ? Math.Round(w / Math.Pow(h / 100, 2), 1) : null;

    public static string? BmiLabel(double? bmi) => bmi switch
    {
        null => null,
        < 18.5 => "Underweight",
        < 25 => "Normal",
        < 30 => "Overweight",
        _ => "Obese",
    };

    public static HealthProfileView View(HealthProfile? profile, DateOnly today) => new(
        profile?.HeightCm, profile?.BirthYear, Age(profile, today), profile?.Sex ?? "", profile?.GoalKg, IsComplete(profile));

    /// <summary>
    /// Everything the page shows: the last <paramref name="range"/> days (newest first), today,
    /// this week and the streak. Weights are carried forward from the first weigh-in, even if it is
    /// older than the range.
    /// </summary>
    public static HealthOverview Overview(HealthProfile? profile, IReadOnlyCollection<HealthDay> days, DateOnly today, int range = DefaultRange)
    {
        range = Math.Clamp(range, 1, MaxRange);
        var byDate = days.Where(d => d.Date <= today).ToDictionary(d => d.Date);
        var rangeStart = today.AddDays(1 - range);
        var start = byDate.Count == 0 ? rangeStart : DateOnly.FromDayNumber(Math.Min(byDate.Keys.Min().DayNumber, rangeStart.DayNumber));

        var views = new Dictionary<DateOnly, HealthDayView>();
        double? weight = null;
        double carry = 0;
        for (var date = start; date <= today; date = date.AddDays(1))
        {
            byDate.TryGetValue(date, out var day);
            var estimated = false;
            if (day?.WeightKg is { } real) weight = real;
            else if (weight is not null)
            {
                weight += carry / KcalPerKg;
                estimated = true;
            }
            var view = Day(profile, date, day, weight, estimated);
            carry = view.Balance ?? 0;
            views[date] = view;
        }

        var todayView = views[today];
        var week = Enumerable.Range(0, 7).Select(i => views.GetValueOrDefault(today.AddDays(-i))).OfType<HealthDayView>().ToList();
        var withMeals = week.Where(d => d.Meals.Count > 0).ToList();
        var outs = week.Where(d => d.Logged && d.KcalOut is not null).Select(d => d.KcalOut!.Value).ToList();
        var balances = week.Where(d => d.Balance is not null).Select(d => d.Balance!.Value).ToList();
        var weekView = new HealthWeekView(
            week.Count(d => d.Logged),
            withMeals.Count == 0 ? 0 : (int)Math.Round(withMeals.Average(d => d.KcalIn)),
            outs.Count == 0 ? null : (int)Math.Round(outs.Average()),
            balances.Count == 0 ? null : balances.Sum(),
            Math.Round(week.Sum(d => d.WalkKm), 2),
            week.Sum(d => d.WalkKcal),
            week.Count(d => d.FastingHours is not null));

        var bmi = Bmi(todayView.WeightKg, profile?.HeightCm);
        return new HealthOverview(
            today,
            View(profile, today),
            todayView,
            todayView.WeightKg,
            todayView.WeightEstimated,
            bmi,
            BmiLabel(bmi),
            todayView.WeightKg is { } now && profile?.GoalKg is { } goal ? Math.Round(now - goal, 1) : null,
            FastingStreak(byDate, today),
            weekView,
            [.. Enumerable.Range(0, range).Select(i => views[today.AddDays(-i)])]);
    }

    /// <summary>Days in a row with a finished fast, ending today (or yesterday while today is still open).</summary>
    public static int FastingStreak(IReadOnlyDictionary<DateOnly, HealthDay> byDate, DateOnly today)
    {
        bool Done(DateOnly d) => byDate.TryGetValue(d, out var day) && day.FastingHours is not null;
        var date = Done(today) ? today : today.AddDays(-1);
        var streak = 0;
        while (Done(date))
        {
            streak++;
            date = date.AddDays(-1);
        }
        return streak;
    }

    private static HealthDayView Day(HealthProfile? profile, DateOnly date, HealthDay? day, double? weight, bool estimated)
    {
        var meals = day?.Meals.OrderBy(m => m.At).Select(m => new HealthMealView(m.Id, m.Name, m.Kcal, m.At)).ToList() ?? [];
        var kcalIn = meals.Sum(m => m.Kcal);
        var km = day?.WalkKm ?? 0;
        var walk = WalkKcal(weight, km);
        var bmr = Bmr(profile, weight, date);
        int? kcalOut = bmr is { } b ? (int)Math.Round(b * ActivityFactor) + walk : null;
        int? balance = kcalOut is { } o && meals.Count > 0 ? kcalIn - o : null;
        return new HealthDayView(
            date,
            day is not null,
            meals,
            kcalIn,
            km,
            walk,
            bmr is { } r ? (int)Math.Round(r) : null,
            kcalOut,
            balance,
            day?.FastingHours,
            day?.FastingHours is { } h ? FastingLabel(h) : null,
            weight is { } w ? Math.Round(w, 2) : null,
            estimated);
    }
}