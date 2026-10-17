using ReceiptReader.Application.Accounting;
using ReceiptReader.Application.Companies;
using ReceiptReader.Application.Extraction;
using ReceiptReader.Application.Learning;
using ReceiptReader.Application.Validation;
using ReceiptReader.Domain.Entities;
using ReceiptReader.Domain.Enums;

namespace ReceiptReader.Application.Tests;

internal static class Fixtures
{
    public const string OurTaxId = "0107567000414";
    public const string TheirTaxId = "0105536092641";
    public static readonly DateOnly Today = new(2026, 10, 13);

    public static Company Company() => new() { Name = "บริษัท เรา จำกัด", TaxId = OurTaxId, Branch = "00000", VatRegistered = true };

    public static ChartLookup Chart(Company c) => new(DefaultChart.Create(c.Id));

    public static Receipt Purchase(DocumentType type = DocumentType.FullTaxInvoice) => new()
    {
        Direction = Direction.Purchase,
        DocumentType = type,
        DocumentNumber = "IV-001",
        IssueDate = new DateOnly(2026, 10, 1),
        Period = "2026-10",
        SellerName = "ร้านคู่ค้า",
        SellerTaxId = TheirTaxId,
        BuyerName = "บริษัท เรา จำกัด",
        BuyerTaxId = OurTaxId,
        Subtotal = 1000m,
        VatAmount = 70m,
        Total = 1070m,
    };

    public static Receipt Sale(DocumentType type = DocumentType.FullTaxInvoice)
    {
        var r = Purchase(type);
        r.Direction = Direction.Sale;
        (r.SellerName, r.BuyerName) = (r.BuyerName, r.SellerName);
        (r.SellerTaxId, r.BuyerTaxId) = (r.BuyerTaxId, r.SellerTaxId);
        return r;
    }

    public static decimal Dr(JournalEntry e, AccountRole role, ChartLookup chart) =>
        e.Lines.Where(l => l.AccountId == chart.ByRole(role).Id).Sum(l => l.Debit);

    public static decimal Cr(JournalEntry e, AccountRole role, ChartLookup chart) =>
        e.Lines.Where(l => l.AccountId == chart.ByRole(role).Id).Sum(l => l.Credit);
}

public class JournalBuilderTests
{
    [Fact]
    public void PurchaseTaxInvoicePostsExpenseInputVatAndPayable()
    {
        var c = Fixtures.Company();
        var chart = Fixtures.Chart(c);
        var e = JournalBuilder.ForDocument(Fixtures.Purchase(), c.VatRegistered, chart)!;

        Assert.Equal(1000m, Fixtures.Dr(e, AccountRole.DefaultExpense, chart));
        Assert.Equal(70m, Fixtures.Dr(e, AccountRole.InputVat, chart));
        Assert.Equal(1070m, Fixtures.Cr(e, AccountRole.AccountsPayable, chart));
        Assert.Equal(e.TotalDebit, e.TotalCredit);
    }

    [Fact]
    public void AbbreviatedTaxInvoiceVatIsNotClaimable()
    {
        var c = Fixtures.Company();
        var chart = Fixtures.Chart(c);
        var e = JournalBuilder.ForDocument(Fixtures.Purchase(DocumentType.AbbreviatedTaxInvoice), c.VatRegistered, chart)!;

        Assert.Equal(1070m, Fixtures.Dr(e, AccountRole.DefaultExpense, chart));
        Assert.Equal(0m, Fixtures.Dr(e, AccountRole.InputVat, chart));
        Assert.Equal(e.TotalDebit, e.TotalCredit);
    }

    [Fact]
    public void NonVatCompanyExpensesTheVat()
    {
        var c = Fixtures.Company();
        c.VatRegistered = false;
        var chart = Fixtures.Chart(c);
        var e = JournalBuilder.ForDocument(Fixtures.Purchase(), c.VatRegistered, chart)!;
        Assert.Equal(1070m, Fixtures.Dr(e, AccountRole.DefaultExpense, chart));
    }

    [Fact]
    public void ChosenAccountIsUsed()
    {
        var c = Fixtures.Company();
        var accounts = DefaultChart.Create(c.Id);
        var chart = new ChartLookup(accounts);
        var travel = accounts.Single(a => a.Code == "5211");
        var r = Fixtures.Purchase();
        r.AccountId = travel.Id;

        var e = JournalBuilder.ForDocument(r, true, chart)!;
        Assert.Contains(e.Lines, l => l.AccountId == travel.Id && l.Debit == 1000m);
    }

