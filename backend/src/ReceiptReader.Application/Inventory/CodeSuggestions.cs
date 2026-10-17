using System.Text;
using System.Text.RegularExpressions;
using ReceiptReader.Application.Abstractions;
using ReceiptReader.Domain.Entities;

namespace ReceiptReader.Application.Inventory;

/// <summary>A part of a product code: segment "brand", term "Lotus", code "123". Term null = not in the name.</summary>
public sealed record CodePart(string Segment, string? Term, string Code);

/// <param name="Known">The code came from what the company already uses for this term.</param>
public sealed record SuggestedPart(string Segment, string Label, string? Term, string Code, bool Known);

/// <param name="Warning">Something the user should look at, e.g. the code had to be changed to stay unique.</param>
public sealed record ProductCodeSuggestion(string Code, IReadOnlyList<SuggestedPart> Parts, string? Reason, string? Warning);

/// <summary>A term to learn: <see cref="ProductCodeTerm"/> without the bookkeeping.</summary>
public sealed record NewTerm(string Segment, string Term, string NormalizedTerm, string Code);

/// <summary>
/// What turns the AI's answer into a code the company can use: known terms keep the code they already have, new
/// ones get a code no other term of the segment uses, and the whole code is unique. Kept free of I/O so the rules
/// can be tested directly; the AI only proposes.
/// </summary>
public static partial class CodeSuggestions
{
    public const int MaxSegments = 6;
    public const int MaxSegmentLength = 6;
    public const int MaxTermLength = 100;
    public const int MaxKeyLength = 40;

    [GeneratedRegex("[^A-Z0-9]")]
    private static partial Regex NotCodeChar();

    [GeneratedRegex("[^0-9]")]
    private static partial Regex NotDigit();

    [GeneratedRegex("[^a-z0-9_]")]
    private static partial Regex NotKeyChar();

    // ---------------- scheme ----------------

    /// <summary>
    /// The segments as they will be stored: 1 to <see cref="MaxSegments"/>, labels required, lengths
    /// 1–<see cref="MaxSegmentLength"/>, keys cleaned and unique (a blank key gets one from its position).
    /// </summary>
    public static List<CodeSegment> CleanSegments(IEnumerable<CodeSegment>? segments)
    {
        var list = (segments ?? []).ToList();
        if (list.Count == 0) throw new BadRequestException("กำหนดส่วนของรหัสอย่างน้อย 1 ส่วน เช่น แบรนด์");
        if (list.Count > MaxSegments) throw new BadRequestException($"รหัสสินค้าแบ่งได้ไม่เกิน {MaxSegments} ส่วน");
        var keys = new HashSet<string>();
        var result = new List<CodeSegment>();
        for (var i = 0; i < list.Count; i++)
        {
            var s = list[i];
            var label = s.Label?.Trim() ?? "";
            if (label.Length == 0) throw new BadRequestException($"ตั้งชื่อส่วนที่ {i + 1} ของรหัส เช่น แบรนด์ สี");
            if (s.Length is < 1 or > MaxSegmentLength)
                throw new BadRequestException($"ส่วน {label} ยาวได้ 1–{MaxSegmentLength} ตัว");
            var key = Key(s.Key);
            if (key.Length == 0 || keys.Contains(key)) key = Unique($"part{i + 1}", keys);
            keys.Add(key);
            result.Add(new CodeSegment { Key = key, Label = Cut(label, 50), Length = s.Length, Digits = s.Digits });
        }
        return result;
    }

    private static string Key(string? key)
    {
        var k = NotKeyChar().Replace((key ?? "").Trim().ToLowerInvariant(), "");
        return Cut(k, MaxKeyLength);
    }

    private static string Unique(string key, ISet<string> taken)
    {
        var k = key;
        for (var n = 2; taken.Contains(k); n++) k = $"{key}_{n}";
        return k;
    }

    // ---------------- category ----------------

