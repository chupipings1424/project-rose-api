namespace Rose.Application.Savings;

/// <summary>Create or edit a goal. <c>deadline</c> is <c>yyyy-MM-dd</c> or empty.</summary>
public sealed record GoalRequest(string? Name, string? Icon, decimal? Target, string? Deadline);

/// <summary>Add money (<c>kind</c> = "deposit") or take it out ("withdraw"). <c>date</c> defaults to today.</summary>
public sealed record EntryRequest(string? Kind, decimal? Amount, string? Date, string? Note);