    [Fact]
    public void SalePostsReceivableRevenueAndOutputVat()
    {
        var c = Fixtures.Company();
        var chart = Fixtures.Chart(c);
        var e = JournalBuilder.ForDocument(Fixtures.Sale(), c.VatRegistered, chart)!;

        Assert.Equal(1070m, Fixtures.Dr(e, AccountRole.AccountsReceivable, chart));
        Assert.Equal(1000m, Fixtures.Cr(e, AccountRole.SalesRevenue, chart));
        Assert.Equal(70m, Fixtures.Cr(e, AccountRole.OutputVat, chart));
    }

    [Fact]
    public void PurchaseCreditNoteReversesTheInvoice()
    {
        var c = Fixtures.Company();
        var chart = Fixtures.Chart(c);
        var note = Fixtures.Purchase(DocumentType.CreditNote);
        note.Subtotal = 100m; note.VatAmount = 7m; note.Total = 107m;

        var e = JournalBuilder.ForDocument(note, c.VatRegistered, chart)!;
        Assert.Equal(107m, Fixtures.Dr(e, AccountRole.AccountsPayable, chart));
        Assert.Equal(100m, Fixtures.Cr(e, AccountRole.DefaultExpense, chart));
        Assert.Equal(7m, Fixtures.Cr(e, AccountRole.InputVat, chart));
    }

    [Fact]
    public void SaleCreditNoteGoesToSalesReturns()
    {
        var c = Fixtures.Company();
        var chart = Fixtures.Chart(c);
        var e = JournalBuilder.ForDocument(Fixtures.Sale(DocumentType.CreditNote), c.VatRegistered, chart)!;
        Assert.Equal(1000m, Fixtures.Dr(e, AccountRole.SalesReturns, chart));
        Assert.Equal(70m, Fixtures.Dr(e, AccountRole.OutputVat, chart));
        Assert.Equal(1070m, Fixtures.Cr(e, AccountRole.AccountsReceivable, chart));
    }

    [Fact]
    public void PaymentInALaterMonthClearsThePayable()
    {
        // Billed in January, paid in March: the expense lands in January, the cash leaves in March.
        var c = Fixtures.Company();
        var chart = Fixtures.Chart(c);
        var r = Fixtures.Purchase();
        r.IssueDate = new DateOnly(2026, 1, 20);
        r.Period = "2026-01";
        var p = new Payment { Date = new DateOnly(2026, 3, 5), Amount = 1070m, AccountId = chart.ByRole(AccountRole.Bank).Id };

        var doc = JournalBuilder.ForDocument(r, true, chart)!;
        var pay = JournalBuilder.ForPayment(r, p, chart);

        Assert.Equal("2026-01", doc.Period);
        Assert.Equal("2026-03", pay.Period);
        Assert.Equal(1070m, Fixtures.Dr(pay, AccountRole.AccountsPayable, chart));
        Assert.Equal(1070m, Fixtures.Cr(pay, AccountRole.Bank, chart));
    }

    [Fact]
    public void ReceivingMoneyFromACustomer()
    {
        var c = Fixtures.Company();
        var chart = Fixtures.Chart(c);
        var p = new Payment { Date = Fixtures.Today, Amount = 500m, AccountId = chart.ByRole(AccountRole.Cash).Id };
        var e = JournalBuilder.ForPayment(Fixtures.Sale(), p, chart);
        Assert.Equal(500m, Fixtures.Dr(e, AccountRole.Cash, chart));
        Assert.Equal(500m, Fixtures.Cr(e, AccountRole.AccountsReceivable, chart));
    }
}

public class PaymentMathTests
{
    [Fact]
    public void UnpaidPartialPaid()
    {
        var r = Fixtures.Purchase();
        Assert.Equal(PaymentStatus.Unpaid, PaymentMath.Compute(r, 0).Status);

        r.Payments.Add(new Payment { Amount = 500m });
        var partial = PaymentMath.Compute(r, 0);
        Assert.Equal(PaymentStatus.Partial, partial.Status);
        Assert.Equal(570m, partial.Outstanding);

        r.Payments.Add(new Payment { Amount = 570m });
        Assert.Equal(PaymentStatus.Paid, PaymentMath.Compute(r, 0).Status);
    }

