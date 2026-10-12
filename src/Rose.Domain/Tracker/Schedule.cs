using Rose.Domain.Common;

namespace Rose.Domain.Tracker;

/// <summary>The repayment-plan maths: dates per frequency and splitting a total into instalments.</summary>
public static class Schedule
{
    public static readonly IReadOnlyList<string> Frequencies = ["Weekly", "Bi-weekly", "Monthly", "Quarterly", "Yearly"];
    public static readonly IReadOnlyList<string> AmountModes = ["total", "per_payment"];

    /// <summary>The date <paramref name="n"/> steps of <paramref name="frequency"/> after <paramref name="start"/>.
    /// Months clamp to the month end (Jan 31 + 1 month = Feb 28/29).</summary>
    public static DateOnly NextDue(DateOnly start, string? frequency, int n = 1)
    {
        var key = new string((frequency ?? "").ToLowerInvariant().Where(char.IsLetter).ToArray());
        return key switch
        {
            "weekly" or "week" => start.AddDays(7 * n),
            "biweekly" or "fortnightly" => start.AddDays(14 * n),
            "monthly" or "month" => start.AddMonths(n),
            "quarterly" or "quarter" => start.AddMonths(3 * n),
            "yearly" or "year" or "annually" or "annual" => start.AddMonths(12 * n),
            _ => throw new ValidationException(
                $"Unknown frequency '{frequency}'. Use one of {string.Join(", ", Frequencies)}."),
        };
    }

    /// <summary>"monthly" -> "Monthly"; supported aliases ("fortnightly") are kept as typed; unknown ones throw.</summary>
    public static string CanonicalFrequency(string? frequency)
    {
        var key = (frequency ?? "").Trim();
        var match = Frequencies.FirstOrDefault(f => string.Equals(f, key, StringComparison.OrdinalIgnoreCase));
        if (match is not null) return match;
        NextDue(new DateOnly(2000, 1, 1), frequency);
        return key;
    }

    /// <summary>Splits a total into amounts that add back up exactly; the last one absorbs the leftover cents.</summary>
    public static double[] SplitTotal(double total, int instalments)
    {
        if (instalments < 1) throw new ValidationException("Number of payments must be at least 1.");
        var totalCents = (long)Math.Round(total * 100, MidpointRounding.ToEven);
        var baseCents = totalCents / instalments; // integer division truncates toward zero
        var cents = Enumerable.Repeat(baseCents, instalments).ToArray();
        cents[^1] += totalCents - (baseCents * instalments);
        return cents.Select(c => c / 100.0).ToArray();
    }

    /// <summary>Computes a plan without saving anything (used by the preview and by "create title").</summary>
    public static SchedulePreview Build(
        object? amount, object? instalments, string? frequency, object? startDate, string? amountMode, DateOnly today)
    {
        var count = Coerce.ToInt(instalments);
        if (count < 1) throw new ValidationException("Number of payments must be at least 1.");
        var value = Coerce.ToDouble(amount);
        if (value <= 0) throw new ValidationException("Amount must be greater than zero.");
        var mode = (string.IsNullOrEmpty(amountMode) ? "total" : amountMode).Trim().ToLowerInvariant();
        if (!AmountModes.Contains(mode))
            throw new ValidationException($"amount_mode must be one of {string.Join(", ", AmountModes)}.");
        var start = Coerce.ToDate(startDate) ?? today;

        double[] amounts;
        double total;
        if (mode == "total")
        {
            amounts = SplitTotal(value, count);
            total = Coerce.Round(value);
        }
        else
        {
            amounts = Enumerable.Repeat(Coerce.Round(value), count).ToArray();
            total = Coerce.Round(value * count);
        }

        var dates = new List<DateOnly> { start };
        for (var i = 1; i < count; i++) dates.Add(NextDue(start, frequency, i));

        return new SchedulePreview(
            CanonicalFrequency(frequency),
            mode,
            count,
            amounts.Select(a => Coerce.Round(a)).ToArray(),
            Coerce.Round(amounts[0]),
            Coerce.Round(amounts[^1]),
            total,
            dates.Select(Coerce.IsoDate).ToArray(),
            Coerce.IsoDate(dates[0]),
            Coerce.IsoDate(dates[^1]));
    }
}

public sealed record SchedulePreview(
    string Frequency,
    string AmountMode,
    int Instalments,
    double[] Amounts,
    double PerPayment,
    double FinalPayment,
    double Total,
    string[] Dates,
    string FirstDue,
    string FinalDue);