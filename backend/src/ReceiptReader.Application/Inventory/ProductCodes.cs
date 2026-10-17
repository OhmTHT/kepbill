using System.Text.RegularExpressions;
using ReceiptReader.Application.Abstractions;

namespace ReceiptReader.Application.Inventory;

/// <summary>Product code rules. Kept free of I/O so they can be tested directly.</summary>
public static partial class ProductCodes
{
    public const int MaxLength = 30;
    public const int MaxPrefixLength = 6;

    /// <summary>
    /// Upper-case letters, digits and "-" only, starting with a letter or digit: a USB scanner types the code as
    /// keystrokes, and these are the characters every keyboard layout and scanner agree on.
    /// </summary>
    [GeneratedRegex("^[A-Z0-9][A-Z0-9-]*$")]
    private static partial Regex CodePattern();

    [GeneratedRegex("^[A-Z0-9]+$")]
    private static partial Regex PrefixPattern();

    public static bool IsValid(string? code) =>
        code is { Length: > 0 and <= MaxLength } && CodePattern().IsMatch(code);

    /// <summary>A category prefix: 1 to <see cref="MaxPrefixLength"/> letters and digits, upper-cased. Required.</summary>
    public static string NormalizePrefix(string? prefix)
    {
        var p = prefix?.Trim().ToUpperInvariant() ?? "";
        if (p.Length == 0) throw new BadRequestException("กรอกรหัสหมวด เช่น BED");
        if (p.Length > MaxPrefixLength || !PrefixPattern().IsMatch(p))
            throw new BadRequestException($"รหัสหมวดใช้ได้เฉพาะ A–Z และ 0–9 ไม่เกิน {MaxPrefixLength} ตัว");
        return p;
    }

    /// <summary>A product code as the user typed it: trimmed and upper-cased, then checked. Required.</summary>
    public static string NormalizeCode(string? code)
    {
        var c = code?.Trim().ToUpperInvariant() ?? "";
        if (c.Length == 0) throw new BadRequestException("กรอกรหัสสินค้า หรือกดให้ AI แนะนำ");
        if (!IsValid(c)) throw new BadRequestException($"รหัสสินค้าใช้ได้เฉพาะ A–Z, 0–9 และ - ไม่เกิน {MaxLength} ตัว");
        return c;
    }

    // ---------------- scanning ----------------

    // Kedmanee (the standard Thai layout): what each key types with the Thai layout switched on, against what it
    // types in English. A USB scanner sends key presses, so a label scanned while Windows is in Thai arrives as
    // "ฺฎฏขจจจๅ" instead of "BED-0001".
    private const string LatinKeys = "`1234567890-=qwertyuiop[]\\asdfghjkl;'zxcvbnm,./";
    private const string ThaiKeys = "_ๅ/-ภถุึคตจขชๆไำพะัีรนยบลฃฟหกดเ้่าสวงผปแอิืทมใฝ";
    private const string LatinShiftKeys = "~!@#$%^&*()_+QWERTYUIOP{}|ASDFGHJKL:\"ZXCVBNM<>?";
    private const string ThaiShiftKeys = "%+๑๒๓๔ู฿๕๖๗๘๙๐\"ฎฑธํ๊ณฯญฐ,ฅฤฆฏโฌ็๋ษศซ.()ฉฮฺ์?ฒฬฦ";

    private static readonly Dictionary<char, char> ThaiToLatin = BuildThaiToLatin();

    private static Dictionary<char, char> BuildThaiToLatin()
    {
        var map = new Dictionary<char, char>();
        for (var i = 0; i < ThaiKeys.Length; i++) map[ThaiKeys[i]] = LatinKeys[i];
        for (var i = 0; i < ThaiShiftKeys.Length; i++) map[ThaiShiftKeys[i]] = LatinShiftKeys[i];
        return map;
    }

    /// <summary>
    /// The codes a scanned or typed text may stand for, most likely first: the text itself, then the same key
    /// presses read on an English keyboard (when the scanner typed into the Thai layout). A valid code without
    /// Thai letters is taken as typed: "-" and "/" are Thai-layout keys too, but in "BED-0001" they mean themselves.
    /// </summary>
    public static IReadOnlyList<string> ScanCandidates(string? raw)
    {
        var text = raw?.Trim() ?? "";
        if (text.Length == 0) return [];
        var asTyped = text.ToUpperInvariant();
        if (IsValid(asTyped) && !text.Any(c => c is >= '฀' and <= '๿')) return [asTyped];
        var asEnglish = new string(text.Select(c => ThaiToLatin.TryGetValue(c, out var l) ? l : c).ToArray()).ToUpperInvariant();
        return asEnglish == asTyped ? [asTyped] : [asTyped, asEnglish];
    }
}
