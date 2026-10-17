using ReceiptReader.Application.Extraction;
using ReceiptReader.Application.Validation;
using ReceiptReader.Domain.Entities;
using ReceiptReader.Domain.Enums;

namespace ReceiptReader.Application.Tests;

public class ThaiTaxIdTests
{
    [Theory]
    [InlineData("0105536092641", true)]
    [InlineData("0-1055-36092-64-1", true)]
    [InlineData("0105536092642", false)]
    [InlineData("010553609264", false)]
    [InlineData(null, false)]
    public void Checksum(string? id, bool expected) => Assert.Equal(expected, ThaiTaxId.IsValid(id));

    [Theory]
    [InlineData("0 10 7 567 00041 4", "0107567000414")]
    [InlineData("0100568000322 สาขา 00000", "0100568000322")] // branch printed after the ID
    [InlineData("ไม่มี", null)]
    public void Normalize(string input, string? expected) => Assert.Equal(expected, ThaiTaxId.Normalize(input));

    [Theory]
    [InlineData("สำนักงานใหญ่", "00000")]
    [InlineData("Head Office", "00000")]
    [InlineData("สาขาที่ 12", "00012")]
    [InlineData("00003", "00003")]
    public void Branch(string input, string expected) => Assert.Equal(expected, ThaiBranch.Normalize(input));
}

public class ThaiDateNormalizerTests
{
    [Theory]
    [InlineData(null, "13/10/2569", "2026-10-13")]   // พ.ศ. 4 digits
    [InlineData(null, "13/10/69", "2026-10-13")]     // พ.ศ. 2 digits
    [InlineData(null, "13/10/26", "2026-10-13")]     // ค.ศ. 2 digits
    [InlineData(null, "13-10-2026", "2026-10-13")]
    [InlineData(null, "13 ต.ค. 2569", "2026-10-13")]
    [InlineData(null, "วันที่ 13 ตุลาคม พ.ศ. 2569", "2026-10-13")]
    [InlineData(null, "๑๓/๑๐/๒๕๖๙", "2026-10-13")]    // Thai digits
    [InlineData("2569-10-13", null, "2026-10-13")]   // model left BE in ISO
    [InlineData("1969-10-13", "13/10/69", "2026-10-13")] // raw beats a wrong ISO guess
    [InlineData("2026-10-13", "garbage", "2026-10-13")]
    public void Normalizes(string? iso, string? raw, string expected) =>
        Assert.Equal(DateOnly.Parse(expected), ThaiDateNormalizer.Normalize(iso, raw));

    [Fact]
    public void InvalidDateIsNull() => Assert.Null(ThaiDateNormalizer.Normalize("2026-02-30", "31/02/2569"));
}

public class ReceiptValidatorTests
{
    private static readonly DateOnly Today = new(2026, 10, 13);

    private static Receipt ValidTaxInvoice() => new()
    {
        DocumentType = DocumentType.AbbreviatedTaxInvoice,
        DocumentNumber = "INV-001",
        IssueDate = new DateOnly(2026, 10, 1),
        SellerName = "ร้านทดสอบ",
        SellerTaxId = "0105536092641",
        Subtotal = 100m,
        VatAmount = 7m,
        Total = 107m,
        LineItems =
        [
            new ReceiptLineItem { LineNo = 1, Description = "A", Quantity = 2, UnitPrice = 25m, Amount = 50m },
            new ReceiptLineItem { LineNo = 2, Description = "B", Quantity = 1, UnitPrice = 50m, Amount = 50m },
        ],
    };

    [Fact]
    public void CleanReceiptHasNoIssues() =>
        Assert.Empty(ReceiptValidator.Validate(ValidTaxInvoice(), Today));

    [Fact]
    public void VatInclusiveReceiptPasses()
    {
        var r = ValidTaxInvoice();
        r.Subtotal = 107m; r.VatIncluded = true; r.VatAmount = 7m; r.Total = 107m;
        r.LineItems[0].Amount = 57m; r.LineItems[0].UnitPrice = 28.5m;
        Assert.Empty(ReceiptValidator.Validate(r, Today));
    }

    [Fact]
    public void DetectsTotalMismatch()
    {
        var r = ValidTaxInvoice();
        r.Total = 170m; // misread digit
        Assert.Contains(ReceiptValidator.Validate(r, Today), i => i.Code == "TOTAL_MISMATCH" && i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public void DetectsBadTaxId()
    {
        var r = ValidTaxInvoice();
        r.SellerTaxId = "0105536092642";
        Assert.Contains(ReceiptValidator.Validate(r, Today), i => i.Code == "SELLER_TAXID_INVALID");
    }

    [Fact]
    public void DetectsFutureDate()
    {
        var r = ValidTaxInvoice();
        r.IssueDate = new DateOnly(2569, 10, 1); // BE year not converted
        Assert.Contains(ReceiptValidator.Validate(r, Today), i => i.Code == "DATE_FUTURE");
    }

    [Fact]
    public void WarnsOnWrongVatRate()
    {
        var r = ValidTaxInvoice();
        r.VatAmount = 10m; r.Total = 110m;
        Assert.Contains(ReceiptValidator.Validate(r, Today), i => i.Code == "VAT_RATE");
    }

    [Fact]
    public void WarnsOnLineCalc()
    {
        var r = ValidTaxInvoice();
        r.LineItems[0].Quantity = 3;
        Assert.Contains(ReceiptValidator.Validate(r, Today), i => i.Code == "LINE_CALC");
    }

    [Fact]
    public void WarnsWhenCounterpartyNameMissing()
    {
        var r = ValidTaxInvoice();
        r.SellerName = null;
        Assert.Contains(ReceiptValidator.Validate(r, Today), i => i.Code == "COUNTERPARTY_MISSING" && i.Field == "sellerName");
    }
}

public class ExtractedReceiptTests
{
    [Fact]
    public void ParsesFencedJsonAndMaps()
    {
        const string json = """
            ```json
            {"document_type":"full_tax_invoice","document_number":"IV6910-0001",
             "issue_date_raw":"13/10/69","issue_date":"1969-10-13",
             "seller_name":" บริษัท ทดสอบ จำกัด ","seller_tax_id":"0-1055-36092-64-1","seller_branch":"สำนักงานใหญ่",
             "items":[{"description":"ค่าบริการ","quantity":1,"unit_price":1000,"amount":1000}],
             "subtotal":1000,"discount":-0,"vat_amount":70,"vat_included":false,"total":1070,"currency":"บาท"}
            ```
            """;
        var x = ExtractedReceipt.Parse(json);
        var r = new Receipt();
        ReceiptMapper.Apply(x, r);

        Assert.Equal(DocumentType.FullTaxInvoice, r.DocumentType);
        Assert.Equal(new DateOnly(2026, 10, 13), r.IssueDate);
        Assert.Equal("บริษัท ทดสอบ จำกัด", r.SellerName);
        Assert.Equal("0105536092641", r.SellerTaxId);
        Assert.Equal("00000", r.SellerBranch);
        Assert.Equal("THB", r.Currency);
        Assert.Single(r.LineItems);
        Assert.Equal(1070m, r.Total);
    }

    [Fact]
    public void RoundTripsForVendorExamples()
    {
        var r = new Receipt { SellerName = "X", Total = 10m, DocumentType = DocumentType.Receipt };
        var json = ReceiptMapper.ToExtracted(r).ToJson();
        Assert.Contains("\"seller_name\":\"X\"", json);
        Assert.Equal("receipt", ExtractedReceipt.Parse(json).DocumentType);
    }
}
