using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Rose.Domain.Lingo;
using Rose.Infrastructure.Lingo;

namespace Rose.UnitTests.Infrastructure;

public sealed class LingoDbContextTests : IDisposable
{
    // An in-memory SQLite database lives as long as its connection stays open.
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public LingoDbContextTests() => _connection.Open();

    private LingoDbContext NewContext() =>
        new(new DbContextOptionsBuilder<LingoDbContext>().UseSqlite(_connection).Options);

    [Fact]
    public void The_model_matches_the_last_migration()
    {
        // Fails when an entity or configuration changed without "dotnet ef migrations add".
        using var db = NewContext();
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Cards_round_trip_through_sqlite()
    {
        var today = new DateOnly(2026, 10, 21);
        await using (var db = NewContext())
        {
            await db.Database.MigrateAsync();
            db.Cards.Add(new Card("ja", "ねこ", "", "Cat", "", DateTimeOffset.UnixEpoch, today));
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            var repository = new EfLingoRepository(db);
            var card = Assert.Single(await repository.DueCardsAsync("ja", today, CancellationToken.None));
            Assert.Equal("neko", card.Reading);
            Assert.True(await repository.HasTermAsync("ja", "ねこ", null, CancellationToken.None));
            Assert.False(await repository.HasTermAsync("ja", "ねこ", card.Id, CancellationToken.None));
            Assert.Empty(await repository.DueCardsAsync("ja", today.AddDays(-1), CancellationToken.None));
        }
    }

    [Fact]
    public async Task Any_number_of_quizzes_a_day_are_kept()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        var day = new DateOnly(2026, 10, 21);
        db.QuizResults.Add(new QuizResult(day, "zh-Hans", QuizMode.Random, 5, 5, DateTimeOffset.UnixEpoch));
        db.QuizResults.Add(new QuizResult(day, "zh-Hans", QuizMode.Custom, 3, 5, DateTimeOffset.UnixEpoch));
        await db.SaveChangesAsync();

        await using var again = NewContext();
        var modes = await again.QuizResults.Select(r => r.Mode).ToListAsync();
        Assert.Equal([QuizMode.Random, QuizMode.Custom], modes.Order());
        Assert.Equal("Custom", await again.Database.SqlQueryRaw<string>("SELECT Mode AS Value FROM QuizResults WHERE Score = 3").SingleAsync());
    }

    [Fact]
    public async Task Upgrading_deletes_the_old_daily_quiz_scores_but_keeps_the_cards()
    {
        await using var db = NewContext();
        var migrator = db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();
        var all = db.Database.GetMigrations().ToList();
        await migrator.MigrateAsync(all[^2]);
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO QuizResults (Day, Language, Score, Total, CompletedAt) VALUES ('2026-10-20', 'ja', 4, 5, '2026-10-20 10:00:00+00:00')");
        db.Cards.Add(new Card("ja", "ねこ", "", "Cat", "", DateTimeOffset.UnixEpoch, new DateOnly(2026, 10, 21)));
        await db.SaveChangesAsync();

        await migrator.MigrateAsync(all[^1]);

        Assert.Equal(0, await db.QuizResults.CountAsync());
        Assert.Equal(1, await db.Cards.CountAsync());
    }

    public void Dispose() => _connection.Dispose();
}