    /// <summary>
    /// A prefix for a category: the AI's answer cleaned to A–Z/0–9, or the Latin letters of the name when it gave
    /// nothing usable, then made unique against <paramref name="taken"/> by ending it in a digit.
    /// </summary>
    public static string Prefix(string? proposed, string name, IEnumerable<string> taken)
    {
        var used = taken.ToHashSet(StringComparer.Ordinal);
        var p = Cut(NotCodeChar().Replace((proposed ?? "").ToUpperInvariant(), ""), ProductCodes.MaxPrefixLength);
        if (p.Length == 0) p = Cut(NotCodeChar().Replace(name.ToUpperInvariant(), ""), ProductCodes.MaxPrefixLength);
        if (p.Length == 0) p = "CAT";
        if (!used.Contains(p)) return p;
        for (var n = 2; n < 1000; n++)
        {
            var tail = n.ToString();
            var candidate = Cut(p, ProductCodes.MaxPrefixLength - tail.Length) + tail;
            if (!used.Contains(candidate)) return candidate;
        }
        throw new ConflictException("หารหัสหมวดที่ยังไม่ซ้ำไม่ได้ — ตั้งเอง");
    }

    // ---------------- product ----------------

    /// <summary>
    /// The suggested code: <paramref name="prefix"/> followed by one part per segment, in the scheme's order.
    /// </summary>
    /// <param name="proposed">What the AI answered.</param>
    /// <param name="terms">What the company already uses, every segment.</param>
    /// <param name="takenCodes">Codes of the company's other products, with their names.</param>
    public static ProductCodeSuggestion Product(
        string prefix,
        IReadOnlyList<CodeSegment> segments,
        IReadOnlyList<CodePart> proposed,
        IReadOnlyList<ProductCodeTerm> terms,
        IReadOnlyDictionary<string, string> takenCodes,
        string? reason)
    {
        var bySegment = proposed
            .Where(p => !string.IsNullOrWhiteSpace(p.Segment))
            .GroupBy(p => p.Segment.Trim().ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First());

        var parts = new List<SuggestedPart>();
        foreach (var segment in segments)
        {
            var part = bySegment.GetValueOrDefault(segment.Key);
            var known = terms.Where(t => t.Segment == segment.Key).ToList();
            var term = Cut(ProductNames.Clean(part?.Term), MaxTermLength);
            if (term.Length == 0)
            {
                // Not in the name: a fixed-length segment keeps its place with zeros.
                parts.Add(new SuggestedPart(segment.Key, segment.Label, null, new string('0', segment.Length), false));
                continue;
            }

            var key = ProductNames.Key(term);
            if (known.FirstOrDefault(t => t.NormalizedTerm == key) is { } same)
            {
                parts.Add(new SuggestedPart(segment.Key, segment.Label, term, same.Code, true));
                continue;
            }

            var usedCodes = known.Select(t => t.Code)
                .Concat(parts.Where(p => p.Segment == segment.Key).Select(p => p.Code))
                .ToHashSet(StringComparer.Ordinal);
            parts.Add(new SuggestedPart(segment.Key, segment.Label, term, FreeCode(part!.Code, segment, usedCodes), false));
        }

        var code = Cut(prefix + string.Concat(parts.Select(p => p.Code)), ProductCodes.MaxLength);
        string? warning = null;
        if (takenCodes.TryGetValue(code, out var other))
        {
            var original = code;
            code = UniqueCode(code, takenCodes);
            warning = $"รหัส {original} ใช้กับสินค้า {other} แล้ว จึงเติม {code[original.Length..]} ท้ายรหัส — หรือเพิ่มรายละเอียดในชื่อให้ต่างกัน";
        }
        return new ProductCodeSuggestion(code, parts, string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(), warning);
    }

    /// <summary>
    /// A code for a new term, <see cref="CodeSegment.Length"/> long: the AI's one fitted to the segment, or the next
    /// one no other term of the segment uses (digits count up from it; letters end in a digit). All zeros means
    /// "not in the name", so a term never gets it.
    /// </summary>
    private static string FreeCode(string? proposed, CodeSegment segment, ISet<string> used)
    {
        var length = segment.Length;
        if (segment.Digits)
        {
            var digits = NotDigit().Replace(proposed ?? "", "");
            digits = digits.Length > length ? digits[..length] : digits.PadLeft(length, '0');
            var max = (int)Math.Pow(10, length);
            var start = int.Parse(digits);
            for (var i = 0; i < max; i++)
            {
                var n = (start + i) % max;
                if (n == 0) continue;
                var c = n.ToString().PadLeft(length, '0');
                if (!used.Contains(c)) return c;
            }
            throw new ConflictException($"ส่วน {segment.Label} ใช้รหัสครบทุกเลขแล้ว — เพิ่มความยาวของส่วนนี้");
        }

        var code = NotCodeChar().Replace((proposed ?? "").ToUpperInvariant(), "");
        if (code.Length == 0) code = "X";
        code = code.Length > length ? code[..length] : code.PadRight(length, 'X');
        if (!used.Contains(code) && code.Any(ch => ch != '0')) return code;
        const string tails = "123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        foreach (var t in tails)
        {
            var c = code[..(length - 1)] + t;
            if (!used.Contains(c)) return c;
        }
        throw new ConflictException($"หารหัสของ {segment.Label} ที่ยังไม่ซ้ำไม่ได้ — ตั้งเอง");
    }

