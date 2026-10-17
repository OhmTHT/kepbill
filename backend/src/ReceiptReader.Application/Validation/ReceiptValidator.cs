using ReceiptReader.Application.Accounting;
using ReceiptReader.Domain.Entities;
using ReceiptReader.Domain.Enums;

namespace ReceiptReader.Application.Validation;

/// <summary>What the rules need to know besides the document itself.</summary>
/// <param name="CompanyTaxId">Our tax id from the profile (empty when the profile isn't filled in).</param>
/// <param name="ClosedPeriods">Closed months of the document's company, "yyyy-MM".</param>
public sealed record ValidationContext(string? CompanyTaxId, IReadOnlySet<string> ClosedPeriods)
{
    /// <summary>No company profile and no closed periods — for previews and tests.</summary>
    public static readonly ValidationContext None = new(null, new HashSet<string>());

    public static ValidationContext For(Company c, IEnumerable<string> closedPeriods) =>
        new(string.IsNullOrWhiteSpace(c.TaxId) ? null : c.TaxId, closedPeriods.ToHashSet());
}

/// <summary>
/// Accounting sanity rules. These catch most model mistakes, because a wrong digit almost always
/// breaks an arithmetic identity. Errors block approval (unless forced); warnings only flag for review.
/// </summary>
public static class ReceiptValidator
{
    public const decimal VatRate = 0.07m;
    /// <summary>Allowed rounding difference in baht.</summary>
    public const decimal Tolerance = 1.00m;
    /// <summary>Input VAT may be claimed up to six months after the tax invoice's month.</summary>
    public const int InputVatClaimMonths = 6;

    public static List<ValidationIssue> Validate(Receipt r, DateOnly today) => Validate(r, today, ValidationContext.None);

