using Rose.Application.Abstractions;
using Rose.Application.Common;
using Rose.Domain.Common;
using Rose.Domain.Health;

namespace Rose.Application.Health;

/// <summary>Height, birth year, sex (<c>male</c> / <c>female</c>) and goal weight. Leave any of them empty.</summary>
public sealed record HealthProfileRequest(double? HeightCm, int? BirthYear, string? Sex, double? GoalKg);

/// <summary>A day's log. <c>fasting_hours</c>: 16 = 16:8, 23 = OMAD, null = no fast. Null weight = not weighed.</summary>
public sealed record HealthDayRequest(double? WeightKg, double? WalkKm, int? FastingHours);

/// <summary>Something eaten and its calories (typed in by hand).</summary>
public sealed record HealthMealRequest(string? Name, int? Kcal);

/// <summary>
/// The Health Tracker: a private log of meals, fasts, walks and weight, with calorie estimates
/// (see <see cref="HealthRules"/>). A singleton with one in-memory partition per user. Every change
/// returns the fresh overview, because one new meal can move every estimated weight after it.
/// </summary>
public sealed class HealthService(IRepository<HealthProfile> profiles, IRepository<HealthDay> days, TimeProvider clock) : IDisposable, IUserScopedCache
{
    private readonly RepositoryCache<HealthProfile> profileCache = new(profiles);
    private readonly RepositoryCache<HealthDay> dayCache = new(days);

    public async Task<HealthOverview> OverviewAsync(Guid owner, int? range = null, CancellationToken ct = default)
    {
        // Always profile first, then days: one order everywhere means no deadlocks.
        using var profile = await profileCache.OpenAsync(owner, ct);
        using var log = await dayCache.OpenAsync(owner, ct);
        return HealthRules.Overview(profile.Items.FirstOrDefault(), log.Items, clock.Today(), range ?? HealthRules.DefaultRange);
    }

    public async Task<HealthOverview> SaveProfileAsync(Guid owner, HealthProfileRequest request, CancellationToken ct = default)
    {
        var today = clock.Today();
        var input = HealthRules.ParseProfile(request.HeightCm, request.BirthYear, request.Sex, request.GoalKg, today);
        using var profile = await profileCache.OpenAsync(owner, ct);
        var current = profile.Items.FirstOrDefault();
        if (current is null)
        {
            current = new HealthProfile { Id = owner.ToString(), UpdatedAt = clock.GetUtcNow() };
            profile.Items.Add(current);
        }
        current.HeightCm = input.HeightCm;
        current.BirthYear = input.BirthYear;
        current.Sex = input.Sex;
        current.GoalKg = input.GoalKg;
        current.UpdatedAt = clock.GetUtcNow();
        await profile.SaveAsync(current, ct);

        using var log = await dayCache.OpenAsync(owner, ct);
        return HealthRules.Overview(current, log.Items, today);
    }

    public Task<HealthOverview> LogDayAsync(Guid owner, string? date, HealthDayRequest request, CancellationToken ct = default)
    {
        var input = HealthRules.ParseDay(request.WeightKg, request.WalkKm, request.FastingHours);
        return ChangeDayAsync(owner, date, day =>
        {
            day.WeightKg = input.WeightKg;
            day.WalkKm = input.WalkKm;
            day.FastingHours = input.FastingHours;
        }, ct);
    }

    public Task<HealthOverview> AddMealAsync(Guid owner, string? date, HealthMealRequest request, CancellationToken ct = default)
    {
        var input = HealthRules.ParseMeal(request.Name, request.Kcal);
        return ChangeDayAsync(owner, date, day =>
            day.Meals.Add(new HealthMeal(Guid.NewGuid().ToString(), input.Name, input.Kcal, clock.GetUtcNow())), ct);
    }

    public Task<HealthOverview> RemoveMealAsync(Guid owner, string? date, string mealId, CancellationToken ct = default) =>
        ChangeDayAsync(owner, date, day =>
        {
            if (day.Meals.RemoveAll(m => m.Id == mealId) == 0) throw new NotFoundException("That meal is already gone.");
        }, ct);

    public void Forget(Guid owner)
    {
        profileCache.Forget(owner);
        dayCache.Forget(owner);
    }

    public void Dispose()
    {
        profileCache.Dispose();
        dayCache.Dispose();
    }

    /// <summary>Finds (or starts) the day, applies the change and saves it. A day left empty is deleted.</summary>
    private async Task<HealthOverview> ChangeDayAsync(Guid owner, string? dateText, Action<HealthDay> change, CancellationToken ct)
    {
        var today = clock.Today();
        var date = HealthRules.ParseDate(dateText, today);
        using var profile = await profileCache.OpenAsync(owner, ct);
        using var log = await dayCache.OpenAsync(owner, ct);
        var day = log.Items.Find(d => d.Date == date);
        var isNew = day is null;
        day ??= new HealthDay { Id = Guid.NewGuid().ToString(), Date = date, UpdatedAt = clock.GetUtcNow() };

        change(day);
        day.UpdatedAt = clock.GetUtcNow();
        if (day.IsEmpty)
        {
            if (!isNew)
            {
                await log.DeleteAsync(day.Id, ct);
                log.Items.Remove(day);
            }
        }
        else
        {
            if (isNew) log.Items.Add(day);
            await log.SaveAsync(day, ct);
        }
        return HealthRules.Overview(profile.Items.FirstOrDefault(), log.Items, today);
    }
}