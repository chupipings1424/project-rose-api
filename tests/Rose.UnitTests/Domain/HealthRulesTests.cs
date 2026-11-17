using Rose.Domain.Common;
using Rose.Domain.Health;

namespace Rose.UnitTests.Domain;

public sealed class HealthRulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 16);
    private static readonly DateTimeOffset Noon = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    // 176 cm, 36 years old, male: at 80 kg the BMR is 800 + 1100 - 180 + 5 = 1,725 kcal.
    private static HealthProfile Profile() => new()
    {
        Id = "me",
        HeightCm = 176,
        BirthYear = 1990,
        Sex = HealthSex.Male,
        GoalKg = 75,
        UpdatedAt = Noon,
    };

    private static HealthDay Day(DateOnly date, double? weight = null, double km = 0, int? fast = null, params int[] meals) => new()
    {
        Id = date.ToString("O"),
        Date = date,
        WeightKg = weight,
        WalkKm = km,
        FastingHours = fast,
        Meals = [.. meals.Select((kcal, i) => new HealthMeal($"{date:O}-{i}", "Food", kcal, Noon.AddMinutes(i)))],
        UpdatedAt = Noon,
    };

    [Theory]
    [InlineData(16, "16:8")]
    [InlineData(18, "18:6")]
    [InlineData(20, "20:4")]
    [InlineData(23, "23:1 (OMAD)")]
    public void Fasting_label_shows_the_fasting_and_eating_hours(int hours, string label) =>
        Assert.Equal(label, HealthRules.FastingLabel(hours));

    [Fact]
    public void Bmr_uses_mifflin_st_jeor_and_needs_a_full_profile()
    {
        Assert.Equal(1725, HealthRules.Bmr(Profile(), 80, Today));

        var female = Profile();
        female.Sex = HealthSex.Female;
        Assert.Equal(1725 - 166, HealthRules.Bmr(female, 80, Today));

        var noHeight = Profile();
        noHeight.HeightCm = null;
        Assert.Null(HealthRules.Bmr(noHeight, 80, Today));
        Assert.Null(HealthRules.Bmr(Profile(), null, Today));
    }

    [Fact]
    public void Walking_burns_about_0_6_kcal_per_kg_per_km()
    {
        Assert.Equal(240, HealthRules.WalkKcal(80, 5));
        Assert.Equal(126, HealthRules.WalkKcal(null, 3)); // 70 kg until the first weigh-in
    }

    [Theory]
    [InlineData(55, 176, 17.8, "Underweight")]
    [InlineData(70, 176, 22.6, "Normal")]
    [InlineData(80, 176, 25.8, "Overweight")]
    [InlineData(95, 176, 30.7, "Obese")]
    public void Bmi_is_weight_over_height_squared(double kg, double cm, double bmi, string label)
    {
        Assert.Equal(bmi, HealthRules.Bmi(kg, cm));
        Assert.Equal(label, HealthRules.BmiLabel(bmi));
    }

    [Fact]
    public void A_day_adds_up_calories_in_and_out()
    {
        var overview = HealthRules.Overview(Profile(), [Day(Today, 80, 5, 16, 1000, 810)], Today);
        var day = overview.TodayLog;

        Assert.True(day.Logged);
        Assert.Equal(1810, day.KcalIn);
        Assert.Equal(240, day.WalkKcal);
        Assert.Equal(1725, day.Bmr);
        Assert.Equal(2070 + 240, day.KcalOut);
        Assert.Equal(-500, day.Balance);
        Assert.Equal("16:8", day.FastingLabel);
        Assert.Equal(80, day.WeightKg);
        Assert.False(day.WeightEstimated);
        Assert.Equal(25.8, overview.Bmi);
        Assert.Equal(5, overview.ToGoalKg);
    }

    [Fact]
    public void Weight_is_estimated_from_the_calorie_balance_until_the_next_weigh_in()
    {
        var days = new[]
        {
            Day(Today.AddDays(-3), 80, 5, null, 1000, 810), // -500 kcal
            Day(Today.AddDays(-2), null, 0, null, 2070),     // even
            Day(Today.AddDays(-1)),                          // nothing eaten logged = unknown, counts as even
            Day(Today, 79.5),
        };
        var overview = HealthRules.Overview(Profile(), days, Today);
        var byDate = overview.Days.ToDictionary(d => d.Date);

        Assert.Equal(79.94, byDate[Today.AddDays(-2)].WeightKg); // 80 - 500 / 7,700
        Assert.True(byDate[Today.AddDays(-2)].WeightEstimated);
        Assert.Equal(79.94, byDate[Today.AddDays(-1)].WeightKg);
        Assert.Equal(79.5, overview.LatestWeightKg);
        Assert.False(overview.LatestWeightEstimated);
    }

    [Fact]
    public void Weight_older_than_the_range_still_carries_forward()
    {
        var overview = HealthRules.Overview(Profile(), [Day(Today.AddDays(-100), 82)], Today, 7);

        Assert.Equal(7, overview.Days.Count);
        Assert.Equal(Today, overview.Days[0].Date);
        Assert.Equal(82, overview.LatestWeightKg);
        Assert.True(overview.LatestWeightEstimated);
    }

    [Fact]
    public void Without_a_profile_there_are_no_calories_out_but_the_log_still_works()
    {
        var overview = HealthRules.Overview(null, [Day(Today, 80, 2, 18, 600)], Today);

        Assert.Null(overview.TodayLog.KcalOut);
        Assert.Null(overview.TodayLog.Balance);
        Assert.Equal(600, overview.TodayLog.KcalIn);
        Assert.False(overview.Profile.Complete);
        Assert.Null(overview.Bmi);
    }

    [Fact]
    public void Fasting_streak_counts_days_in_a_row_and_waits_for_today()
    {
        var days = new[]
        {
            Day(Today.AddDays(-4), fast: 16),
            Day(Today.AddDays(-2), fast: 16),
            Day(Today.AddDays(-1), fast: 20),
        };
        Assert.Equal(2, HealthRules.Overview(null, days, Today).FastingStreak);
        Assert.Equal(3, HealthRules.Overview(null, [.. days, Day(Today, fast: 23)], Today).FastingStreak);
    }

    [Fact]
    public void The_week_sums_the_last_seven_days()
    {
        var days = new[]
        {
            Day(Today.AddDays(-7), 80, 10, 16, 3000), // outside the week
            Day(Today.AddDays(-1), null, 3, 16, 1500),
            Day(Today, null, 2, null, 2500),
        };
        var week = HealthRules.Overview(Profile(), days, Today).Week;

        Assert.Equal(2, week.DaysLogged);
        Assert.Equal(2000, week.AverageKcalIn);
        Assert.Equal(5, week.WalkKm);
        Assert.Equal(1, week.FastsDone);
    }

    [Theory]
    [InlineData(10.0, null, null)]
    [InlineData(null, -1.0, null)]
    [InlineData(null, null, 11)]
    [InlineData(null, null, 24)]
    public void Bad_day_input_is_rejected(double? weight, double? km, int? fast) =>
        Assert.Throws<ValidationException>(() => HealthRules.ParseDay(weight, km, fast));

    [Fact]
    public void Meals_need_a_name_and_sensible_calories()
    {
        Assert.Equal(new HealthMealInput("Rice", 200), HealthRules.ParseMeal(" Rice ", 200));
        Assert.Throws<ValidationException>(() => HealthRules.ParseMeal(" ", 200));
        Assert.Throws<ValidationException>(() => HealthRules.ParseMeal("Rice", null));
        Assert.Throws<ValidationException>(() => HealthRules.ParseMeal("Rice", 20_000));
    }

    [Fact]
    public void Dates_must_be_today_or_earlier()
    {
        Assert.Equal(Today, HealthRules.ParseDate("2026-11-16", Today));
        Assert.Throws<ValidationException>(() => HealthRules.ParseDate("2026-11-17", Today));
        Assert.Throws<ValidationException>(() => HealthRules.ParseDate("16/11/2026", Today));
    }

    [Fact]
    public void Profile_sex_must_be_male_female_or_empty()
    {
        Assert.Equal("female", HealthRules.ParseProfile(160, 1995, " Female ", null, Today).Sex);
        Assert.Throws<ValidationException>(() => HealthRules.ParseProfile(160, 1995, "x", null, Today));
        Assert.Throws<ValidationException>(() => HealthRules.ParseProfile(30, 1995, "", null, Today));
    }
}