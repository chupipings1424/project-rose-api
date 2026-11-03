namespace Rose.Application.Habits;

/// <summary>Create / edit a habit. <c>remind_at</c> is "HH:mm" or empty for no nudge.</summary>
public sealed record HabitRequest(string? Name, string? Icon, string? Kind, int? Target, string? Unit, string? RemindAt);

/// <summary>How much was done on one day: 0/1 for check habits, 0-9999 for count habits.</summary>
public sealed record DayRequest(int? Value);