    [Fact]
    public void CreditNoteReducesWhatIsOwed()
    {
        var r = Fixtures.Purchase();
        var note = Fixtures.Purchase(DocumentType.CreditNote);
        note.Total = 107m;
        note.Status = ReceiptStatus.Approved;
        r.Payments.Add(new Payment { Amount = 963m });

        var state = PaymentMath.Compute(r, PaymentMath.Adjustments([note]));
        Assert.Equal(-107m, state.Adjustments);
        Assert.Equal(PaymentStatus.Paid, state.Status);
    }

    [Fact]
    public void UnapprovedNotesDoNotCount() =>
        Assert.Equal(0m, PaymentMath.Adjustments([Fixtures.Purchase(DocumentType.CreditNote)]));

    [Theory]
    [InlineData("เงินสด", AccountRole.Cash)]
    [InlineData("CASH", AccountRole.Cash)]
    [InlineData("บัตรเครดิต", AccountRole.Bank)]
    [InlineData("โอน", AccountRole.Bank)]
    [InlineData(null, AccountRole.Cash)]
    public void GuessesPaymentAccount(string? method, AccountRole expected) =>
        Assert.Equal(expected, PaymentMath.AccountRoleFor(method));
}

public class PeriodsTests
{
    [Fact]
    public void Basics()
    {
        Assert.Equal("2026-01", Periods.Of(new DateOnly(2026, 1, 31)));
        Assert.Equal("2026-03", Periods.Normalize(" 2026-03 "));
        Assert.Null(Periods.Normalize("2026-13"));
        Assert.Equal(2, Periods.MonthsBetween("2026-01", "2026-03"));
        Assert.Equal(-11, Periods.MonthsBetween("2026-12", "2026-01"));
        Assert.Equal((new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28)), Periods.Range("2026-02"));
    }
}

public class CompanySideTests
{
    [Fact]
    public void PurchaseUsesProfileAsBuyerAndRemembersWhatWasPrinted()
    {
        var r = new Receipt { Direction = Direction.Purchase, BuyerName = "อ่านผิด", BuyerTaxId = "0105561000011" };
        CompanySide.CaptureAndApply(r, Fixtures.Company());
        Assert.Equal("บริษัท เรา จำกัด", r.BuyerName);
        Assert.Equal(Fixtures.OurTaxId, r.BuyerTaxId);
        Assert.Equal("0105561000011", r.OwnTaxIdOnDocument);
    }

    [Fact]
    public void SaleUsesProfileAsSeller()
    {
        var r = new Receipt { Direction = Direction.Sale, SellerName = "?", BuyerName = "ลูกค้า" };
        CompanySide.CaptureAndApply(r, Fixtures.Company());
        Assert.Equal(Fixtures.OurTaxId, r.SellerTaxId);
        Assert.Equal("ลูกค้า", r.CounterpartyName);
    }
}

public class CompanyRulesTests
{
    private static readonly ValidationContext Ctx = ValidationContext.For(Fixtures.Company(), ["2026-09"]);

    [Fact]
    public void CleanPurchaseHasNoIssues()
    {
        var r = Fixtures.Purchase();
        r.OwnTaxIdOnDocument = Fixtures.OurTaxId;
        Assert.Empty(ReceiptValidator.Validate(r, Fixtures.Today, Ctx));
    }

    [Fact]
    public void WarnsWhenDocumentIsAddressedToAnotherCompany()
    {
        var r = Fixtures.Purchase();
        r.OwnTaxIdOnDocument = "0105561000011";
        Assert.Contains(ReceiptValidator.Validate(r, Fixtures.Today, Ctx), i => i.Code == "OWN_TAXID_MISMATCH");
    }

    [Fact]
    public void WarnsWhenCounterpartyIsOurselves()
    {
        var r = Fixtures.Purchase();
        r.SellerTaxId = Fixtures.OurTaxId;
        Assert.Contains(ReceiptValidator.Validate(r, Fixtures.Today, Ctx), i => i.Code == "COUNTERPARTY_IS_SELF");
    }