    public static List<ValidationIssue> Validate(Receipt r, DateOnly today, ValidationContext ctx)
    {
        var issues = new List<ValidationIssue>();
        void Error(string code, string? field, string msg) => issues.Add(new(code, IssueSeverity.Error, field, msg));
        void Warn(string code, string? field, string msg) => issues.Add(new(code, IssueSeverity.Warning, field, msg));

        var isPurchase = r.Direction == Direction.Purchase;
        var isTaxDocument = VatRules.IsTaxDocument(r.DocumentType);
        var isFullTaxDocument = VatRules.IsFullTaxDocument(r.DocumentType);
        var counterpartyNameField = isPurchase ? "sellerName" : "buyerName";
        var counterpartyTaxField = isPurchase ? "sellerTaxId" : "buyerTaxId";
        var counterpartyLabel = isPurchase ? "ผู้ขาย" : "ผู้ซื้อ";

        // ---- header ----
        if (r.DocumentType == DocumentType.Unknown)
            Warn("DOC_TYPE_UNKNOWN", "documentType", "ระบุประเภทเอกสารไม่ได้");

        if (r.IssueDate is null)
            Error("DATE_MISSING", "issueDate", "ไม่พบวันที่ในเอกสาร");
        else if (r.IssueDate > today.AddDays(1))
            Error("DATE_FUTURE", "issueDate", $"วันที่ {r.IssueDate:yyyy-MM-dd} อยู่ในอนาคต (อาจแปลง พ.ศ./ค.ศ. ผิด)");
        else if (r.IssueDate < today.AddYears(-2))
            Warn("DATE_OLD", "issueDate", $"วันที่ {r.IssueDate:yyyy-MM-dd} เก่ากว่า 2 ปี ตรวจสอบปีอีกครั้ง");

        if (r.DueDate is { } due && r.IssueDate is { } issued && due < issued)
            Warn("DUE_BEFORE_ISSUE", "dueDate", "วันครบกำหนดชำระอยู่ก่อนวันที่เอกสาร");

        if (isTaxDocument && string.IsNullOrWhiteSpace(r.DocumentNumber))
            Error("DOC_NUMBER_MISSING", "documentNumber", "ใบกำกับภาษี/ใบลดหนี้/ใบเพิ่มหนี้ต้องมีเลขที่เอกสาร");

        if (r.IsAdjustmentNote && string.IsNullOrWhiteSpace(r.ReferenceDocumentNumber))
            Warn("REF_MISSING", "referenceDocumentNumber", "ใบลดหนี้/ใบเพิ่มหนี้ควรอ้างอิงเลขที่ใบกำกับภาษีเดิม");

        // ---- period ----
        if (r.Period is { } period)
        {
            var issueMonth = r.IssueDate is { } d ? Periods.Of(d) : null;
            if (r.Status != ReceiptStatus.Approved && ctx.ClosedPeriods.Contains(period))
                Error("PERIOD_CLOSED", "period", $"งวด {period} ปิดแล้ว ย้ายเอกสารไปงวดที่ยังเปิดอยู่");

            if (issueMonth is not null)
            {
                var lag = Periods.MonthsBetween(issueMonth, period);
                if (lag < 0)
                    Error("PERIOD_BEFORE_DATE", "period", $"งวด {period} อยู่ก่อนเดือนของวันที่เอกสาร ({issueMonth})");
                else if (isPurchase && lag > InputVatClaimMonths && (r.VatAmount ?? 0) > 0)
                    Warn("PERIOD_LATE", "period", $"ภาษีซื้อขอคืนได้ภายใน {InputVatClaimMonths} เดือนนับจากเดือนที่ออกใบกำกับ");
                else if (!isPurchase && lag != 0)
                    Warn("PERIOD_SALE_MONTH", "period", $"ภาษีขายต้องยื่นในเดือนที่ออกใบกำกับ ({issueMonth})");
            }
        }

        // ---- counterparty ----
        if (string.IsNullOrWhiteSpace(r.CounterpartyName))
        {
            if (!isPurchase && isFullTaxDocument)
                Warn("BUYER_MISSING", "buyerName", "ใบกำกับภาษีเต็มรูปต้องระบุชื่อผู้ซื้อ");
            else
                Warn("COUNTERPARTY_MISSING", counterpartyNameField, $"ไม่พบชื่อ{counterpartyLabel}");
        }

        if (!string.IsNullOrEmpty(r.SellerTaxId) && !ThaiTaxId.IsValid(r.SellerTaxId))
            Error("SELLER_TAXID_INVALID", "sellerTaxId", "เลขผู้เสียภาษีผู้ขายไม่ถูกต้อง (checksum ไม่ผ่าน)");
        else if (isTaxDocument && string.IsNullOrEmpty(r.SellerTaxId))
        {
            if (isPurchase) Error("SELLER_TAXID_MISSING", "sellerTaxId", "ใบกำกับภาษีต้องมีเลขผู้เสียภาษีของผู้ขาย");
            else Error("PROFILE_INCOMPLETE", null, "ยังไม่ได้กรอกเลขผู้เสียภาษีในโปรไฟล์บริษัท");
        }

        if (!string.IsNullOrEmpty(r.BuyerTaxId) && !ThaiTaxId.IsValid(r.BuyerTaxId))
            Error("BUYER_TAXID_INVALID", "buyerTaxId", "เลขผู้เสียภาษีผู้ซื้อไม่ถูกต้อง (checksum ไม่ผ่าน)");

        // ---- our side vs. what the document printed ----
        if (ctx.CompanyTaxId is { } ours)
        {
            if (r.CounterpartyTaxId == ours)
                Warn("COUNTERPARTY_IS_SELF", counterpartyTaxField,
                    $"{counterpartyLabel}เป็นบริษัทเราเอง — อาจเลือกรายจ่าย/รายรับผิด");

            if (ThaiTaxId.IsValid(r.OwnTaxIdOnDocument) && r.OwnTaxIdOnDocument != ours)
                Warn("OWN_TAXID_MISMATCH", null,
                    $"เลขผู้เสียภาษีฝั่งเราบนเอกสาร ({r.OwnTaxIdOnDocument}) ไม่ตรงกับบริษัท — อาจเลือกบริษัทหรือรายจ่าย/รายรับผิด");
            else if (isPurchase && isFullTaxDocument && r.OwnTaxIdOnDocument is null && r.RawExtraction is not null)
                Warn("OWN_TAXID_NOT_PRINTED", null,
                    "ไม่พบเลขผู้เสียภาษีของบริษัทบนใบกำกับภาษีเต็มรูป — ถ้าไม่มีจริงจะใช้ขอคืนภาษีซื้อไม่ได้");
        }

        // ---- amounts ----
        foreach (var (name, value) in new[]
                 {
                     ("subtotal", r.Subtotal), ("discount", r.Discount),
                     ("vatAmount", r.VatAmount), ("total", r.Total),
                 })
        {
            if (value < 0) Error("NEGATIVE_AMOUNT", name, "จำนวนเงินติดลบ");
        }

        if (r.Total is null)
            Error("TOTAL_MISSING", "total", "ไม่พบยอดรวม");
        else if (r.Total == 0)
            Error("TOTAL_ZERO", "total", "ยอดรวมเป็น 0");

        var discount = r.Discount ?? 0m;
        var vat = r.VatAmount ?? 0m;

        // subtotal - discount (+ vat when exclusive) = total. Receipts differ in whether "subtotal"
        // is before or after VAT, so accept either identity and only flag when neither fits.
        if (r.Total is { } total && r.Subtotal is { } subtotal)
        {
            var inclusive = subtotal - discount;
            var exclusive = subtotal - discount + vat;
            if (!Near(total, inclusive) && !Near(total, exclusive))
                Error("TOTAL_MISMATCH", "total",
                    $"ยอดรวม {total:N2} ไม่ตรงกับ ยอดก่อนภาษี {subtotal:N2} − ส่วนลด {discount:N2} (+ VAT {vat:N2})");
        }

        // VAT should be 7% — of the pre-VAT base (exclusive) or 7/107 of the total (inclusive).
        if (r.VatAmount is > 0 && r.Total is { } t2)
        {
            var candidates = new List<decimal> { t2 * 7m / 107m };
            if (r.Subtotal is { } s2)
            {
                candidates.Add((s2 - discount) * VatRate);
                candidates.Add((s2 - discount) * 7m / 107m);
            }
            candidates.Add((t2 - vat) * VatRate);

            if (!candidates.Any(c => Near(vat, c)))
                Warn("VAT_RATE", "vatAmount", $"VAT {vat:N2} ไม่ใช่ 7% ของยอด (คาดว่าราว {t2 * 7m / 107m:N2})");
        }
        else if (isTaxDocument && (r.VatAmount is null or 0))
        {
            Warn("VAT_MISSING", "vatAmount", "ใบกำกับภาษีแต่ไม่พบยอด VAT");
        }

        // ---- line items ----
        for (var i = 0; i < r.LineItems.Count; i++)
        {
            var li = r.LineItems[i];
            var lineDiscount = li.Discount ?? 0m;
            if (li.Discount < 0)
                Error("NEGATIVE_AMOUNT", $"lineItems.{i}.discount", $"บรรทัด {i + 1}: ส่วนลดติดลบ");
            if (li is { Quantity: { } q, UnitPrice: { } p, Amount: { } a } && !Near(q * p - lineDiscount, a))
                Warn("LINE_CALC", $"lineItems.{i}.amount", lineDiscount == 0
                    ? $"บรรทัด {i + 1}: {q:0.##} × {p:N2} = {q * p:N2} แต่ระบุ {a:N2}"
                    : $"บรรทัด {i + 1}: {q:0.##} × {p:N2} − ส่วนลด {lineDiscount:N2} = {q * p - lineDiscount:N2} แต่ระบุ {a:N2}");
        }

        var amounts = r.LineItems.Select(li => li.Amount).ToList();
        if (amounts.Count > 0 && amounts.All(a => a is not null) && r.Total is { } t3)
        {
            var sum = amounts.Sum(a => a!.Value);
            // The line sum can match the subtotal, the gross before discount, or the total.
            var targets = new List<decimal> { t3, t3 + discount, t3 - vat, t3 - vat + discount };
            if (r.Subtotal is { } s3) targets.Add(s3);
            if (!targets.Any(x => Near(sum, x)))
                Warn("LINES_MISMATCH", "lineItems",
                    $"ผลรวมรายการ {sum:N2} ไม่ตรงกับยอดก่อนภาษีหรือยอดรวม (อาจอ่านรายการไม่ครบ)");
        }

        // ---- withholding tax ----
        if (r.WhtAmount is { } wht)
        {
            if (r.Total is { } whtTotal && wht >= whtTotal)
                Error("WHT_TOO_LARGE", "whtAmount", "ยอดหัก ณ ที่จ่ายต้องน้อยกว่ายอดรวม");
            else if (r.WhtRate is { } rate && PaymentMath.WhtBase(r) is { } whtBase)
            {
                var expected = Math.Round(whtBase * rate / 100m, 2, MidpointRounding.AwayFromZero);
                if (Math.Abs(expected - wht) > Tolerance)
                    Warn("WHT_MISMATCH", "whtAmount",
                        $"หัก ณ ที่จ่าย {rate:0.##}% ของยอดก่อน VAT {whtBase:N2} ควรเป็น {expected:N2} (กรอกไว้ {wht:N2})");
            }
        }

        return issues;
    }

    public static bool HasErrors(IEnumerable<ValidationIssue> issues) =>
        issues.Any(i => i.Severity == IssueSeverity.Error);

    /// <summary>Lower is better; used to pick between two extraction attempts.</summary>
    public static int Score(IEnumerable<ValidationIssue> issues) =>
        issues.Sum(i => i.Severity == IssueSeverity.Error ? 10 : 1);

    private static bool Near(decimal a, decimal b) => Math.Abs(a - b) <= Tolerance;
}
