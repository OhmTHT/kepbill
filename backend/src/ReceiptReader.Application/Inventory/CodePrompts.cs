using System.Text;
using System.Text.Json.Nodes;
using ReceiptReader.Domain.Entities;

namespace ReceiptReader.Application.Inventory;

/// <summary>Prompts and JSON schemas of the product code suggestions.</summary>
public static class CodePrompts
{
    public const string System =
        """
        You help a Thai shop name its products and design product codes. The codes are printed as Code 128 barcodes
        and typed by USB scanners, so a code uses ONLY upper-case A–Z and 0–9: no "-", no spaces, no Thai.
        Codes are built from parts joined directly: category prefix + one part per segment (BED6 + 123 + 111 → BED6123111).
        Write every text the user reads (hints, labels, reasons, names) in Thai; keep brand and model names as written.
        Follow the patterns the shop already uses before inventing new ones. Reply ONLY with JSON matching the schema.
        """;

    // ---------------- the questionnaire ----------------

    public static string Scheme(string businessType, IReadOnlyList<string> productTypes, string? notes,
        ProductCodeScheme? current, IReadOnlyList<ProductCategory> categories, IReadOnlyList<(string Code, string Name, string Category)> products)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Design the naming and coding pattern for this shop.");
        sb.AppendLine().Append("Business type: ").AppendLine(businessType);
        sb.AppendLine("The shop sells ONLY these product types (never add others):").Append(CodeSuggestions.Lines(productTypes));
        if (!string.IsNullOrWhiteSpace(notes)) sb.Append("How the owner tells products apart: ").AppendLine(notes);
        if (current is { Segments.Count: > 0 })
            sb.AppendLine("Current code segments (keep these keys for segments you keep):")
                .Append(CodeSuggestions.Lines(current.Segments.Select(Describe)));
        sb.AppendLine("Existing categories (name → prefix; keep these prefixes, follow their style):")
            .Append(CodeSuggestions.Lines(categories.Select(c => $"{c.Name} → {c.CodePrefix}")));
        sb.AppendLine("Existing products (code → name, category):")
            .Append(CodeSuggestions.Lines(products.Select(p => $"{p.Code} → {p.Name} ({p.Category})")));
        sb.AppendLine(
            """

            Return:
            - category_name_hint: how to write category names, as a Thai pattern with an example,
              e.g. "ประเภทสินค้า + ขนาด เช่น เตียงนอน 6 ฟุต".
            - product_name_hint: how to write product names, e.g. "แบรนด์ รุ่น สี เช่น Lotus Burgi ขาว".
            - segments: 1–4 parts of the product code after the category prefix, in order, chosen from what really
              tells this business's products apart (brand, model, colour, pattern, material, size …).
              key = short English id (brand, model, color, size …); label = Thai name; length = 1–4 characters;
              digits = true for a numeric part (most parts: Lotus → 123, ขาว → 111), false for letters.
              Size that already defines the category (เตียงนอน 6 ฟุต) belongs in the category prefix, not a segment.
            - code_hint: the code structure in one Thai sentence with a full example code.
            - categories: suggested categories for the listed product types only, each name starting with its
              type as written (1–3 per type, e.g. per size when size matters), each with code_prefix = 2–6 characters: a short English abbreviation of the type plus the
              size number when the name has one (เตียงนอน 6 ฟุต → BED6, เตียงนอน 5 ฟุต → BED5).
              Include existing categories unchanged.
            - examples: 3 example products: name following product_name_hint, category_prefix = the prefix of
              one of the categories, and parts = one per segment {segment, term, code}, each code exactly the
              segment's length and kind.
            """);
        return sb.ToString();
    }

    public static JsonObject SchemeSchema() => Object(new()
    {
        ["category_name_hint"] = String(),
        ["product_name_hint"] = String(),
        ["code_hint"] = String(),
        ["segments"] = Array(Object(new()
        {
            ["key"] = String(),
            ["label"] = String(),
            ["length"] = new JsonObject { ["type"] = "integer" },
            ["digits"] = new JsonObject { ["type"] = "boolean" },
        })),
        ["categories"] = Array(Object(new() { ["name"] = String(), ["code_prefix"] = String() })),
        ["examples"] = Array(Object(new() { ["name"] = String(), ["category_prefix"] = String(), ["parts"] = Parts() })),
    });

    // ---------------- what tells products apart ----------------

    public static string Attributes(string businessType, IReadOnlyList<string> productTypes, string? notes)
    {
        var sb = new StringBuilder();
        sb.Append("Business type: ").AppendLine(businessType);
        sb.AppendLine("The shop sells ONLY these product types (never add others):").Append(CodeSuggestions.Lines(productTypes));
        if (!string.IsNullOrWhiteSpace(notes)) sb.AppendLine("What the owner wrote so far (follow it, e.g. a type said to have no colour gets none):").AppendLine(notes);
        sb.AppendLine(
            """

            For each listed product type, in the same order, list the attributes that tell one product of that type
            from another in this shop, most important first: 2–4 short Thai words such as แบรนด์, รุ่น, ขนาด, สี,
            ลาย, วัสดุ, จำนวนที่นั่ง. Only attributes that really vary for that type (a bed is told apart by size,
            a sofa by number of seats). type = the product type exactly as listed.
            """);
        return sb.ToString();
    }

    public static JsonObject AttributesSchema() => Object(new()
    {
        ["types"] = Array(Object(new() { ["type"] = String(), ["attributes"] = Array(String()) })),
    });

    // ---------------- category ----------------

    public static string Category(string name, ProductCodeScheme scheme, IReadOnlyList<ProductCategory> others)
    {
        var sb = new StringBuilder();
        sb.Append("Business type: ").AppendLine(scheme.BusinessType);
        if (scheme.CategoryNameHint is { } hint) sb.Append("Category names follow: ").AppendLine(hint);
        if (scheme.CodeHint is { } codeHint) sb.Append("Product codes: ").AppendLine(codeHint);
        sb.AppendLine("Existing categories (name → prefix):")
            .Append(CodeSuggestions.Lines(others.Select(c => $"{c.Name} → {c.CodePrefix}")));
        sb.AppendLine().Append("New category: ").AppendLine(name);
        sb.AppendLine(
            """

            Suggest its code_prefix: 2–6 characters A–Z/0–9, different from every existing prefix. Use a short English
            abbreviation of the product type, plus the size or variant number when the name has one. A category of
            the same type as an existing one must share its letters (existing ผ้าปู 5 ฟุต → SHT5, so ผ้าปู 6 ฟุต → SHT6).
            reason: one short Thai sentence on how the prefix was built.
            """);
        return sb.ToString();
    }

    public static JsonObject CategorySchema() => Object(new() { ["code_prefix"] = String(), ["reason"] = String() });

    // ---------------- product ----------------

    public static string Product(string name, ProductCategory category, ProductCodeScheme scheme,
        IReadOnlyList<ProductCodeTerm> terms, IReadOnlyList<(string Code, string Name)> sameCategory)
    {
        var sb = new StringBuilder();
        sb.Append("Business type: ").AppendLine(scheme.BusinessType);
        if (scheme.ProductNameHint is { } hint) sb.Append("Product names follow: ").AppendLine(hint);
        if (scheme.CodeHint is { } codeHint) sb.Append("Product codes: ").AppendLine(codeHint);
        sb.Append("Category: ").Append(category.Name).Append(" (prefix ").Append(category.CodePrefix).AppendLine(")");
        sb.AppendLine("Segments after the prefix, in order:").Append(CodeSuggestions.Lines(scheme.Segments.Select(Describe)));
        foreach (var s in scheme.Segments)
            sb.Append("Codes already used for ").Append(s.Key).AppendLine(" (term = code; reuse them exactly):")
                .Append(CodeSuggestions.Lines(terms.Where(t => t.Segment == s.Key).Take(150).Select(t => $"{t.Term} = {t.Code}")));
        sb.AppendLine("Products already in this category (code → name):")
            .Append(CodeSuggestions.Lines(sameCategory.Select(p => $"{p.Code} → {p.Name}")));
        sb.AppendLine().Append("New product: ").AppendLine(name);
        sb.AppendLine(
            """

            Return one part per segment, in order:
            - segment: the segment key.
            - term: the word(s) of the product name that give this segment's value, as written in the name
              ("Lotus", "Burgi", "ขาว"). null when the name has no value for it, or when the category already says it.
            - code: when the term is in the "already used" list (same meaning, any spelling or case), that exact code.
              Otherwise a new code of exactly the segment's length that no other term of the segment uses: for a
              number in the name (6 ฟุต) the number itself, padded with leading zeros; for other numeric terms the
              next free number after the highest one used; for letter segments an abbreviation of the term.
              "" when term is null.
            reason: one short Thai sentence on how the code was built.
            """);
        return sb.ToString();
    }

    public static JsonObject ProductSchema() => Object(new() { ["parts"] = Parts(), ["reason"] = String() });

    private static JsonObject Parts() => Array(Object(new()
    {
        ["segment"] = String(),
        ["term"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
        ["code"] = String(),
    }));

    private static string Describe(CodeSegment s) =>
        $"{s.Key} = {s.Label}, {s.Length} {(s.Digits ? "digits" : "letters/digits")}";

    private static JsonObject String() => new() { ["type"] = "string" };

    private static JsonObject Array(JsonObject items) => new() { ["type"] = "array", ["items"] = items };

    private static JsonObject Object(Dictionary<string, JsonNode?> properties)
    {
        var props = new JsonObject();
        foreach (var (k, v) in properties) props[k] = v;
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = props,
            ["required"] = new JsonArray(properties.Keys.Select(k => (JsonNode)k).ToArray()),
        };
    }
}
