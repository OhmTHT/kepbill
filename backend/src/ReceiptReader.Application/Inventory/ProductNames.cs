using System.Text;
using System.Text.RegularExpressions;

namespace ReceiptReader.Application.Inventory;

/// <summary>How product names are cleaned and compared. Same rule as user names (UserService.NameKey).</summary>
public static partial class ProductNames
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>The name as it will be shown: trimmed, runs of whitespace collapsed to one space.</summary>
    public static string Clean(string? name) =>
        Whitespace().Replace((name ?? "").Normalize(NormalizationForm.FormC), " ").Trim();

    /// <summary>What names are compared by: <see cref="Clean"/>, lower-cased ("เตียง  Lotus" = "เตียง lotus").</summary>
    public static string Key(string? name) => Clean(name).ToLowerInvariant();
}
