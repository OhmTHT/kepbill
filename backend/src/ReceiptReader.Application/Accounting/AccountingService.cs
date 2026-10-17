using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ReceiptReader.Application.Abstractions;
using ReceiptReader.Application.Companies;
using ReceiptReader.Application.Receipts;
using ReceiptReader.Domain.Entities;
using ReceiptReader.Domain.Enums;

namespace ReceiptReader.Application.Accounting;

public sealed record SideSummaryDto(int Count, int Approved, decimal Net, decimal Vat, decimal Total);

public sealed record PeriodSummaryDto(
    string Period,
    bool Closed,
    DateTimeOffset? ClosedAt,
    string? ClosedBy,
    SideSummaryDto Purchases,
    SideSummaryDto Sales,
    /// <summary>ภาษีขาย − ภาษีซื้อ (negative = refundable).</summary>
    decimal VatPayable,
    int PendingCount,
    decimal PaymentsOut,
    decimal PaymentsIn);

public sealed record JournalDto(
    string Period, IReadOnlyList<JournalEntryDto> Entries, decimal TotalDebit, decimal TotalCredit);

public sealed record TrialBalanceRowDto(
    Guid AccountId, string Code, string Name, AccountType Type,
    decimal PeriodDebit, decimal PeriodCredit, decimal BalanceDebit, decimal BalanceCredit);

public sealed record TrialBalanceDto(string Period, IReadOnlyList<TrialBalanceRowDto> Rows);

public sealed record OutstandingItemDto(
    Guid Id, string? DocumentNumber, DocumentType DocumentType, string? CounterpartyName, string? CounterpartyTaxId,
    DateOnly? IssueDate, DateOnly? DueDate, decimal Total, decimal Paid, decimal Adjustments, decimal Outstanding,
    int DaysOverdue, string Bucket, decimal WhtRemaining);

public sealed record OutstandingDto(
    Direction Direction, IReadOnlyList<OutstandingItemDto> Items,
    IReadOnlyDictionary<string, decimal> Buckets, decimal Total);

public sealed record ClosePeriodRequest(string? ClosedBy = null);

/// <summary>Month-end work: period summaries, closing, the journal and trial balance, and who still owes what.</summary>
public sealed class AccountingService(IAppDbContext db, IClock clock)
{
    // ---------------- periods ----------------

    public async Task<IReadOnlyList<PeriodSummaryDto>> PeriodsAsync(Guid companyId, CancellationToken ct)
    {
        var ctx = await db.LoadCompanyContextAsync(companyId, ct);
        var docs = await db.Receipts.AsNoTracking().Include(r => r.Payments)
            .Where(r => r.CompanyId == companyId)
            .ToListAsync(ct);
        var closed = await db.ClosedPeriods.AsNoTracking().Where(p => p.CompanyId == companyId).ToListAsync(ct);

        var approved = docs.Where(d => d.Status == ReceiptStatus.Approved).ToList();
        var payments = approved.SelectMany(d => d.Payments.Select(p => (Doc: d, Payment: p))).ToList();

        var periods = docs.Select(d => d.Period)
            .Concat(payments.Select(x => Periods.Of(x.Payment.Date)))
            .Concat(closed.Select(c => c.Period))
            .Append(Periods.Of(clock.TodayInThailand))
            .OfType<string>()
            .Distinct()
            .OrderByDescending(p => p, StringComparer.Ordinal);

        var vat = ctx.Company.VatRegistered;
        return periods.Select(p =>
        {
            var inPeriod = docs.Where(d => d.Period == p).ToList();
            var c = closed.FirstOrDefault(x => x.Period == p);
            var purchases = Side(inPeriod.Where(d => d.Direction == Direction.Purchase), vat);
            var sales = Side(inPeriod.Where(d => d.Direction == Direction.Sale), vat);
            var paid = payments.Where(x => Periods.Of(x.Payment.Date) == p).ToList();
            return new PeriodSummaryDto(p, c is not null, c?.ClosedAt, c?.ClosedBy, purchases, sales,
                VatPayable: sales.Vat - purchases.Vat,
                PendingCount: inPeriod.Count(d => d.Status != ReceiptStatus.Approved),
                PaymentsOut: paid.Where(x => x.Doc.Direction == Direction.Purchase).Sum(x => x.Payment.Amount * VatRules.Sign(x.Doc)),
                PaymentsIn: paid.Where(x => x.Doc.Direction == Direction.Sale).Sum(x => x.Payment.Amount * VatRules.Sign(x.Doc)));
        }).ToList();
    }