    private static string UniqueCode(string code, IReadOnlyDictionary<string, string> taken)
    {
        for (var n = 2; n < 1000; n++)
        {
            var tail = n.ToString();
            var c = Cut(code, ProductCodes.MaxLength - tail.Length) + tail;
            if (!taken.ContainsKey(c)) return c;
        }
        throw new ConflictException("หารหัสที่ยังไม่ซ้ำไม่ได้ — ตั้งเอง");
    }

    // ---------------- learning ----------------

    /// <summary>
    /// The parts of a saved product worth remembering: those with a term and a code that is part of the product code,
    /// whose term the segment doesn't know yet and whose code no other term of the segment has. A term the company
    /// already coded is never re-coded from here.
    /// </summary>
    public static IReadOnlyList<NewTerm> NewTerms(string productCode, IEnumerable<CodePart>? parts, IReadOnlyList<ProductCodeTerm> known)
    {
        var result = new List<NewTerm>();
        foreach (var part in parts ?? [])
        {
            var segment = Key(part.Segment);
            var term = Cut(ProductNames.Clean(part.Term), MaxTermLength);
            var code = NotCodeChar().Replace((part.Code ?? "").ToUpperInvariant(), "");
            if (segment.Length == 0 || term.Length == 0 || code.Length is 0 or > 10) continue;
            if (code.All(c => c == '0') || !productCode.Contains(code, StringComparison.Ordinal)) continue;

            var key = ProductNames.Key(term);
            var inSegment = known.Where(t => t.Segment == segment)
                .Select(t => (t.NormalizedTerm, t.Code))
                .Concat(result.Where(t => t.Segment == segment).Select(t => (t.NormalizedTerm, t.Code)))
                .ToList();
            if (inSegment.Any(t => t.NormalizedTerm == key || t.Code == code)) continue;
            result.Add(new NewTerm(segment, term, key, code));
        }
        return result;
    }

    // ---------------- questionnaire ----------------

    /// <summary>
    /// The "how products differ" answer as text, one block per product type in the owner's order:
    /// "- เตียงนอน\nแบรนด์ รุ่น ขนาด". Only the owner's types: an answer for any other type is dropped, and a type
    /// the AI skipped is left out.
    /// </summary>
    public static string AttributeNotes(IReadOnlyList<string> types, IReadOnlyList<(string? Type, IReadOnlyList<string>? Attributes)> answer)
    {
        var blocks = new List<string>();
        foreach (var type in types)
        {
            var key = ProductNames.Key(type);
            var match = answer.FirstOrDefault(a => ProductNames.Key(a.Type) == key);
            if (match.Type is null) match = answer.FirstOrDefault(a => ProductNames.Key(a.Type) is { Length: > 0 } k && (k.Contains(key) || key.Contains(k)));
            var attributes = (match.Attributes ?? [])
                .Select(a => ProductNames.Clean(a))
                .Where(a => a.Length is > 0 and <= 40)
                .DistinctBy(a => a.ToLowerInvariant())
                .Take(6)
                .ToList();
            if (attributes.Count > 0) blocks.Add($"- {type}\n{string.Join(' ', attributes)}");
        }
        return string.Join('\n', blocks);
    }

    private static string Cut(string s, int max) => s.Length <= max ? s : s[..max];

    /// <summary>For prompts: a list as "- a" lines, or "(none)".</summary>
    internal static string Lines(IEnumerable<string> items)
    {
        var sb = new StringBuilder();
        foreach (var i in items) sb.Append("- ").AppendLine(i);
        return sb.Length == 0 ? "(none)\n" : sb.ToString();
    }
}
