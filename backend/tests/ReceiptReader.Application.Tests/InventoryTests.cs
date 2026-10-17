using ReceiptReader.Application.Abstractions;
using ReceiptReader.Application.Inventory;
using ReceiptReader.Domain.Entities;
using ReceiptReader.Domain.Enums;

namespace ReceiptReader.Application.Tests;

public class ProductCodeTests
{
    [Theory]
    [InlineData(" bed ", "BED")]
    [InlineData("mat2", "MAT2")]
    public void Prefix_is_trimmed_and_upper_cased(string input, string expected) =>
        Assert.Equal(expected, ProductCodes.NormalizePrefix(input));

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    [InlineData("BED-")] // the dash is added between prefix and number, not typed
    [InlineData("BEDROOM")]
    [InlineData("เตียง")]
    public void Prefix_is_required_letters_and_digits_up_to_six(string? input) =>
        Assert.Throws<BadRequestException>(() => ProductCodes.NormalizePrefix(input));

    [Theory]
    [InlineData(" bed6123111 ", "BED6123111")]
    [InlineData("BED-001", "BED-001")] // codes from before stay valid
    public void Code_is_trimmed_and_upper_cased(string input, string expected) =>
        Assert.Equal(expected, ProductCodes.NormalizeCode(input));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("BED 6")]
    [InlineData("เตียง6")]
    public void Code_is_required_and_scannable(string? input) =>
        Assert.Throws<BadRequestException>(() => ProductCodes.NormalizeCode(input));

    [Theory]
    [InlineData("BED-001", true)]
    [InlineData("-BED", false)]
    [InlineData("BED 01", false)]
    [InlineData("เตียง01", false)]
    public void Codes_a_scanner_can_type(string code, bool valid) => Assert.Equal(valid, ProductCodes.IsValid(code));
}

public class ProductNameTests
{
    [Theory]
    [InlineData("  เตียง   Lotus	6 ฟุต ", "เตียง Lotus 6 ฟุต")]
    [InlineData("lotus", "lotus")]
    public void Names_are_trimmed_and_spaces_collapsed(string input, string expected) =>
        Assert.Equal(expected, ProductNames.Clean(input));

    [Fact]
    public void Names_compare_ignoring_case_and_spacing() =>
        Assert.Equal(ProductNames.Key("เตียง Lotus 6 ฟุต"), ProductNames.Key("  เตียง  lotus 6 ฟุต"));

    [Fact]
    public void Different_names_stay_different() =>
        Assert.NotEqual(ProductNames.Key("เตียง Lotus 6 ฟุต"), ProductNames.Key("เตียง Lotus 5 ฟุต"));
}

public class ScanTests
{
    [Fact]
    public void Plain_scan_is_one_candidate() =>
        Assert.Equal(["BED-0001"], ProductCodes.ScanCandidates(" BED-0001\r\n"));

    [Fact]
    public void Lower_case_typing_is_upper_cased() =>
        Assert.Equal(["BED-0001"], ProductCodes.ScanCandidates("bed-0001"));

    [Theory]
    // The scanner pressed Shift+B Shift+E Shift+D - 0 0 0 1 while Windows was switched to the Thai keyboard.
    [InlineData("ฺฎฏขจจจๅ", "BED-0001")]
    [InlineData("ฉ/-ภ", "C234")]
    [InlineData("ถุึคต", "56789")]
    [InlineData("ฤฆฏโฌ็๋ษศ", "ASDFGHJKL")]
    [InlineData("๐\"ฎฑธํ๊ณฯญ", "QWERTYUIOP")]
    [InlineData("()ฉฮฺ์?", "ZXCVBNM")]
    [InlineData("-/", "32")] // only keys that type ASCII in Thai: not a valid code, so read as English keys
    public void Scan_typed_into_the_Thai_layout_is_read_as_English_keys(string scanned, string code)
    {
        var candidates = ProductCodes.ScanCandidates(scanned);
        Assert.Equal(code, candidates[^1]);
    }

    [Fact]
    public void Blank_scan_has_no_candidates() => Assert.Empty(ProductCodes.ScanCandidates("  "));
}

