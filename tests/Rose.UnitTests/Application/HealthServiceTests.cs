using Rose.Application.Health;
using Rose.Application.Preferences;
using Rose.Domain.Common;
using Rose.Domain.Health;
using Rose.Domain.Preferences;
using Rose.UnitTests.Support;

namespace Rose.UnitTests.Application;

public sealed class HealthServiceTests : IDisposable
{
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 11, 16, 9, 0, 0, TimeSpan.Zero));
    private readonly MemoryRepository<HealthProfile> _profiles = new(p => p.Id);
    private readonly MemoryRepository<HealthDay> _days = new(d => d.Id);
    private readonly HealthService _service;

    public HealthServiceTests() => _service = new HealthService(_profiles, _days, _clock);

    public void Dispose() => _service.Dispose();

    [Fact]
    public async Task Logging_a_day_and_meals_saves_one_day()
    {
        await _service.LogDayAsync(TestUsers.Alice, "2026-11-16", new HealthDayRequest(80, 3, 16));
        await _service.AddMealAsync(TestUsers.Alice, "2026-11-16", new HealthMealRequest("Chicken adobo", 450));
        var overview = await _service.AddMealAsync(TestUsers.Alice, "2026-11-16", new HealthMealRequest("Rice", 200));

        var day = Assert.Single(_days.Stored);
        Assert.Equal(2, day.Meals.Count);
        Assert.Equal(650, overview.TodayLog.KcalIn);
        Assert.Equal("16:8", overview.TodayLog.FastingLabel);
        Assert.Equal(80, overview.LatestWeightKg);
    }

    [Fact]
    public async Task Removing_the_last_thing_from_a_day_deletes_it()
    {
        var overview = await _service.AddMealAsync(TestUsers.Alice, "2026-11-15", new HealthMealRequest("Bread", 150));
        var meal = overview.Days.Single(d => d.Date == new DateOnly(2026, 11, 15)).Meals.Single();

        overview = await _service.RemoveMealAsync(TestUsers.Alice, "2026-11-15", meal.Id);

        Assert.Empty(_days.Stored);
        Assert.False(overview.Days.Single(d => d.Date == new DateOnly(2026, 11, 15)).Logged);
        await Assert.ThrowsAsync<NotFoundException>(() => _service.RemoveMealAsync(TestUsers.Alice, "2026-11-15", meal.Id));
    }

    [Fact]
    public async Task The_profile_turns_on_calories_out()
    {
        await _service.LogDayAsync(TestUsers.Alice, "2026-11-16", new HealthDayRequest(80, 0, null));
        var overview = await _service.SaveProfileAsync(TestUsers.Alice, new HealthProfileRequest(176, 1990, "male", 75));

        Assert.True(overview.Profile.Complete);
        Assert.Equal(36, overview.Profile.Age);
        Assert.Equal(2070, overview.TodayLog.KcalOut);
        Assert.Equal(TestUsers.Alice.ToString(), _profiles.Stored.Single().Id);
    }

    [Fact]
    public async Task Each_person_has_their_own_log()
    {
        await _service.LogDayAsync(TestUsers.Alice, "2026-11-16", new HealthDayRequest(80, 0, 16));

        var bob = await _service.OverviewAsync(TestUsers.Bob);

        Assert.False(bob.TodayLog.Logged);
        Assert.Empty(_days.StoredFor(TestUsers.Bob));
    }

    [Fact]
    public async Task Future_days_are_refused() =>
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.LogDayAsync(TestUsers.Alice, "2026-11-17", new HealthDayRequest(80, 0, null)));
}

public sealed class AppPrefsServiceTests : IDisposable
{
    private readonly MemoryRepository<AppPrefs> _repo = new(p => p.Id);
    private readonly AppPrefsService _service;

    public AppPrefsServiceTests() =>
        _service = new AppPrefsService(_repo, new FakeClock(new DateTimeOffset(2026, 11, 16, 9, 0, 0, TimeSpan.Zero)));

    public void Dispose() => _service.Dispose();

    [Fact]
    public async Task Nothing_is_hidden_until_you_choose()
    {
        var prefs = await _service.GetAsync(TestUsers.Alice);

        Assert.Empty(prefs.Hidden);
        Assert.Null(prefs.UpdatedAt);
    }

    [Fact]
    public async Task Hidden_apps_are_cleaned_saved_and_replaced()
    {
        await _service.SetAsync(TestUsers.Alice, new AppPrefsRequest([" Savings ", "habits", "savings"]));
        Assert.Equal(["habits", "savings"], (await _service.GetAsync(TestUsers.Alice)).Hidden);

        await _service.SetAsync(TestUsers.Alice, new AppPrefsRequest([]));
        Assert.Empty(Assert.Single(_repo.Stored).Hidden);
        Assert.Empty((await _service.GetAsync(TestUsers.Bob)).Hidden);
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("<script>")]
    [InlineData("")]
    public async Task Odd_ids_are_refused(string id) =>
        await Assert.ThrowsAsync<ValidationException>(() => _service.SetAsync(TestUsers.Alice, new AppPrefsRequest([id])));
}