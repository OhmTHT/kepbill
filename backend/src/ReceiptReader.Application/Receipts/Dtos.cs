using ReceiptReader.Application.Accounting;
using ReceiptReader.Domain.Entities;
using ReceiptReader.Domain.Enums;

namespace ReceiptReader.Application.Receipts;

public sealed record UploadResult(Guid Id, string FileName, bool Duplicate);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public sealed record ReceiptListQuery(
    Guid CompanyId,
    Direction? Direction = null,
    /// <summary>Any of these statuses; null or empty = every status.</summary>
    IReadOnlyCollection<ReceiptStatus>? Statuses = null,
    string? Period = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 50);

public sealed record ReceiptSummaryDto(
    Guid Id,
    string OriginalFileName,
    ReceiptStatus Status,
    Direction Direction,
    string? Period,
    DocumentType DocumentType,
    string? DocumentNumber,
    DateOnly? IssueDate,
    DateOnly? DueDate,
    string? CounterpartyName,
    string? CounterpartyTaxId,
    decimal? Total,
    decimal? VatAmount,
    string? AccountName,
    PaymentStatus PaymentStatus,
    decimal Outstanding,
    int ErrorCount,
    int WarningCount,
    DateTimeOffset CreatedAt);

public sealed record LineItemDto(
    int LineNo, string Description, decimal? Quantity, decimal? UnitPrice, decimal? Discount, decimal? Amount);

public sealed record PaymentDto(
    Guid Id, DateOnly Date, decimal Amount, Guid AccountId, string? AccountName, string? Note, bool IsAuto, decimal WhtAmount);

public sealed record JournalLineDto(Guid AccountId, string AccountCode, string AccountName, decimal Debit, decimal Credit);

public sealed record JournalEntryDto(
    DateOnly Date,
    string Period,
    string Kind,
    Guid ReceiptId,
    Guid? PaymentId,
    string? DocumentNumber,
    string Description,
    IReadOnlyList<JournalLineDto> Lines);

public sealed record ReferenceDto(Guid Id, string? DocumentNumber, DateOnly? IssueDate, decimal? Total);

public sealed record ReceiptDetailDto(
    Guid Id,
    Guid CompanyId,
    string? CompanyName,
    Direction Direction,
    string? Period,
    bool PeriodClosed,
    string OriginalFileName,
    /// <summary>Null for a manual entry (no file).</summary>
    string? FileUrl,
    string? ContentType,
    ReceiptStatus Status,
    string? ExtractorName,
    string? ModelName,
    string? ProcessingError,
    IReadOnlyList<ValidationIssue> Issues,
    DocumentType DocumentType,
    string? DocumentNumber,
    DateOnly? IssueDate,
    DateOnly? DueDate,
    string? ReferenceDocumentNumber,
    ReferenceDto? Reference,
    IReadOnlyList<ReferenceDto> Adjustments,
    string? SellerName,
    string? SellerTaxId,
    string? SellerBranch,
    string? SellerAddress,
    string? BuyerName,
    string? BuyerTaxId,
    string? BuyerBranch,
    string? BuyerAddress,
    string? OwnTaxIdOnDocument,
    decimal? Subtotal,
    decimal? Discount,
    decimal? VatAmount,
    decimal? Total,
    bool VatIncluded,
    string Currency,
    string? PaymentMethod,
    Guid? AccountId,
    string? AccountName,
    string? Notes,
    Guid? VendorId,
    string? VendorName,
    IReadOnlyList<LineItemDto> LineItems,
    IReadOnlyList<PaymentDto> Payments,
    decimal Paid,
    decimal AdjustmentTotal,
    decimal Outstanding,
    PaymentStatus PaymentStatus,
    IReadOnlyList<JournalEntryDto> Journal,
    string? RawExtraction,
    string? OcrText,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ProcessedAt,
    DateTimeOffset? ApprovedAt,
    string? ApprovedBy,
    /// <summary>Pinned as a Training AI example (always sent first, never rotated out).</summary>
    bool ExamplePinned,
    decimal? WhtRate,
    decimal? WhtAmount,
    /// <summary>Withholding tax not settled by a payment yet; the next payment's default.</summary>
    decimal WhtRemaining);

/// <summary>Everything a reviewer can correct on the review screen. Our side is ignored (it comes from the profile).</summary>
public sealed record ReceiptUpdateDto(
    Direction Direction,
    string? Period,
    DocumentType DocumentType,
    string? DocumentNumber,
    DateOnly? IssueDate,
    DateOnly? DueDate,
    string? ReferenceDocumentNumber,
    string? SellerName,
    string? SellerTaxId,
    string? SellerBranch,
    string? SellerAddress,
    string? BuyerName,
    string? BuyerTaxId,
    string? BuyerBranch,
    string? BuyerAddress,
    decimal? Subtotal,
    decimal? Discount,
    decimal? VatAmount,
    decimal? Total,
    bool VatIncluded,
    string? Currency,
    string? PaymentMethod,
    Guid? AccountId,
    string? Notes,
    IReadOnlyList<LineItemDto> LineItems,
    decimal? WhtRate = null,
    decimal? WhtAmount = null);

public sealed record ApproveRequest(bool Force = false, string? ApprovedBy = null);

/// <summary>
/// A document typed in by hand. The draft form only needs the company and side; validating and saving also send
/// what was typed (<paramref name="Data"/>, whose direction wins over <paramref name="Direction"/>).
/// </summary>
public sealed record ManualReceiptRequest(Guid CompanyId, Direction Direction, ReceiptUpdateDto? Data = null);

/// <param name="Amount">Money paid or received.</param>
/// <param name="WhtAmount">Withholding tax settled in the same payment (kept back, not paid).</param>
public sealed record PaymentInput(DateOnly Date, decimal Amount, Guid? AccountId, string? Note, decimal WhtAmount = 0m);

public sealed record ValidatePreviewResult(IReadOnlyList<ValidationIssue> Issues);

public sealed record StatusCountDto(ReceiptStatus Status, int Count);

public static class DtoMapping
{
    public static ReceiptSummaryDto ToSummary(this Receipt r, PaymentState pay) => new(
        r.Id, r.OriginalFileName, r.Status, r.Direction, r.Period, r.DocumentType, r.DocumentNumber,
        r.IssueDate, r.DueDate, r.CounterpartyName, r.CounterpartyTaxId,
        r.Total, r.VatAmount, r.Account?.Name, pay.Status, pay.Outstanding,
        r.Issues.Count(i => i.Severity == IssueSeverity.Error),
        r.Issues.Count(i => i.Severity == IssueSeverity.Warning),
        r.CreatedAt);

    public static JournalEntryDto ToDto(this JournalEntry e) => new(
        e.Date, e.Period, e.Kind, e.ReceiptId, e.PaymentId, e.DocumentNumber, e.Description,
        e.Lines.Select(l => new JournalLineDto(l.AccountId, l.AccountCode, l.AccountName, l.Debit, l.Credit)).ToList());

    public static ReferenceDto ToReference(this Receipt r) => new(r.Id, r.DocumentNumber, r.IssueDate, r.Total);

    public static PaymentDto ToDto(this Payment p) =>
        new(p.Id, p.Date, p.Amount, p.AccountId, p.Account?.Name, p.Note, p.IsAuto, p.WhtAmount);
}