public class StockLedgerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private static int _seq;

    private static StockLine L(int day, StockMovementType type, decimal qty, decimal? cost = null) =>
        new(new DateOnly(2026, 10, day), T0.AddSeconds(++_seq), type, qty, cost);

    [Fact]
    public void Moving_average_reaverages_on_each_purchase()
    {
        // The worked example: 10 at 5,000, sell 4, buy 4 at 5,500 → average 5,200; sell 3 → 7 left worth 36,400.
        var p = StockLedger.Run([
            L(1, StockMovementType.Purchase, 10, 5000),
            L(5, StockMovementType.Sale, -4),
            L(10, StockMovementType.Purchase, 4, 5500),
            L(15, StockMovementType.Sale, -3),
        ]);
        Assert.Equal(7, p.Quantity);
        Assert.Equal(36400, p.Value);
        Assert.Equal(5200, p.AverageCost);
    }

    [Fact]
    public void Opening_stock_counts_like_a_purchase()
    {
        var p = StockLedger.Run([L(1, StockMovementType.Opening, 3, 4000), L(2, StockMovementType.Purchase, 1, 4400)]);
        Assert.Equal(4, p.Quantity);
        Assert.Equal(4100, p.AverageCost);
    }

    [Fact]
    public void Movements_run_in_date_order_whatever_order_they_were_entered()
    {
        var p = StockLedger.Run([
            L(15, StockMovementType.Sale, -3),
            L(10, StockMovementType.Purchase, 4, 5500),
            L(5, StockMovementType.Sale, -4),
            L(1, StockMovementType.Purchase, 10, 5000),
        ]);
        Assert.Equal(36400, p.Value);
    }

    [Fact]
    public void Transfers_leave_the_product_total_alone()
    {
        var p = StockLedger.Run([
            L(1, StockMovementType.Opening, 5, 1000),
            L(2, StockMovementType.TransferOut, -2),
            L(2, StockMovementType.TransferIn, 2),
        ]);
        Assert.Equal(5, p.Quantity);
        Assert.Equal(5000, p.Value);
    }

    [Fact]
    public void Selling_everything_leaves_no_value()
    {
        var p = StockLedger.Run([L(1, StockMovementType.Purchase, 3, 333.33m), L(2, StockMovementType.Sale, -3)]);
        Assert.Equal(StockPosition.Empty, p);
    }

    [Fact]
    public void Stock_arriving_after_a_shortfall_sets_the_average_by_itself()
    {
        // Sold 2 before the purchase was recorded: the books go to −2, then 5 arrive at 100.
        var p = StockLedger.Run([
            L(1, StockMovementType.Purchase, 1, 80),
            L(2, StockMovementType.Sale, -3),
            L(3, StockMovementType.Purchase, 5, 100),
        ]);
        Assert.Equal(3, p.Quantity);
        Assert.Equal(300, p.Value);
    }

    [Fact]
    public void No_movements_is_empty() => Assert.Equal(StockPosition.Empty, StockLedger.Run([]));
}

public class CodeSuggestionTests
{
    private static readonly CodeSegment Brand = new() { Key = "brand", Label = "แบรนด์", Length = 3, Digits = true };
    private static readonly CodeSegment Color = new() { Key = "color", Label = "สี", Length = 3, Digits = true };
    private static readonly CodeSegment Model = new() { Key = "model", Label = "รุ่น", Length = 2, Digits = false };
    private static readonly Dictionary<string, string> NoCodes = [];

    private static ProductCodeTerm T(string segment, string term, string code) =>
        new() { Segment = segment, Term = term, NormalizedTerm = ProductNames.Key(term), Code = code };

    [Fact]
    public void Code_is_the_prefix_and_the_parts_in_scheme_order()
    {
        var s = CodeSuggestions.Product("BED6", [Brand, Color],
            [new CodePart("color", "ขาว", "111"), new CodePart("brand", "Lotus", "123")], [], NoCodes, "ok");
        Assert.Equal("BED6123111", s.Code);
        Assert.Equal(["brand", "color"], s.Parts.Select(p => p.Segment));
        Assert.Null(s.Warning);
    }

    [Fact]
    public void Known_terms_keep_their_code_whatever_the_ai_said()
    {
        var s = CodeSuggestions.Product("BED6", [Brand, Color],
            [new CodePart("brand", "LOTUS", "999"), new CodePart("color", "ดำ", "222")],
            [T("brand", "Lotus", "123"), T("color", "ขาว", "111")], NoCodes, null);
        Assert.Equal("BED6123222", s.Code);
        Assert.True(s.Parts[0].Known);
        Assert.False(s.Parts[1].Known);
    }

    [Fact]
    public void A_new_term_never_takes_a_code_another_term_has()
    {
        var s = CodeSuggestions.Product("BED", [Brand],
            [new CodePart("brand", "Slumberland", "123")], [T("brand", "Lotus", "123"), T("brand", "Dunlopillo", "124")], NoCodes, null);
        Assert.Equal("BED125", s.Code);
    }

    [Fact]
    public void Digit_parts_are_padded_to_the_segment_length()
    {
        var size = new CodeSegment { Key = "size", Label = "ขนาด", Length = 2, Digits = true };
        var s = CodeSuggestions.Product("SHT", [size], [new CodePart("size", "6 ฟุต", "6")], [], NoCodes, null);
        Assert.Equal("SHT06", s.Code);
    }

