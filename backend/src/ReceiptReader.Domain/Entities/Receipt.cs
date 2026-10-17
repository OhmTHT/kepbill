using ReceiptReader.Domain.Enums;

namespace ReceiptReader.Domain.Entities;

public class Receipt
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // ---- ownership ----
    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }
    public Direction Direction { get; set; } = Direction.Purchase;
    /// <summary>Accounting month "yyyy-MM". Defaults to the issue date's month but may be moved (late input-VAT claims).</summary>
    public string? Period { get; set; }

    // ---- source file (all null for a document typed in by hand) ----
    public string OriginalFileName { get; set; } = "";
    public string? StoredPath { get; set; }
    public string? ContentType { get; set; }
    /// <summary>SHA-256 of the uploaded bytes, used to reject duplicate uploads within the same company.</summary>
    public string? Sha256 { get; set; }

    // ---- processing ----
    public ReceiptStatus Status { get; set; } = ReceiptStatus.Pending;
    public string? ExtractorName { get; set; }
    public string? ModelName { get; set; }
    /// <summary>The raw JSON the model returned, kept for debugging and future fine-tuning.</summary>
    public string? RawExtraction { get; set; }
    /// <summary>OCR text when the OCR+LLM extractor was used.</summary>
    public string? OcrText { get; set; }
    public string? ProcessingError { get; set; }
    public List<ValidationIssue> Issues { get; set; } = [];
    public int ProcessingAttempts { get; set; }

    // ---- document header ----
    public DocumentType DocumentType { get; set; } = DocumentType.Unknown;
    public string? DocumentNumber { get; set; }
    public DateOnly? IssueDate { get; set; }
    public DateOnly? DueDate { get; set; }
    /// <summary>Credit/debit notes: the number of the invoice being adjusted, as printed.</summary>
    public string? ReferenceDocumentNumber { get; set; }
    /// <summary>The adjusted invoice, when it is in the system (matched by number and counterparty).</summary>
    public Guid? ReferenceReceiptId { get; set; }

    public string? SellerName { get; set; }
    public string? SellerTaxId { get; set; }
    /// <summary>"00000" = สำนักงานใหญ่, otherwise branch number.</summary>
    public string? SellerBranch { get; set; }
    public string? SellerAddress { get; set; }

    public string? BuyerName { get; set; }
    public string? BuyerTaxId { get; set; }
    public string? BuyerBranch { get; set; }
    public string? BuyerAddress { get; set; }

    /// <summary>
    /// Our tax id as the model read it from the document, before our side was replaced by the company profile.
    /// Used only to warn when the document seems to belong to another company or the wrong side was chosen.
    /// </summary>
    public string? OwnTaxIdOnDocument { get; set; }

    // ---- amounts (THB) ----
    public decimal? Subtotal { get; set; }
    public decimal? Discount { get; set; }
    public decimal? VatAmount { get; set; }
    public decimal? Total { get; set; }
    /// <summary>True when the printed prices already include VAT (common on retail receipts).</summary>
    public bool VatIncluded { get; set; }
    public string Currency { get; set; } = "THB";
    public string? PaymentMethod { get; set; }

    // ---- withholding tax (ภาษีหัก ณ ที่จ่าย) ----
    /// <summary>Rate in percent (3 = services, 5 = rent …); null when nothing is withheld.</summary>
    public decimal? WhtRate { get; set; }
    /// <summary>
    /// Baht withheld on this document, normally the rate × the amount before VAT. It is settled through payments
    /// (<see cref="Payment.WhtAmount"/>): we keep it back from a supplier, or a customer keeps it back from us.
    /// </summary>
    public decimal? WhtAmount { get; set; }

    // ---- bookkeeping ----
    /// <summary>Expense account (purchases) or revenue account (sales) the net amount posts to.</summary>
    public Guid? AccountId { get; set; }
    public Account? Account { get; set; }
    public string? Notes { get; set; }

    public Guid? VendorId { get; set; }
    public Vendor? Vendor { get; set; }

    public List<ReceiptLineItem> LineItems { get; set; } = [];
    public List<Payment> Payments { get; set; } = [];

    // ---- audit ----
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessedAt { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? ApprovedBy { get; set; }

    // ---- sides ----
    public string? CounterpartyName => Direction == Direction.Purchase ? SellerName : BuyerName;
    public string? CounterpartyTaxId => Direction == Direction.Purchase ? SellerTaxId : BuyerTaxId;

    public bool IsAdjustmentNote => DocumentType is DocumentType.CreditNote or DocumentType.DebitNote;

    /// <summary>Typed in by hand: there is no file for the model to read.</summary>
    public bool IsManual => StoredPath is null;
}

public class ReceiptLineItem
{
    // Left empty on purpose: EF generates it when the item is discovered through Receipt.LineItems.
    // (A pre-set key on a generated-key entity makes EF treat it as existing → UPDATE of a missing row.)
    public Guid Id { get; set; }
    public Guid ReceiptId { get; set; }
    public int LineNo { get; set; }
    public string Description { get; set; } = "";
    public decimal? Quantity { get; set; }
    public decimal? UnitPrice { get; set; }
    /// <summary>Discount on this line in baht (positive number).</summary>
    public decimal? Discount { get; set; }
    public decimal? Amount { get; set; }
}

/// <summary>Money paid (purchases) or received (sales) against a document.</summary>
public class Payment
{
    public Guid Id { get; set; } // generated by EF, see ReceiptLineItem.Id
    public Guid ReceiptId { get; set; }
    public DateOnly Date { get; set; }
    /// <summary>The money that moved (cash or bank).</summary>
    public decimal Amount { get; set; }
    /// <summary>
    /// Withholding tax settled with this payment, on top of <see cref="Amount"/>: kept back from the supplier
    /// (purchases, owed to the Revenue Department) or kept back by the customer (sales, a tax credit for us).
    /// </summary>
    public decimal WhtAmount { get; set; }
    /// <summary>The cash or bank account the money moved through.</summary>
    public Guid AccountId { get; set; }
    public Account? Account { get; set; }
    public string? Note { get; set; }
    /// <summary>Created on approval of a receipt-type document (paid on the spot); removed on unapprove.</summary>
    public bool IsAuto { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
