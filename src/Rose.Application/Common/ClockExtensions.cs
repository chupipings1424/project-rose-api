namespace Rose.Application.Common;

public static class ClockExtensions
{
    /// <summary>Today's date on this PC.</summary>
    public static DateOnly Today(this TimeProvider clock) => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
}