    [Fact]
    public void ClosedPeriodBlocksUnapprovedDocuments()
    {
        var r = Fixtures.Purchase();
        r.IssueDate = new DateOnly(2026, 9, 15);
        r.Period = "2026-09";
        Assert.Contains(ReceiptValidator.Validate(r, Fixtures.Today, Ctx), i => i.Code == "PERIOD_CLOSED");

        r.Period = "2026-10"; // moved to the open month: a late input-VAT claim is allowed
        Assert.DoesNotContain(ReceiptValidator.Validate(r, Fixtures.Today, Ctx), i => i.Code.StartsWith("PERIOD"));
    }

    [Fact]
    public void PeriodCannotPrecedeTheDocument()
    {
        var r = Fixtures.Purchase();
        r.Period = "2026-08";
        Assert.Contains(ReceiptValidator.Validate(r, Fixtures.Today, Ctx), i => i.Code == "PERIOD_BEFORE_DATE");
    }

    [Fact]
    public void SaleMustStayInItsMonth()
    {
        var r = Fixtures.Sale();
        r.Period = "2026-11";
        Assert.Contains(ReceiptValidator.Validate(r, Fixtures.Today, Ctx), i => i.Code == "PERIOD_SALE_MONTH");
    }

    [Fact]
    public void SaleTaxInvoiceNeedsBuyer()
    {
        var r = Fixtures.Sale();
        r.BuyerName = null;
        Assert.Contains(ReceiptValidator.Validate(r, Fixtures.Today, Ctx), i => i.Code == "BUYER_MISSING");
    }

    [Fact]
    public void CreditNoteShouldReferenceTheInvoice()
    {
        var r = Fixtures.Purchase(DocumentType.CreditNote);
        r.OwnTaxIdOnDocument = Fixtures.OurTaxId;
        Assert.Contains(ReceiptValidator.Validate(r, Fixtures.Today, Ctx), i => i.Code == "REF_MISSING");
        r.ReferenceDocumentNumber = "IV-000";
        Assert.DoesNotContain(ReceiptValidator.Validate(r, Fixtures.Today, Ctx), i => i.Code == "REF_MISSING");
    }

    [Fact]
    public void LineDiscountIsPartOfTheLineMath()
    {
        var r = Fixtures.Purchase();
        r.OwnTaxIdOnDocument = Fixtures.OurTaxId;
        r.LineItems =
        [
            new ReceiptLineItem { LineNo = 1, Description = "A", Quantity = 2, UnitPrice = 300m, Discount = 100m, Amount = 500m },
            new ReceiptLineItem { LineNo = 2, Description = "B", Quantity = 1, UnitPrice = 500m, Amount = 500m },
        ];
        Assert.Empty(ReceiptValidator.Validate(r, Fixtures.Today, Ctx));

        r.LineItems[0].Discount = null; // 2 × 300 ≠ 500 without the discount
        Assert.Contains(ReceiptValidator.Validate(r, Fixtures.Today, Ctx), i => i.Code == "LINE_CALC");
    }
}

public class ExtractionContractTests
{
    [Fact]
    public void MapsLineDiscountDueDateAndCreditNote()
    {
        const string json = """
            {"document_type":"credit_note","document_number":"CN-01","reference_document_number":"IV-001",
             "issue_date_raw":"13/10/69","issue_date":"2569-10-13","due_date":"2569-11-30",
             "items":[{"description":"คืนสินค้า","quantity":1,"unit_price":100,"discount":-10,"amount":90}],
             "total":96.3,"vat_amount":6.3}
            """;
        var r = new Receipt();
        ReceiptMapper.Apply(ExtractedReceipt.Parse(json), r);

        Assert.Equal(DocumentType.CreditNote, r.DocumentType);
        Assert.Equal("IV-001", r.ReferenceDocumentNumber);
        Assert.Equal(new DateOnly(2026, 11, 30), r.DueDate);
        Assert.Equal(10m, r.LineItems[0].Discount);
        Assert.Contains("\"discount\":10", ReceiptMapper.ToExtracted(r).ToJson());
    }

