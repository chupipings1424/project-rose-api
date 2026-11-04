namespace Rose.Application.Common;

public static class ClockExtensions
{
    /// <summary>Today's date in the server's time zone (TZ=Asia/Manila in the container).</summary>
    public static DateOnly Today(this TimeProvider clock) => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
}