    public async Task CloseAsync(Guid companyId, string period, ClosePeriodRequest req, CancellationToken ct)
    {
        var p = Periods.Normalize(period) ?? throw new BadRequestException("รูปแบบงวดต้องเป็น yyyy-MM");
        if (!await db.Companies.AnyAsync(c => c.Id == companyId, ct)) throw new NotFoundException($"ไม่พบบริษัท {companyId}");
        if (await db.ClosedPeriods.AnyAsync(c => c.CompanyId == companyId && c.Period == p, ct)) return;

        var pending = await db.Receipts.CountAsync(r => r.CompanyId == companyId && r.Period == p
                                                         && r.Status != ReceiptStatus.Approved, ct);
        if (pending > 0)
            throw new ConflictException($"งวด {p} ยังมีเอกสารที่ไม่ได้อนุมัติ {pending} ใบ — อนุมัติ ลบ หรือย้ายงวดก่อนปิด");

        db.ClosedPeriods.Add(new ClosedPeriod
        {
            CompanyId = companyId,
            Period = p,
            ClosedAt = clock.Now,
            ClosedBy = string.IsNullOrWhiteSpace(req.ClosedBy) ? null : req.ClosedBy.Trim(),
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task ReopenAsync(Guid companyId, string period, CancellationToken ct)
    {
        var p = Periods.Normalize(period) ?? throw new BadRequestException("รูปแบบงวดต้องเป็น yyyy-MM");
        await db.ClosedPeriods.Where(c => c.CompanyId == companyId && c.Period == p).ExecuteDeleteAsync(ct);
    }

    // ---------------- journal ----------------

    public async Task<JournalDto> JournalAsync(Guid companyId, string period, CancellationToken ct)
    {
        var p = Periods.Normalize(period) ?? throw new BadRequestException("รูปแบบงวดต้องเป็น yyyy-MM");
        var ctx = await db.LoadCompanyContextAsync(companyId, ct);
        var entries = (await EntriesAsync(ctx, ct))
            .Where(e => e.Period == p)
            .OrderBy(e => e.Date).ThenBy(e => e.Kind == "payment").ThenBy(e => e.DocumentNumber)
            .ToList();
        return new JournalDto(p, entries.Select(e => e.ToDto()).ToList(),
            entries.Sum(e => e.TotalDebit), entries.Sum(e => e.TotalCredit));
    }

    /// <summary>Movement in the period plus the running balance at its end, per account.</summary>
    public async Task<TrialBalanceDto> TrialBalanceAsync(Guid companyId, string period, CancellationToken ct)
    {
        var p = Periods.Normalize(period) ?? throw new BadRequestException("รูปแบบงวดต้องเป็น yyyy-MM");
        var ctx = await db.LoadCompanyContextAsync(companyId, ct);
        var lines = (await EntriesAsync(ctx, ct))
            .Where(e => string.CompareOrdinal(e.Period, p) <= 0)
            .SelectMany(e => e.Lines.Select(l => (e.Period, Line: l)))
            .ToList();

        var rows = ctx.Accounts
            .Select(a =>
            {
                var mine = lines.Where(x => x.Line.AccountId == a.Id).ToList();
                var inPeriod = mine.Where(x => x.Period == p).ToList();
                var balance = mine.Sum(x => x.Line.Debit - x.Line.Credit);
                return new TrialBalanceRowDto(a.Id, a.Code, a.Name, a.Type,
                    inPeriod.Sum(x => x.Line.Debit), inPeriod.Sum(x => x.Line.Credit),
                    balance > 0 ? balance : 0m, balance < 0 ? -balance : 0m);
            })
            .Where(r => r.PeriodDebit != 0 || r.PeriodCredit != 0 || r.BalanceDebit != 0 || r.BalanceCredit != 0)
            .OrderBy(r => r.Code, StringComparer.Ordinal)
            .ToList();
        return new TrialBalanceDto(p, rows);
    }

    /// <summary>The journal as CSV (UTF-8 with BOM so Excel shows Thai correctly).</summary>
    public async Task<byte[]> JournalCsvAsync(Guid companyId, string period, CancellationToken ct)
    {
        var journal = await JournalAsync(companyId, period, ct);
        var sb = new StringBuilder();
        sb.AppendLine("วันที่,งวด,ประเภท,เลขที่เอกสาร,คำอธิบาย,รหัสบัญชี,ชื่อบัญชี,เดบิต,เครดิต");
        foreach (var e in journal.Entries)
            foreach (var l in e.Lines)
                sb.AppendLine(string.Join(",",
                    e.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), e.Period,
                    e.Kind == "payment" ? "ชำระเงิน" : "เอกสาร", Csv(e.DocumentNumber), Csv(e.Description),
                    Csv(l.AccountCode), Csv(l.AccountName),
                    l.Debit.ToString("0.00", CultureInfo.InvariantCulture),
                    l.Credit.ToString("0.00", CultureInfo.InvariantCulture)));
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }

    // ---------------- payables / receivables ----------------

    public async Task<OutstandingDto> OutstandingAsync(Guid companyId, Direction direction, CancellationToken ct)
    {
        var docs = await db.Receipts.AsNoTracking().Include(r => r.Payments)
            .Where(r => r.CompanyId == companyId && r.Direction == direction && r.Status == ReceiptStatus.Approved)
            .ToListAsync(ct);
        var adjustments = docs.Where(d => d.ReferenceReceiptId != null)
            .GroupBy(d => d.ReferenceReceiptId!.Value)
            .ToDictionary(g => g.Key, g => PaymentMath.Adjustments(g));

        var today = clock.TodayInThailand;
        var items = docs
            .Where(d => !d.IsAdjustmentNote)
            .Select(d => (Doc: d, Pay: PaymentMath.Compute(d, adjustments.GetValueOrDefault(d.Id))))
            .Where(x => x.Pay.Status is PaymentStatus.Unpaid or PaymentStatus.Partial)
            .Select(x =>
            {
                var due = x.Doc.DueDate ?? x.Doc.IssueDate;
                var overdue = due is { } d ? today.DayNumber - d.DayNumber : 0;
                return new OutstandingItemDto(x.Doc.Id, x.Doc.DocumentNumber, x.Doc.DocumentType,
                    x.Doc.CounterpartyName, x.Doc.CounterpartyTaxId, x.Doc.IssueDate, x.Doc.DueDate,
                    x.Doc.Total ?? 0m, x.Pay.Paid, x.Pay.Adjustments, x.Pay.Outstanding, Math.Max(0, overdue), Bucket(overdue), x.Pay.WhtRemaining);
            })
            .OrderByDescending(i => i.DaysOverdue).ThenBy(i => i.CounterpartyName)
            .ToList();

        var buckets = BucketNames.ToDictionary(b => b, b => items.Where(i => i.Bucket == b).Sum(i => i.Outstanding));
        return new OutstandingDto(direction, items, buckets, items.Sum(i => i.Outstanding));
    }

    // ------------------------------------------------------------------

    private static readonly string[] BucketNames = ["ยังไม่ครบกำหนด", "1-30 วัน", "31-60 วัน", "61-90 วัน", "เกิน 90 วัน"];

    private static string Bucket(int daysOverdue) => daysOverdue switch
    {
        <= 0 => BucketNames[0],
        <= 30 => BucketNames[1],
        <= 60 => BucketNames[2],
        <= 90 => BucketNames[3],
        _ => BucketNames[4],
    };

    private static SideSummaryDto Side(IEnumerable<Receipt> docs, bool vatRegistered)
    {
        var list = docs.ToList();
        var approved = list.Where(d => d.Status == ReceiptStatus.Approved).ToList();
        decimal Sum(Func<Receipt, decimal> f) => approved.Sum(d => f(d) * VatRules.Sign(d));
        return new SideSummaryDto(list.Count, approved.Count,
            Net: Sum(d => (d.Total ?? 0m) - VatRules.RecognizedVat(d, vatRegistered)),
            Vat: Sum(d => VatRules.RecognizedVat(d, vatRegistered)),
            Total: Sum(d => d.Total ?? 0m));
    }

    /// <summary>Every journal entry of the company: approved documents and the payments against them.</summary>
    private async Task<List<JournalEntry>> EntriesAsync(CompanyContext ctx, CancellationToken ct)
    {
        var docs = await db.Receipts.AsNoTracking().Include(r => r.Payments)
            .Where(r => r.CompanyId == ctx.Company.Id && r.Status == ReceiptStatus.Approved)
            .ToListAsync(ct);
        var entries = new List<JournalEntry>();
        foreach (var d in docs)
        {
            if (JournalBuilder.ForDocument(d, ctx.Company.VatRegistered, ctx.Chart) is { } e) entries.Add(e);
            entries.AddRange(d.Payments.Select(p => JournalBuilder.ForPayment(d, p, ctx.Chart)));
        }
        return entries;
    }

    private static string Csv(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
    }
}
