using ReceiptReader.Domain.Entities;
using ReceiptReader.Domain.Enums;

namespace ReceiptReader.Application.Accounting;

public sealed record JournalLine(Guid AccountId, string AccountCode, string AccountName, decimal Debit, decimal Credit);

/// <param name="Kind">"document" (the invoice itself) or "payment" (money paid/received against it).</param>
public sealed record JournalEntry(
    DateOnly Date,
    string Period,
    string Kind,
    Guid ReceiptId,
    Guid? PaymentId,
    string? DocumentNumber,
    string Description,
    IReadOnlyList<JournalLine> Lines)
{
    public decimal TotalDebit => Lines.Sum(l => l.Debit);
    public decimal TotalCredit => Lines.Sum(l => l.Credit);
}

/// <summary>A company's chart of accounts, looked up by id or by the role the generator posts to.</summary>
public sealed class ChartLookup
{
    private readonly Dictionary<Guid, Account> _byId;
    private readonly Dictionary<AccountRole, Account> _byRole;

    public ChartLookup(IEnumerable<Account> accounts)
    {
        var list = accounts.ToList();
        _byId = list.ToDictionary(a => a.Id);
        _byRole = list.Where(a => a.Role != AccountRole.None)
            .GroupBy(a => a.Role)
            .ToDictionary(g => g.Key, g => g.OrderBy(a => a.Code, StringComparer.Ordinal).First());
    }

    public Account ByRole(AccountRole role) => _byRole.TryGetValue(role, out var a)
        ? a
        : throw new InvalidOperationException($"ผังบัญชีไม่มีบัญชีสำหรับ {role}");

    public Account? ById(Guid? id) => id is { } v && _byId.TryGetValue(v, out var a) ? a : null;
}

/// <summary>
/// Turns approved documents and their payments into double-entry journal entries (Dr = Cr).
/// Entries are derived, not stored: they always reflect the current document, and closing a period
/// is what freezes them.
/// <code>
/// Purchase invoice   Dr expense (net) + Dr input VAT   / Cr accounts payable (total)
/// Payment            Dr accounts payable               / Cr cash or bank
/// Sale invoice       Dr accounts receivable (total)    / Cr revenue (net) + Cr output VAT
/// Receipt of money   Dr cash or bank                   / Cr accounts receivable
/// Credit note        the same lines with Dr and Cr swapped (debit note = like an invoice)
/// </code>
/// </summary>
public static class JournalBuilder
{
    public static JournalEntry? ForDocument(Receipt r, bool vatRegistered, ChartLookup chart)
    {
        var total = r.Total ?? 0m;
        if (total == 0m || r.IssueDate is null) return null;

        var vat = VatRules.RecognizedVat(r, vatRegistered);
        var net = total - vat;
        var sign = VatRules.Sign(r);

        List<(Account Account, decimal Amount)> postings = r.Direction == Direction.Purchase
            ?
            [
                (chart.ById(r.AccountId) ?? chart.ByRole(AccountRole.DefaultExpense), net),
                (chart.ByRole(AccountRole.InputVat), vat),
                (chart.ByRole(AccountRole.AccountsPayable), -total),
            ]
            :
            [
                (chart.ByRole(AccountRole.AccountsReceivable), total),
                (chart.ById(r.AccountId) ?? chart.ByRole(r.DocumentType == DocumentType.CreditNote
                    ? AccountRole.SalesReturns
                    : AccountRole.SalesRevenue), -net),
                (chart.ByRole(AccountRole.OutputVat), -vat),
            ];

        var what = (r.Direction, r.DocumentType) switch
        {
            (_, DocumentType.CreditNote) => "ลดหนี้",
            (_, DocumentType.DebitNote) => "เพิ่มหนี้",
            (Direction.Purchase, _) => "ซื้อ",
            _ => "ขาย",
        };

        return new JournalEntry(r.IssueDate.Value, r.Period ?? Periods.Of(r.IssueDate.Value), "document", r.Id, null,
            r.DocumentNumber, Describe(what, r), ToLines(postings, sign));
    }

    public static JournalEntry ForPayment(Receipt r, Payment p, ChartLookup chart)
    {
        var cash = chart.ById(p.AccountId) ?? chart.ByRole(AccountRole.Cash);
        var settled = p.Amount + p.WhtAmount;
        // Withholding tax settles part of the debt without money moving: we owe it to the Revenue Department
        // (purchases), or the customer paid it there for us (sales).
        List<(Account Account, decimal Amount)> postings = r.Direction == Direction.Purchase
            ?
            [
                (chart.ByRole(AccountRole.AccountsPayable), settled),
                (cash, -p.Amount),
                (chart.ByRole(AccountRole.WhtPayable), -p.WhtAmount),
            ]
            :
            [
                (cash, p.Amount),
                (chart.ByRole(AccountRole.WhtReceivable), p.WhtAmount),
                (chart.ByRole(AccountRole.AccountsReceivable), -settled),
            ];

        var what = r.Direction == Direction.Purchase ? "จ่ายชำระ" : "รับชำระ";
        return new JournalEntry(p.Date, Periods.Of(p.Date), "payment", r.Id, p.Id, r.DocumentNumber,
            Describe(what, r), ToLines(postings, VatRules.Sign(r)));
    }

    private static string Describe(string what, Receipt r) =>
        string.Join(" ", new[] { what, r.CounterpartyName, r.DocumentNumber }.Where(s => !string.IsNullOrWhiteSpace(s)));

    /// <summary>Positive = debit, negative = credit. Zero lines are dropped; the sign flips everything for credit notes.</summary>
    private static List<JournalLine> ToLines(IEnumerable<(Account Account, decimal Amount)> postings, int sign) =>
        postings
            .Select(p => (p.Account, Amount: p.Amount * sign))
            .Where(p => p.Amount != 0m)
            .Select(p => new JournalLine(p.Account.Id, p.Account.Code, p.Account.Name,
                Debit: p.Amount > 0 ? p.Amount : 0m,
                Credit: p.Amount < 0 ? -p.Amount : 0m))
            .ToList();
}
