using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using ReceiptReader.Application.Abstractions;
using ReceiptReader.Domain.Entities;

namespace ReceiptReader.Application.Inventory;

public sealed record CodeSegmentDto(string Key, string Label, int Length, bool Digits);

public sealed record CodeTermDto(Guid Id, string Segment, string Term, string Code);

/// <param name="Terms">What the company has learned so far: แบรนด์ Lotus = 123 …</param>
public sealed record CodeSchemeDto(
    string BusinessType, IReadOnlyList<string> ProductTypes, string? Notes,
    string? CategoryNameHint, string? ProductNameHint, string? CodeHint,
    IReadOnlyList<CodeSegmentDto> Segments, IReadOnlyList<CodeTermDto> Terms, DateTimeOffset UpdatedAt);

/// <summary>The questionnaire: what the business is and sells.</summary>
public sealed record SchemeQuestion(string BusinessType, IReadOnlyList<string>? ProductTypes, string? Notes);

/// <param name="Exists">The company already has a category of this name (its prefix is the one shown).</param>
public sealed record CategoryProposalDto(string Name, string CodePrefix, bool Exists);

public sealed record CodeExampleDto(string Name, string Code);

/// <summary>What the AI proposes from the questionnaire; nothing is saved until the user saves it.</summary>
public sealed record SchemeProposalDto(
    string? CategoryNameHint, string? ProductNameHint, string? CodeHint,
    IReadOnlyList<CodeSegmentDto> Segments, IReadOnlyList<CategoryProposalDto> Categories, IReadOnlyList<CodeExampleDto> Examples);

/// <param name="NewCategories">Categories to create along with it (from the proposal); names that exist are skipped.</param>
public sealed record CodeSchemeInput(
    string BusinessType, IReadOnlyList<string>? ProductTypes, string? Notes,
    string? CategoryNameHint, string? ProductNameHint, string? CodeHint,
    IReadOnlyList<CodeSegmentDto>? Segments, IReadOnlyList<ProductCategoryInput>? NewCategories = null);

/// <param name="CategoryId">The category being edited, so its own prefix doesn't count as taken.</param>
public sealed record CategoryCodeRequest(string Name, Guid? CategoryId = null);

public sealed record CategoryCodeSuggestion(string CodePrefix, string? Reason);

/// <param name="Notes">The "how products differ" answer, one block per product type.</param>
public sealed record AttributeNotesSuggestion(string Notes);

/// <param name="ProductId">The product being edited, so its own code doesn't count as taken.</param>
public sealed record ProductCodeRequest(Guid CategoryId, string Name, Guid? ProductId = null);

