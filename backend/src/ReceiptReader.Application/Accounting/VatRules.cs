using ReceiptReader.Domain.Entities;
using ReceiptReader.Domain.Enums;

namespace ReceiptReader.Application.Accounting;

/// <summary>Thai VAT rules that decide which documents carry claimable input VAT or output VAT.</summary>
public static class VatRules
{
    /// <summary>Documents that must show the seller's tax id and a document number.</summary>
    public static bool IsTaxDocument(DocumentType t) => t is DocumentType.FullTaxInvoice
        or DocumentType.AbbreviatedTaxInvoice or DocumentType.ReceiptTaxInvoice
        or DocumentType.CreditNote or DocumentType.DebitNote;

    /// <summary>Documents that must name the buyer — the only ones that support an input-VAT claim.</summary>
    public static bool IsFullTaxDocument(DocumentType t) => t is DocumentType.FullTaxInvoice
        or DocumentType.ReceiptTaxInvoice or DocumentType.CreditNote or DocumentType.DebitNote;

    /// <summary>A receipt is proof the money already changed hands at the issue date.</summary>
    public static bool IsPaidOnIssue(DocumentType t) => t is DocumentType.Receipt
        or DocumentType.AbbreviatedTaxInvoice or DocumentType.ReceiptTaxInvoice;

    /// <summary>
    /// Input VAT we may claim. VAT on an abbreviated tax invoice or a plain receipt can't be claimed,
    /// so it stays part of the expense.
    /// </summary>
    public static decimal ClaimableInputVat(Receipt r, bool vatRegistered) =>
        vatRegistered && r.Direction == Direction.Purchase && IsFullTaxDocument(r.DocumentType)
            ? r.VatAmount ?? 0m
            : 0m;

    public static decimal OutputVat(Receipt r, bool vatRegistered) =>
        vatRegistered && r.Direction == Direction.Sale ? r.VatAmount ?? 0m : 0m;

    /// <summary>The VAT that posts to the VAT accounts (input or output) rather than to expense/revenue.</summary>
    public static decimal RecognizedVat(Receipt r, bool vatRegistered) =>
        r.Direction == Direction.Purchase ? ClaimableInputVat(r, vatRegistered) : OutputVat(r, vatRegistered);

    /// <summary>Credit notes reverse the direction of every amount.</summary>
    public static int Sign(Receipt r) => r.DocumentType == DocumentType.CreditNote ? -1 : 1;
}
