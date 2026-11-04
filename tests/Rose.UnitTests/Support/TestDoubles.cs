using Rose.Application.Abstractions;

namespace Rose.UnitTests.Support;

/// <summary>A clock the test controls: set <see cref="Now"/> or <see cref="Advance"/> it.</summary>
internal sealed class FakeClock(DateTimeOffset now, TimeZoneInfo? zone = null) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();

    public override TimeZoneInfo LocalTimeZone { get; } = zone ?? TimeZoneInfo.Utc;

    public void Advance(TimeSpan by) => Now += by;
}

/// <summary>An in-memory <see cref="IRepository{T}"/> that counts writes. <see cref="Stored"/> is "the database".</summary>
internal sealed class MemoryRepository<T>(Func<T, string> key) : IRepository<T>
    where T : class
{
    public List<T> Stored { get; } = [];

    public int Writes { get; private set; }

    public Task<List<T>> LoadAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<List<T>>([.. Stored]);

    public Task SaveAsync(IReadOnlyCollection<T> items, CancellationToken cancellationToken = default)
    {
        foreach (var item in items)
        {
            var index = Stored.FindIndex(s => key(s) == key(item));
            if (index >= 0) Stored[index] = item;
            else Stored.Add(item);
        }
        Writes++;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        Stored.RemoveAll(s => key(s) == id);
        Writes++;
        return Task.CompletedTask;
    }
}

internal static class Zones
{
    /// <summary>Berlin: DST starts on the last Sunday of March, ends on the last Sunday of October.</summary>
    public static TimeZoneInfo Berlin { get; } = TimeZoneInfo.FindSystemTimeZoneById(
        OperatingSystem.IsWindows() ? "W. Europe Standard Time" : "Europe/Berlin");
}