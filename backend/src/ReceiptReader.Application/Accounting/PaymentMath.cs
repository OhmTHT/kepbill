using ReceiptReader.Domain.Entities;
using ReceiptReader.Domain.Enums;

namespace ReceiptReader.Application.Accounting;

/// <param name="Paid">Settled so far: money plus withholding tax.</param>
/// <param name="WhtRemaining">Withholding tax of the document not settled yet (part of <paramref name="Outstanding"/>).</param>
public sealed record PaymentState(decimal Paid, decimal Adjustments, decimal Outstanding, PaymentStatus Status, decimal WhtRemaining = 0m);

/// <summary>How much of a document is still owed, after payments and linked credit/debit notes.</summary>
public static class PaymentMath
{
    private const decimal Epsilon = 0.005m;

    /// <summary>Net effect of approved notes that point at a document: debit notes add, credit notes subtract.</summary>
    public static decimal Adjustments(IEnumerable<Receipt> notes) =>
        notes.Where(n => n.Status == ReceiptStatus.Approved)
            .Sum(n => (n.Total ?? 0m) * (n.DocumentType == DocumentType.CreditNote ? -1 : 1));

    public static PaymentState Compute(Receipt r, decimal adjustments)
    {
        var paid = r.Payments.Sum(p => p.Amount + p.WhtAmount);
        // Notes settle through the invoice they adjust; a document without a total has nothing to settle yet.
        if (r.IsAdjustmentNote || r.Total is not > 0) return new PaymentState(paid, 0m, 0m, PaymentStatus.NotApplicable);

        var outstanding = (r.Total ?? 0m) + adjustments - paid;
        var status = outstanding <= Epsilon ? PaymentStatus.Paid
            : paid <= Epsilon ? PaymentStatus.Unpaid
            : PaymentStatus.Partial;
        outstanding = Math.Max(0m, outstanding);
        var whtRemaining = Math.Min(outstanding, Math.Max(0m, (r.WhtAmount ?? 0m) - r.Payments.Sum(p => p.WhtAmount)));
        return new PaymentState(paid, adjustments, outstanding, status, whtRemaining);
    }

    /// <summary>What withholding tax is computed on: the amount before VAT.</summary>
    public static decimal? WhtBase(Receipt r) => r.Total is { } total ? total - (r.VatAmount ?? 0m) : null;

    /// <summary>Which payment account a printed payment method most likely went through.</summary>
    public static AccountRole AccountRoleFor(string? paymentMethod)
    {
        if (string.IsNullOrWhiteSpace(paymentMethod)) return AccountRole.Cash;
        var m = paymentMethod.ToLowerInvariant();
        return m.Contains("เงินสด") || m.Contains("cash") ? AccountRole.Cash : AccountRole.Bank;
    }
}
