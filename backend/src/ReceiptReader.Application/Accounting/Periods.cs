using System.Globalization;

namespace ReceiptReader.Application.Accounting;

/// <summary>Accounting months as "yyyy-MM" strings — sortable, readable in SQL, and what the UI shows.</summary>
public static class Periods
{
    public static string Of(DateOnly date) => $"{date.Year:D4}-{date.Month:D2}";

    public static bool TryParse(string? value, out DateOnly firstDay)
    {
        firstDay = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        return DateOnly.TryParseExact(value.Trim() + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out firstDay);
    }

    /// <summary>Canonical form, or null when the value isn't a month.</summary>
    public static string? Normalize(string? value) => TryParse(value, out var d) ? Of(d) : null;

    /// <summary>Whole months from <paramref name="from"/> to <paramref name="to"/> (negative when to is earlier).</summary>
    public static int MonthsBetween(string from, string to)
    {
        if (!TryParse(from, out var a) || !TryParse(to, out var b)) return 0;
        return (b.Year * 12 + b.Month) - (a.Year * 12 + a.Month);
    }

    public static (DateOnly Start, DateOnly End) Range(string period)
    {
        if (!TryParse(period, out var start)) throw new ArgumentException($"Invalid period '{period}'", nameof(period));
        return (start, start.AddMonths(1).AddDays(-1));
    }
}
