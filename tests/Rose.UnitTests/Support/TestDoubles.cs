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

/// <summary>An in-memory <see cref="IDataFile{T}"/> that counts writes.</summary>
internal sealed class MemoryDataFile<T>(T initial) : IDataFile<T>
{
    public T Value { get; private set; } = initial;

    public int Writes { get; private set; }

    public T Read() => Value;

    public void Write(T value)
    {
        Value = value;
        Writes++;
    }
}

/// <summary>A throw-away folder deleted when the test ends.</summary>
internal sealed class TempDirectory : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("rose-tests-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch (IOException) { }
    }
}

internal static class Zones
{
    /// <summary>Berlin: DST starts on the last Sunday of March, ends on the last Sunday of October.</summary>
    public static TimeZoneInfo Berlin { get; } = TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");
}