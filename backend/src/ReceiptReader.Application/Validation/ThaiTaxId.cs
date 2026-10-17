namespace ReceiptReader.Application.Validation;

/// <summary>Thai 13-digit tax ID / national ID (เลขประจำตัวผู้เสียภาษี) helpers.</summary>
public static class ThaiTaxId
{
    /// <summary>
    /// Strips dashes/spaces. Returns null when nothing digit-like is left. Keeps only the first 13 digits:
    /// receipts often print the branch right after the ID ("0100568000322 สาขา 00000").
    /// </summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var digits = new string(value.Where(char.IsAsciiDigit).Take(13).ToArray());
        return digits.Length == 0 ? null : digits;
    }

    /// <summary>
    /// Mod-11 checksum: sum(d[i] * (13 - i)) for the first 12 digits,
    /// check digit = (11 - sum % 11) % 10.
    /// </summary>
    public static bool IsValid(string? value)
    {
        var digits = Normalize(value);
        if (digits is null || digits.Length != 13) return false;

        var sum = 0;
        for (var i = 0; i < 12; i++) sum += (digits[i] - '0') * (13 - i);
        var check = (11 - sum % 11) % 10;
        return check == digits[12] - '0';
    }
}

public static class ThaiBranch
{
    /// <summary>Column size of the branch fields.</summary>
    public const int MaxLength = 50;

    /// <summary>"สำนักงานใหญ่"/"Head office" → "00000"; "สาขา 12" → "00012".</summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();
        if (v.Contains("ใหญ่") || v.Contains("head", StringComparison.OrdinalIgnoreCase)) return "00000";
        var digits = new string(v.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length is > 0 and <= 5) return digits.PadLeft(5, '0');
        return v.Length <= MaxLength ? v : v[..MaxLength];
    }
}
