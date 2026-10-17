using System.Globalization;
using System.Text.RegularExpressions;

namespace ReceiptReader.Application.Validation;

/// <summary>
/// Turns whatever the model read into a Gregorian <see cref="DateOnly"/>.
/// Handles พ.ศ. years (2569 → 2026), 2-digit years ("13/10/69"), and Thai month names.
/// The raw printed text wins over the model's ISO guess, because models often mis-convert 2-digit BE years.
/// </summary>
public static partial class ThaiDateNormalizer
{
    private const int BuddhistOffset = 543;

    private static readonly (string[] Names, int Month)[] ThaiMonths =
    [
        (["มกราคม", "ม.ค.", "มค"], 1),
        (["กุมภาพันธ์", "ก.พ.", "กพ"], 2),
        (["มีนาคม", "มี.ค.", "มีค"], 3),
        (["เมษายน", "เม.ย.", "เมย"], 4),
        (["พฤษภาคม", "พ.ค.", "พค"], 5),
        (["มิถุนายน", "มิ.ย.", "มิย"], 6),
        (["กรกฎาคม", "ก.ค.", "กค"], 7),
        (["สิงหาคม", "ส.ค.", "สค"], 8),
        (["กันยายน", "ก.ย.", "กย"], 9),
        (["ตุลาคม", "ต.ค.", "ตค"], 10),
        (["พฤศจิกายน", "พ.ย.", "พย"], 11),
        (["ธันวาคม", "ธ.ค.", "ธค"], 12),
    ];

    [GeneratedRegex(@"(?<!\d)(\d{1,2})\s*[/\-.]\s*(\d{1,2})\s*[/\-.]\s*(\d{2,4})(?!\d)")]
    private static partial Regex NumericDate();

    [GeneratedRegex(@"(?<!\d)(\d{4})-(\d{1,2})-(\d{1,2})(?!\d)")]
    private static partial Regex IsoDate();

    [GeneratedRegex(@"(?<!\d)(\d{1,2})\s*([ก-๙.]+)\s*(\d{2,4})(?!\d)")]
    private static partial Regex ThaiMonthDate();

    public static DateOnly? Normalize(string? isoGuess, string? raw)
    {
        return FromRaw(raw) ?? FromIso(isoGuess) ?? FromIso(raw);
    }

    /// <summary>4-digit ≥ 2400 is พ.ศ.; 2-digit 60–99 is พ.ศ. 25xx, 00–59 is ค.ศ. 20xx.</summary>
    internal static int ResolveYear(int year, int digits)
    {
        if (digits <= 2)
            return year >= 60 ? 2500 + year - BuddhistOffset : 2000 + year;
        return year >= 2400 ? year - BuddhistOffset : year;
    }

    private static DateOnly? FromIso(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso)) return null;
        var m = IsoDate().Match(iso);
        if (!m.Success) return null;
        return Build(ResolveYear(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), 4),
                     int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                     int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture));
    }

    private static DateOnly? FromRaw(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var text = ToArabicDigits(raw).Replace("พ.ศ.", " ").Replace("ค.ศ.", " ");

        // Prefer ISO if the raw text already is ISO (some e-receipts print 2026-10-13).
        if (IsoDate().IsMatch(text)) return FromIso(text);

        var m = NumericDate().Match(text);
        if (m.Success)
        {
            var y = m.Groups[3].Value;
            return Build(ResolveYear(int.Parse(y, CultureInfo.InvariantCulture), y.Length),
                         int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),   // Thai order: d/m/y
                         int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
        }

        m = ThaiMonthDate().Match(text);
        if (m.Success)
        {
            var month = MatchThaiMonth(m.Groups[2].Value);
            if (month is null) return null;
            var y = m.Groups[3].Value;
            return Build(ResolveYear(int.Parse(y, CultureInfo.InvariantCulture), y.Length),
                         month.Value,
                         int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
        }
        return null;
    }

    private static int? MatchThaiMonth(string token)
    {
        var t = token.Trim();
        var compact = t.Replace(".", "");
        foreach (var (names, month) in ThaiMonths)
            foreach (var n in names)
                if (t == n || compact == n.Replace(".", "")) return month;
        return null;
    }

    private static DateOnly? Build(int year, int month, int day)
    {
        if (year is < 1900 or > 2200 || month is < 1 or > 12 || day < 1) return null;
        if (day > DateTime.DaysInMonth(year, month)) return null;
        return new DateOnly(year, month, day);
    }

    /// <summary>Thai digits ๐-๙ → 0-9.</summary>
    private static string ToArabicDigits(string s) =>
        string.Create(s.Length, s, static (span, src) =>
        {
            for (var i = 0; i < src.Length; i++)
            {
                var c = src[i];
                span[i] = c is >= '๐' and <= '๙' ? (char)('0' + (c - '๐')) : c;
            }
        });
}
