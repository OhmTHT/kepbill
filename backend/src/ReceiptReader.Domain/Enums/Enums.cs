namespace ReceiptReader.Domain.Enums;

public enum ReceiptStatus
{
    /// <summary>Uploaded, waiting in the processing queue.</summary>
    Pending = 0,
    /// <summary>Currently being read by the extractor.</summary>
    Processing = 1,
    /// <summary>Extracted, but at least one validation rule raised an error or warning.</summary>
    NeedsReview = 2,
    /// <summary>Extracted and every validation rule passed — still needs a human click to approve.</summary>
    Validated = 3,
    /// <summary>Confirmed by a person; safe to use for bookkeeping.</summary>
    Approved = 4,
    /// <summary>Extraction crashed (model offline, unreadable file, ...).</summary>
    Failed = 5,
}

public enum DocumentType
{
    Unknown = 0,
    /// <summary>ใบเสร็จรับเงิน (ไม่ใช่ใบกำกับภาษี)</summary>
    Receipt = 1,
    /// <summary>ใบกำกับภาษีอย่างย่อ</summary>
    AbbreviatedTaxInvoice = 2,
    /// <summary>ใบกำกับภาษีเต็มรูป</summary>
    FullTaxInvoice = 3,
    /// <summary>ใบเสร็จรับเงิน/ใบกำกับภาษี (เอกสารรวม)</summary>
    ReceiptTaxInvoice = 4,
    /// <summary>ใบแจ้งหนี้ / ใบวางบิล (ยังไม่ใช่ใบกำกับภาษี)</summary>
    Invoice = 5,
    /// <summary>ใบลดหนี้ — reduces an earlier invoice (returns, after-sale discount).</summary>
    CreditNote = 6,
    /// <summary>ใบเพิ่มหนี้ — increases an earlier invoice.</summary>
    DebitNote = 7,
}

public enum IssueSeverity
{
    Warning = 0,
    Error = 1,
}

/// <summary>Which side of the document our company is on.</summary>
public enum Direction
{
    /// <summary>ฝั่งซื้อ — we are the buyer; the AI reads the seller.</summary>
    Purchase = 0,
    /// <summary>ฝั่งขาย — we are the seller; the AI reads the buyer.</summary>
    Sale = 1,
}

public enum PaymentStatus
{
    /// <summary>Credit/debit notes settle through the document they adjust.</summary>
    NotApplicable = 0,
    Unpaid = 1,
    Partial = 2,
    Paid = 3,
}

public enum UserRole
{
    /// <summary>Records income and expense documents in assigned companies; cannot approve.</summary>
    User = 0,
    /// <summary>Bookkeeper: records and approves, and sees payments, periods and the journal of assigned companies.</summary>
    Approver = 1,
    /// <summary>Runs assigned companies (profile, chart of accounts, their approvers and users); cannot add companies.</summary>
    Admin = 2,
    /// <summary>Runs the whole system: every company, every user, Training AI.</summary>
    SuperAdmin = 3,
}

public enum StockMovementType
{
    /// <summary>ยอดยกมา: stock on hand before the company started using the system.</summary>
    Opening = 0,
    Purchase = 1,
    Sale = 2,
    /// <summary>Returned to the supplier.</summary>
    PurchaseReturn = 3,
    /// <summary>Returned by a customer.</summary>
    SaleReturn = 4,
    TransferOut = 5,
    TransferIn = 6,
    /// <summary>Counted, damaged or lost: the difference between the books and the shelf.</summary>
    Adjustment = 7,
}

public enum AccountType
{
    Asset = 0,
    Liability = 1,
    Equity = 2,
    Revenue = 3,
    Expense = 4,
}

/// <summary>
/// Accounts the journal generator posts to automatically. Each company has exactly one account per role
/// (seeded with the default chart); the user may rename them or change their codes.
/// </summary>
public enum AccountRole
{
    None = 0,
    Cash = 1,
    Bank = 2,
    AccountsReceivable = 3,
    AccountsPayable = 4,
    InputVat = 5,
    OutputVat = 6,
    SalesRevenue = 7,
    SalesReturns = 8,
    DefaultExpense = 9,
    /// <summary>Withholding tax kept back from suppliers, to file with ภ.ง.ด.3/53 (liability).</summary>
    WhtPayable = 10,
    /// <summary>Withholding tax customers kept back from us, credited against our income tax (asset).</summary>
    WhtReceivable = 11,
}
