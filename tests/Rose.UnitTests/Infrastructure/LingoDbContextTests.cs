using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
    public async Task Only_one_quiz_result_per_day_and_language()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        var day = new DateOnly(2026, 10, 21);
        db.QuizResults.Add(new QuizResult(day, "ja", 5, 5, DateTimeOffset.UnixEpoch));
        db.QuizResults.Add(new QuizResult(day, "ja", 3, 5, DateTimeOffset.UnixEpoch));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    public void Dispose() => _connection.Dispose();
}
