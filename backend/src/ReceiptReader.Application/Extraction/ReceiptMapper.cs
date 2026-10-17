using ReceiptReader.Application.Validation;
using ReceiptReader.Domain.Entities;
using ReceiptReader.Domain.Enums;

namespace ReceiptReader.Application.Extraction;

/// <summary>Converts between the model's JSON contract and the domain entity, normalizing as it goes.</summary>
public static class ReceiptMapper
{
    public static void Apply(ExtractedReceipt x, Receipt r)
    {
        r.DocumentType = ParseDocumentType(x.DocumentType);
        r.DocumentNumber = Clean(x.DocumentNumber, 100);
        r.IssueDate = ThaiDateNormalizer.Normalize(x.IssueDate, x.IssueDateRaw);
        r.DueDate = ThaiDateNormalizer.Normalize(x.DueDate, null);
        r.ReferenceDocumentNumber = Clean(x.ReferenceDocumentNumber, 100);

        r.SellerName = Clean(x.SellerName, 300);
        r.SellerTaxId = ThaiTaxId.Normalize(x.SellerTaxId);
        r.SellerBranch = ThaiBranch.Normalize(x.SellerBranch);
        r.SellerAddress = Clean(x.SellerAddress);

        r.BuyerName = Clean(x.BuyerName, 300);
        r.BuyerTaxId = ThaiTaxId.Normalize(x.BuyerTaxId);
        r.BuyerBranch = ThaiBranch.Normalize(x.BuyerBranch);
        r.BuyerAddress = Clean(x.BuyerAddress);

        r.Subtotal = Money(x.Subtotal);
        r.Discount = Money(x.Discount is null ? null : Math.Abs(x.Discount.Value)); // "-50.00" → 50
        r.VatAmount = Money(x.VatAmount);
        r.Total = Money(x.Total);
        r.VatIncluded = x.VatIncluded ?? false;
        r.Currency = string.IsNullOrWhiteSpace(x.Currency) ? "THB" : x.Currency.Trim().ToUpperInvariant() switch
        {
            "฿" or "บาท" or "BAHT" => "THB",
            var c => c.Length > 3 ? "THB" : c,
        };
        r.PaymentMethod = Clean(x.PaymentMethod, 100);

        r.LineItems.Clear();
        var lineNo = 1;
        foreach (var item in x.Items.Where(i => !string.IsNullOrWhiteSpace(i.Description) || i.Amount is not null))
        {
            r.LineItems.Add(new ReceiptLineItem
            {
                ReceiptId = r.Id,
                LineNo = lineNo++,
                Description = Clean(item.Description, 500) ?? "",
                Quantity = item.Quantity,
                UnitPrice = Money(item.UnitPrice),
                Discount = Money(item.Discount is null or 0 ? null : Math.Abs(item.Discount.Value)),
                Amount = Money(item.Amount),
            });
        }
    }

    /// <summary>Back to the JSON contract — used to store approved receipts as few-shot examples.</summary>
    public static ExtractedReceipt ToExtracted(Receipt r) => new()
    {
        DocumentType = DocumentTypeToJson(r.DocumentType),
        DocumentNumber = r.DocumentNumber,
        IssueDate = r.IssueDate?.ToString("yyyy-MM-dd"),
        DueDate = r.DueDate?.ToString("yyyy-MM-dd"),
        ReferenceDocumentNumber = r.ReferenceDocumentNumber,
        SellerName = r.SellerName,
        SellerTaxId = r.SellerTaxId,
        SellerBranch = r.SellerBranch,
        SellerAddress = r.SellerAddress,
        BuyerName = r.BuyerName,
        BuyerTaxId = r.BuyerTaxId,
        BuyerBranch = r.BuyerBranch,
        BuyerAddress = r.BuyerAddress,
        Items = r.LineItems.OrderBy(l => l.LineNo).Select(l => new ExtractedLineItem
        {
            Description = l.Description, Quantity = l.Quantity, UnitPrice = l.UnitPrice, Discount = l.Discount,
            Amount = l.Amount,
        }).ToList(),
        Subtotal = r.Subtotal,
        Discount = r.Discount,
        VatAmount = r.VatAmount,
        VatIncluded = r.VatIncluded,
        Total = r.Total,
        Currency = r.Currency,
        PaymentMethod = r.PaymentMethod,
    };

    public static DocumentType ParseDocumentType(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "receipt" => DocumentType.Receipt,
        "abbreviated_tax_invoice" => DocumentType.AbbreviatedTaxInvoice,
        "full_tax_invoice" => DocumentType.FullTaxInvoice,
        "receipt_tax_invoice" => DocumentType.ReceiptTaxInvoice,
        "invoice" => DocumentType.Invoice,
        "credit_note" => DocumentType.CreditNote,
        "debit_note" => DocumentType.DebitNote,
        _ => DocumentType.Unknown,
    };

    public static string DocumentTypeToJson(DocumentType t) => t switch
    {
        DocumentType.Receipt => "receipt",
        DocumentType.AbbreviatedTaxInvoice => "abbreviated_tax_invoice",
        DocumentType.FullTaxInvoice => "full_tax_invoice",
        DocumentType.ReceiptTaxInvoice => "receipt_tax_invoice",
        DocumentType.Invoice => "invoice",
        DocumentType.CreditNote => "credit_note",
        DocumentType.DebitNote => "debit_note",
        _ => "unknown",
    };

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>Trims and cuts to the column size (see EntityConfigurations) so a rambling model answer can't fail the save.</summary>
    private static string? Clean(string? s, int maxLength) => Clean(s) is { } v && v.Length > maxLength ? v[..maxLength] : Clean(s);

    private static decimal? Money(decimal? d) => d is null ? null : Math.Round(d.Value, 2, MidpointRounding.AwayFromZero);
}