    [Fact]
    public void PromptNamesOurSide()
    {
        var hint = ReceiptPrompt.ContextHint(new ExtractionContext(true, "บริษัท เรา จำกัด", Fixtures.OurTaxId));
        Assert.Contains("PURCHASE", hint);
        Assert.Contains("concentrate on the SELLER", hint, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("", ReceiptPrompt.ContextHint(null));
    }
}

public class LearningCompareTests
{
    [Fact]
    public void FindsTheFieldsAPersonCorrected()
    {
        var approved = Fixtures.Purchase();
        approved.Total = 1070m;
        var aiSaid = ReceiptMapper.ToExtracted(approved);
        aiSaid.Total = 1010m;          // misread
        aiSaid.SellerName = "ร้านคู่ค้า  "; // whitespace only — not counted as wrong
        approved.RawExtraction = aiSaid.ToJson();

        Assert.Equal(["total"], LearningService.Compare(approved));
    }

    [Fact]
    public void UnreadableRawIsSkipped()
    {
        var approved = Fixtures.Purchase();
        approved.RawExtraction = "not json";
        Assert.Null(LearningService.Compare(approved));
    }
}

public class WithholdingTaxTests
{
    // A 6,500 service + 455 VAT with 3% withheld: 195 kept back, 6,760 actually paid.
    private static Receipt Service(Direction direction)
    {
        var r = direction == Direction.Purchase ? Fixtures.Purchase() : Fixtures.Sale();
        (r.Subtotal, r.VatAmount, r.Total) = (6500m, 455m, 6955m);
        (r.WhtRate, r.WhtAmount) = (3m, 195m);
        return r;
    }

    [Fact]
    public void PayingASupplierKeepsTheTaxBackAsALiability()
    {
        var chart = Fixtures.Chart(Fixtures.Company());
        var p = new Payment { Date = Fixtures.Today, Amount = 6760m, WhtAmount = 195m, AccountId = chart.ByRole(AccountRole.Bank).Id };
        var e = JournalBuilder.ForPayment(Service(Direction.Purchase), p, chart);

        Assert.Equal(6955m, Fixtures.Dr(e, AccountRole.AccountsPayable, chart));
        Assert.Equal(6760m, Fixtures.Cr(e, AccountRole.Bank, chart));
        Assert.Equal(195m, Fixtures.Cr(e, AccountRole.WhtPayable, chart));
    }

    [Fact]
    public void ACustomerWithholdingGivesUsATaxCredit()
    {
        var chart = Fixtures.Chart(Fixtures.Company());
        var p = new Payment { Date = Fixtures.Today, Amount = 6760m, WhtAmount = 195m, AccountId = chart.ByRole(AccountRole.Bank).Id };
        var e = JournalBuilder.ForPayment(Service(Direction.Sale), p, chart);

        Assert.Equal(6760m, Fixtures.Dr(e, AccountRole.Bank, chart));
        Assert.Equal(195m, Fixtures.Dr(e, AccountRole.WhtReceivable, chart));
        Assert.Equal(6955m, Fixtures.Cr(e, AccountRole.AccountsReceivable, chart));
    }

    [Fact]
    public void WithheldTaxCountsTowardsSettlement()
    {
        var r = Service(Direction.Purchase);
        var before = PaymentMath.Compute(r, 0);
        Assert.Equal(195m, before.WhtRemaining);

        r.Payments.Add(new Payment { Amount = 6760m, WhtAmount = 195m });
        var after = PaymentMath.Compute(r, 0);
        Assert.Equal(PaymentStatus.Paid, after.Status);
        Assert.Equal(0m, after.Outstanding);
        Assert.Equal(0m, after.WhtRemaining);
    }

    [Fact]
    public void AmountThatDoesNotMatchTheRateIsFlagged()
    {
        var r = Service(Direction.Purchase);
        Assert.DoesNotContain(ReceiptValidator.Validate(r, Fixtures.Today), i => i.Code.StartsWith("WHT_"));

        r.WhtAmount = 325m; // 5% instead of 3%
        Assert.Contains(ReceiptValidator.Validate(r, Fixtures.Today), i => i.Code == "WHT_MISMATCH");
    }

    [Fact]
    public void EveryCompanyGetsTheWithholdingAccounts()
    {
        var chart = Fixtures.Chart(Fixtures.Company());
        Assert.Equal(AccountType.Liability, chart.ByRole(AccountRole.WhtPayable).Type);
        Assert.Equal(AccountType.Asset, chart.ByRole(AccountRole.WhtReceivable).Type);
    }
}