    [Fact]
    public void Letter_parts_are_cut_to_the_segment_length() =>
        Assert.Equal("BEDBU", CodeSuggestions.Product("BED", [Model], [new CodePart("model", "Burgi", "burgi")], [], NoCodes, null).Code);

    [Fact]
    public void A_segment_missing_from_the_name_keeps_its_place_with_zeros()
    {
        var s = CodeSuggestions.Product("BED6", [Brand, Color], [new CodePart("brand", "Lotus", "123")], [], NoCodes, null);
        Assert.Equal("BED6123000", s.Code);
        Assert.Null(s.Parts[1].Term);
    }

    [Fact]
    public void A_code_another_product_has_gets_a_tail_and_a_warning()
    {
        var s = CodeSuggestions.Product("BED6", [Brand], [new CodePart("brand", "Lotus", "123")], [T("brand", "Lotus", "123")],
            new Dictionary<string, string> { ["BED6123"] = "BED6123 เตียง Lotus" }, null);
        Assert.Equal("BED61232", s.Code);
        Assert.Contains("BED6123", s.Warning);
    }

    [Theory]
    [InlineData("bed-6", "BED6")]
    [InlineData("", "LOTUS6")] // nothing usable from the AI: the Latin letters of the name
    public void Category_prefix_is_cleaned(string proposed, string expected) =>
        Assert.Equal(expected, CodeSuggestions.Prefix(proposed, "เตียง Lotus 6 ฟุต", []));

    [Fact]
    public void Category_prefix_is_made_unique() =>
        Assert.Equal("BED62", CodeSuggestions.Prefix("BED6", "เตียงนอน 6 ฟุต", ["BED6"]));

    [Fact]
    public void Saving_learns_only_new_terms_that_are_in_the_code()
    {
        var learned = CodeSuggestions.NewTerms("BED6123222",
            [
                new CodePart("brand", "Lotus", "123"), // known already
                new CodePart("color", "ดำ", "222"),
                new CodePart("model", "Burgi", "BU"), // not in the code: edited by hand
                new CodePart("size", null, "00"),
            ],
            [T("brand", "Lotus", "123")]);
        Assert.Equal([new NewTerm("color", "ดำ", "ดำ", "222")], learned);
    }

    [Fact]
    public void Saving_never_recodes_a_term_or_reuses_a_code()
    {
        var learned = CodeSuggestions.NewTerms("BED6555111",
            [new CodePart("brand", "lotus", "555"), new CodePart("color", "ดำ", "111")],
            [T("brand", "Lotus", "123"), T("color", "ขาว", "111")]);
        Assert.Empty(learned);
    }

    [Fact]
    public void Attribute_notes_follow_the_owners_types_and_order()
    {
        var notes = CodeSuggestions.AttributeNotes(["เตียงนอน", "โซฟา", "เก้าอี้"],
        [
            ("โซฟา", ["แบรนด์", "จำนวนที่นั่ง", "สี", "สี"]),
            ("ตู้เสื้อผ้า", ["แบรนด์"]), // not sold here
            ("เตียงนอน ", ["แบรนด์", "รุ่น", "ขนาด"]),
            ("เก้าอี้", []), // nothing usable: left out
        ]);
        Assert.Equal("- เตียงนอน\nแบรนด์ รุ่น ขนาด\n- โซฟา\nแบรนด์ จำนวนที่นั่ง สี", notes);
    }

    [Fact]
    public void Attribute_notes_match_a_type_written_slightly_differently() =>
        Assert.Equal("- เตียงนอน\nแบรนด์ ขนาด", CodeSuggestions.AttributeNotes(["เตียงนอน"], [("เตียงนอน 6 ฟุต", ["แบรนด์", "ขนาด"])]));

    [Fact]
    public void Segments_need_labels_and_get_unique_keys()
    {
        var s = CodeSuggestions.CleanSegments([
            new CodeSegment { Key = "Brand!", Label = " แบรนด์ ", Length = 3, Digits = true },
            new CodeSegment { Key = "brand", Label = "สี", Length = 3, Digits = true },
        ]);
        Assert.Equal(["brand", "part2"], s.Select(x => x.Key));
        Assert.Equal("แบรนด์", s[0].Label);
        Assert.Throws<BadRequestException>(() => CodeSuggestions.CleanSegments([]));
        Assert.Throws<BadRequestException>(() => CodeSuggestions.CleanSegments([new CodeSegment { Label = "สี", Length = 9 }]));
    }
}