/// <summary>
/// The company's naming and code pattern (from the questionnaire) and the AI suggestions built on it. Without a
/// saved pattern the suggestions are off: the AI would only guess.
/// </summary>
public sealed class CodeSchemeService(IAppDbContext db, ILanguageModel llm, InventoryService inventory, IClock clock)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private const string NotSetUp = "ตอบแบบสอบถามรูปแบบรหัสก่อน จึงจะใช้ AI แนะนำรหัสได้";

    /// <summary>The company's pattern, or null when the questionnaire hasn't been answered.</summary>
    public async Task<CodeSchemeDto?> GetAsync(Guid companyId, CancellationToken ct)
    {
        var scheme = await db.ProductCodeSchemes.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == companyId, ct);
        return scheme is null ? null : await ToDtoAsync(scheme, ct);
    }

    /// <summary>Asks the AI for a pattern from the questionnaire, taking what the company already has into account.</summary>
    public async Task<SchemeProposalDto> ProposeAsync(Guid companyId, SchemeQuestion q, CancellationToken ct)
    {
        await RequireCompanyAsync(companyId, ct);
        var businessType = Required(q.BusinessType, 200, "กรอกประเภทธุรกิจ เช่น ร้านเฟอร์นิเจอร์");
        var types = CleanList(q.ProductTypes);
        if (types.Count == 0) throw new BadRequestException("กรอกประเภทสินค้าที่ขายอย่างน้อย 1 อย่าง เช่น เตียง");
        var current = await db.ProductCodeSchemes.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == companyId, ct);
        var categories = await CategoriesAsync(companyId, null, ct);
        var products = await db.Products.AsNoTracking()
            .Where(p => p.CompanyId == companyId)
            .OrderByDescending(p => p.CreatedAt).Take(30)
            .Select(p => new { p.Code, p.Name, Category = p.Category!.Name })
            .ToListAsync(ct);

        var a = await AskAsync<SchemeAnswer>(
            CodePrompts.Scheme(businessType, types, Optional(q.Notes, 1000), current, categories,
                products.Select(p => (p.Code, p.Name, p.Category)).ToList()),
            CodePrompts.SchemeSchema(), ct);

        List<CodeSegment> segments;
        try
        {
            segments = CodeSuggestions.CleanSegments((a.Segments ?? [])
                .Where(s => !string.IsNullOrWhiteSpace(s.Label))
                .Take(4)
                .Select(s => new CodeSegment { Key = s.Key ?? "", Label = s.Label!, Length = Math.Clamp(s.Length, 1, 4), Digits = s.Digits }));
        }
        catch (BadRequestException)
        {
            segments = [];
        }
        if (segments.Count == 0)
            segments =
            [
                new CodeSegment { Key = "brand", Label = "แบรนด์", Length = 3, Digits = true },
                new CodeSegment { Key = "color", Label = "สี", Length = 3, Digits = true },
            ];

        // Existing categories keep their prefix; new ones get one no other category has.
        var taken = categories.Select(c => c.CodePrefix).ToHashSet();
        var proposed = new List<CategoryProposalDto>();
        foreach (var c in a.Categories ?? [])
        {
            var name = Optional(c.Name, 200);
            if (name is null || proposed.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
            if (categories.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is { } existing)
            {
                proposed.Add(new CategoryProposalDto(existing.Name, existing.CodePrefix, true));
                continue;
            }
            // Only what the owner sells: the AI is told so, and a category of any other type is dropped here.
            if (!types.Any(t => name.Contains(t, StringComparison.OrdinalIgnoreCase))) continue;
            var prefix = CodeSuggestions.Prefix(c.CodePrefix, name, taken);
            taken.Add(prefix);
            proposed.Add(new CategoryProposalDto(name, prefix, false));
        }

        // Built by the same rules as a real suggestion, so the examples always match the segments shown.
        var examples = (a.Examples ?? [])
            .Where(e => !string.IsNullOrWhiteSpace(e.Name))
            .Take(5)
            .Select(e => new CodeExampleDto(e.Name!.Trim(), CodeSuggestions.Product(
                new string((e.CategoryPrefix ?? "").ToUpperInvariant().Where(char.IsAsciiLetterOrDigit).Take(ProductCodes.MaxPrefixLength).ToArray()),
                segments, ToParts(e.Parts), [], new Dictionary<string, string>(), null).Code))
            .ToList();

        return new SchemeProposalDto(Optional(a.CategoryNameHint, 300), Optional(a.ProductNameHint, 300),
            Optional(a.CodeHint, 500), segments.Select(ToDto).ToList(), proposed, examples);
    }

    /// <summary>
    /// Saves the pattern (and creates the chosen categories) in one go. Learned codes of a segment that was taken
    /// out go with it.
    /// </summary>
    public async Task<CodeSchemeDto> SaveAsync(Guid companyId, CodeSchemeInput input, CancellationToken ct)
    {
        await RequireCompanyAsync(companyId, ct);
        var businessType = Required(input.BusinessType, 200, "กรอกประเภทธุรกิจ เช่น ร้านเฟอร์นิเจอร์");
        var types = CleanList(input.ProductTypes);
        if (types.Count == 0) throw new BadRequestException("กรอกประเภทสินค้าที่ขายอย่างน้อย 1 อย่าง เช่น เตียง");
        var segments = CodeSuggestions.CleanSegments((input.Segments ?? [])
            .Select(s => new CodeSegment { Key = s.Key, Label = s.Label, Length = s.Length, Digits = s.Digits }));

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var scheme = await db.ProductCodeSchemes.FirstOrDefaultAsync(s => s.CompanyId == companyId, ct);
        if (scheme is null)
        {
            scheme = new ProductCodeScheme { CompanyId = companyId };
            db.ProductCodeSchemes.Add(scheme);
        }
        scheme.BusinessType = businessType;
        scheme.ProductTypes = types;
        scheme.Notes = Optional(input.Notes, 1000);
        scheme.CategoryNameHint = Optional(input.CategoryNameHint, 300);
        scheme.ProductNameHint = Optional(input.ProductNameHint, 300);
        scheme.CodeHint = Optional(input.CodeHint, 500);
        scheme.Segments = segments;
        scheme.UpdatedAt = clock.Now;

        var keys = segments.Select(s => s.Key).ToList();
        db.ProductCodeTerms.RemoveRange(await db.ProductCodeTerms
            .Where(t => t.CompanyId == companyId && !keys.Contains(t.Segment)).ToListAsync(ct));
        await db.SaveChangesAsync(ct);

        var names = await db.ProductCategories.Where(c => c.CompanyId == companyId).Select(c => c.Name).ToListAsync(ct);
        foreach (var c in input.NewCategories ?? [])
        {
            if (names.Any(n => n.Equals(c.Name?.Trim(), StringComparison.OrdinalIgnoreCase))) continue;
            var created = await inventory.CreateCategoryAsync(companyId, c with { Active = null }, ct);
            names.Add(created.Name);
        }
        await tx.CommitAsync(ct);
        return await ToDtoAsync(scheme, ct);
    }

    /// <summary>Forgets the pattern and everything learned: the AI suggestions are off until it is set up again.</summary>
    public async Task DeleteAsync(Guid companyId, CancellationToken ct)
    {
        await db.ProductCodeTerms.Where(t => t.CompanyId == companyId).ExecuteDeleteAsync(ct);
        await db.ProductCodeSchemes.Where(s => s.CompanyId == companyId).ExecuteDeleteAsync(ct);
    }

    /// <summary>Forgets one learned code; the next product with that term gets a code from the AI again.</summary>
    public async Task DeleteTermAsync(Guid companyId, Guid termId, CancellationToken ct)
    {
        var term = await db.ProductCodeTerms.FirstOrDefaultAsync(t => t.Id == termId && t.CompanyId == companyId, ct)
                   ?? throw new NotFoundException($"ไม่พบรหัสที่จำไว้ {termId}");
        db.ProductCodeTerms.Remove(term);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// A first answer to "how do products differ" for the questionnaire, per product type the owner listed
    /// (เตียงนอน: แบรนด์ รุ่น ขนาด). Works before the pattern is saved: it is part of answering the questionnaire.
    /// </summary>
    public async Task<AttributeNotesSuggestion> SuggestNotesAsync(Guid companyId, SchemeQuestion q, CancellationToken ct)
    {
        await RequireCompanyAsync(companyId, ct);
        var businessType = Required(q.BusinessType, 200, "กรอกประเภทธุรกิจ เช่น ร้านเฟอร์นิเจอร์");
        var types = CleanList(q.ProductTypes);
        if (types.Count == 0) throw new BadRequestException("กรอกประเภทสินค้าที่ขายอย่างน้อย 1 อย่าง เช่น เตียง");
        var a = await AskAsync<AttributesAnswer>(CodePrompts.Attributes(businessType, types, Optional(q.Notes, 1000)),
            CodePrompts.AttributesSchema(), ct);
        var notes = CodeSuggestions.AttributeNotes(types,
            (a.Types ?? []).Select(t => (t.Type, (IReadOnlyList<string>?)t.Attributes)).ToList());
        if (notes.Length == 0) throw new UnavailableException("AI ไม่ได้แนะนำอะไรมา ลองกดอีกครั้ง");
        return new AttributeNotesSuggestion(notes.Length > 1000 ? notes[..1000] : notes);
    }

    public async Task<CategoryCodeSuggestion> SuggestCategoryAsync(Guid companyId, CategoryCodeRequest req, CancellationToken ct)
    {
        var scheme = await RequireSchemeAsync(companyId, ct);
        var name = Required(req.Name, 200, "กรอกชื่อหมวดก่อน แล้วค่อยให้ AI แนะนำรหัส");
        var others = await CategoriesAsync(companyId, req.CategoryId, ct);
        var a = await AskAsync<CategoryAnswer>(CodePrompts.Category(name, scheme, others), CodePrompts.CategorySchema(), ct);
        return new CategoryCodeSuggestion(CodeSuggestions.Prefix(a.CodePrefix, name, others.Select(c => c.CodePrefix)), Optional(a.Reason, 300));
    }

    public async Task<ProductCodeSuggestion> SuggestProductAsync(Guid companyId, ProductCodeRequest req, CancellationToken ct)
    {
        var scheme = await RequireSchemeAsync(companyId, ct);
        var name = Required(ProductNames.Clean(req.Name), 300, "กรอกชื่อสินค้าก่อน แล้วค่อยให้ AI แนะนำรหัส");
        var category = await db.ProductCategories.AsNoTracking()
                           .FirstOrDefaultAsync(c => c.Id == req.CategoryId && c.CompanyId == companyId, ct)
                       ?? throw new BadRequestException("เลือกหมวดสินค้าก่อน");
        var terms = await db.ProductCodeTerms.AsNoTracking().Where(t => t.CompanyId == companyId).OrderBy(t => t.Code).ToListAsync(ct);
        var products = await db.Products.AsNoTracking()
            .Where(p => p.CompanyId == companyId && p.Id != req.ProductId)
            .Select(p => new { p.Code, p.Name, p.CategoryId, p.CreatedAt })
            .ToListAsync(ct);
        var sameCategory = products.Where(p => p.CategoryId == category.Id)
            .OrderByDescending(p => p.CreatedAt).Take(30)
            .Select(p => (p.Code, p.Name)).ToList();

        var a = await AskAsync<ProductAnswer>(CodePrompts.Product(name, category, scheme, terms, sameCategory), CodePrompts.ProductSchema(), ct);
        return CodeSuggestions.Product(category.CodePrefix, scheme.Segments, ToParts(a.Parts), terms,
            products.ToDictionary(p => p.Code, p => p.Name), Optional(a.Reason, 300));
    }

    // ------------------------------------------------------------------

    private async Task<T> AskAsync<T>(string user, JsonObject schema, CancellationToken ct)
    {
        string json;
        try
        {
            json = await llm.CompleteJsonAsync(CodePrompts.System, user, schema, ct);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            throw new UnavailableException($"เรียก AI ไม่สำเร็จ — ตรวจว่า Ollama เปิดอยู่ แล้วลองอีกครั้ง ({e.Message})");
        }
        try
        {
            return JsonSerializer.Deserialize<T>(json, Json) ?? throw new JsonException("empty");
        }
        catch (JsonException)
        {
            throw new UnavailableException("AI ตอบกลับมาในรูปแบบที่อ่านไม่ได้ ลองกดอีกครั้ง");
        }
    }

    private async Task<ProductCodeScheme> RequireSchemeAsync(Guid companyId, CancellationToken ct) =>
        await db.ProductCodeSchemes.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == companyId, ct)
        ?? throw new BadRequestException(NotSetUp);

    private async Task RequireCompanyAsync(Guid companyId, CancellationToken ct)
    {
        if (!await db.Companies.AnyAsync(c => c.Id == companyId, ct)) throw new NotFoundException($"ไม่พบบริษัท {companyId}");
    }

    private async Task<List<ProductCategory>> CategoriesAsync(Guid companyId, Guid? except, CancellationToken ct) =>
        await db.ProductCategories.AsNoTracking()
            .Where(c => c.CompanyId == companyId && c.Id != except)
            .OrderBy(c => c.SortOrder)
            .ToListAsync(ct);

    private async Task<CodeSchemeDto> ToDtoAsync(ProductCodeScheme s, CancellationToken ct)
    {
        var terms = await db.ProductCodeTerms.AsNoTracking()
            .Where(t => t.CompanyId == s.CompanyId)
            .OrderBy(t => t.Segment).ThenBy(t => t.Code)
            .Select(t => new CodeTermDto(t.Id, t.Segment, t.Term, t.Code))
            .ToListAsync(ct);
        return new CodeSchemeDto(s.BusinessType, s.ProductTypes, s.Notes, s.CategoryNameHint, s.ProductNameHint, s.CodeHint,
            s.Segments.Select(ToDto).ToList(), terms, s.UpdatedAt);
    }

    private static List<CodePart> ToParts(List<PartAnswer>? parts) =>
        (parts ?? []).Select(p => new CodePart(p.Segment ?? "", p.Term, p.Code ?? "")).ToList();

    private static CodeSegmentDto ToDto(CodeSegment s) => new(s.Key, s.Label, s.Length, s.Digits);

    /// <summary>Trimmed, blanks and repeats dropped, at most 50 of up to 100 characters.</summary>
    private static List<string> CleanList(IEnumerable<string>? items) =>
        (items ?? [])
            .Select(i => ProductNames.Clean(i))
            .Where(i => i.Length > 0)
            .Select(i => i.Length > 100 ? i[..100] : i)
            .DistinctBy(i => i.ToLowerInvariant())
            .Take(50)
            .ToList();

    private static string Required(string? value, int max, string missing) =>
        Optional(value, max) ?? throw new BadRequestException(missing);

    private static string? Optional(string? value, int max)
    {
        var v = value?.Trim();
        return string.IsNullOrEmpty(v) ? null : v.Length > max ? v[..max] : v;
    }

    // What the AI answers (snake_case JSON).
    private sealed record SchemeAnswer(
        string? CategoryNameHint, string? ProductNameHint, string? CodeHint,
        List<SegmentAnswer>? Segments, List<CategoryAnswer>? Categories, List<ExampleAnswer>? Examples);

    private sealed record SegmentAnswer(string? Key, string? Label, int Length, bool Digits);

    private sealed record CategoryAnswer(string? Name, string? CodePrefix, string? Reason);

    private sealed record ExampleAnswer(string? Name, string? CategoryPrefix, List<PartAnswer>? Parts);

    private sealed record ProductAnswer(List<PartAnswer>? Parts, string? Reason);

    private sealed record PartAnswer(string? Segment, string? Term, string? Code);

    private sealed record AttributesAnswer(List<TypeAttributes>? Types);

    private sealed record TypeAttributes(string? Type, List<string>? Attributes);
}
