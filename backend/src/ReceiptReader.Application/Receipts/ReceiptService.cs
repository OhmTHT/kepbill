using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ReceiptReader.Application.Abstractions;
using ReceiptReader.Application.Accounting;
using ReceiptReader.Application.Companies;
using ReceiptReader.Application.Extraction;
using ReceiptReader.Application.Validation;
using ReceiptReader.Domain.Entities;
using ReceiptReader.Domain.Enums;

namespace ReceiptReader.Application.Receipts;

/// <summary>Use cases behind the HTTP API: upload, list, review/edit, approve, reprocess, payments.</summary>
public sealed class ReceiptService(
    IAppDbContext db,
    IFileStorage storage,
    IProcessingQueue queue,
    IClock clock,
    IOptions<ProcessingOptions> options)
{
    private static readonly Dictionary<string, string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp",
        ["image/bmp"] = ".bmp",
        ["image/tiff"] = ".tiff",
        ["image/gif"] = ".gif",
        ["application/pdf"] = ".pdf",
    };

    public static IReadOnlyCollection<string> AllowedContentTypes => AllowedTypes.Keys;

    public async Task<UploadResult> UploadAsync(
        Stream content, string fileName, string contentType, Guid companyId, Direction direction, CancellationToken ct)
    {
        if (!AllowedTypes.TryGetValue(contentType, out var ext))
            throw new BadRequestException(
                $"ไม่รองรับไฟล์ {contentType} (รองรับ: {string.Join(", ", AllowedTypes.Keys)})");
        if (!await db.Companies.AnyAsync(c => c.Id == companyId, ct))
            throw new BadRequestException("ไม่พบบริษัทที่เลือก");

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        if (buffer.Length == 0) throw new BadRequestException("ไฟล์ว่าง");
        if (buffer.Length > options.Value.MaxUploadBytes)
            throw new BadRequestException($"ไฟล์ใหญ่เกิน {options.Value.MaxUploadBytes / 1024 / 1024} MB");

        var hash = Convert.ToHexStringLower(SHA256.HashData(buffer.GetBuffer().AsSpan(0, (int)buffer.Length)));

        // Duplicates are per company: the same file may legitimately belong to two companies (buyer and seller).
        var existing = await db.Receipts.AsNoTracking()
            .Where(r => r.CompanyId == companyId && r.Sha256 == hash)
            .Select(r => new { r.Id })
            .FirstOrDefaultAsync(ct);
        if (existing is not null) return new UploadResult(existing.Id, fileName, Duplicate: true);

        buffer.Position = 0;
        var path = await storage.SaveAsync(buffer, ext, ct);

        var receipt = new Receipt
        {
            CompanyId = companyId,
            Direction = direction,
            OriginalFileName = Path.GetFileName(fileName),
            StoredPath = path,
            ContentType = contentType,
            Sha256 = hash,
            Status = ReceiptStatus.Pending,
            CreatedAt = clock.Now,
        };
        db.Receipts.Add(receipt);
        await db.SaveChangesAsync(ct);
        await queue.EnqueueAsync(receipt.Id, ct);

        return new UploadResult(receipt.Id, receipt.OriginalFileName, Duplicate: false);
    }

    /// <summary>
    /// The blank form for a document typed in by hand (no file, no model). Nothing is stored: the document only
    /// exists once the reviewer saves it with <see cref="CreateManualAsync"/>. Its id is <see cref="Guid.Empty"/>.
    /// </summary>
    public async Task<ReceiptDetailDto> DraftManualAsync(ManualReceiptRequest req, CancellationToken ct)
    {
        var ctx = await db.LoadCompanyContextAsync(req.CompanyId, ct);
        var r = NewManual(req.CompanyId, req.Direction, ctx);
        r.Id = Guid.Empty;
        r.Company = ctx.Company;
        r.Issues = ReceiptValidator.Validate(r, clock.TodayInThailand, ctx.Validation);
        return await BuildDetailAsync(r, ct);
    }

    /// <summary>Validate an unsaved manual document so the UI can show live feedback.</summary>
    public async Task<ValidatePreviewResult> PreviewManualAsync(ManualReceiptRequest req, CancellationToken ct)
    {
        var data = req.Data ?? throw new BadRequestException("ไม่มีข้อมูลเอกสาร (data)");
        var ctx = await db.LoadCompanyContextAsync(req.CompanyId, ct);
        var r = NewManual(req.CompanyId, data.Direction, ctx);
        ApplyUpdate(data, r, ctx);
        return new ValidatePreviewResult(ReceiptValidator.Validate(r, clock.TodayInThailand, ctx.Validation));
    }

    /// <summary>
    /// Store a document typed in by hand. It goes through the same review → approve flow as an uploaded one.
    /// </summary>
    public async Task<ReceiptDetailDto> CreateManualAsync(ManualReceiptRequest req, CancellationToken ct)
    {
        var data = req.Data ?? throw new BadRequestException("ไม่มีข้อมูลเอกสาร (data)");
        var ctx = await db.LoadCompanyContextAsync(req.CompanyId, ct);
        var r = NewManual(req.CompanyId, data.Direction, ctx);
        ApplyUpdate(data, r, ctx);
        await ResolveReferenceAsync(r, ct);
        r.Issues = ReceiptValidator.Validate(r, clock.TodayInThailand, ctx.Validation);
        r.Status = r.Issues.Count == 0 ? ReceiptStatus.Validated : ReceiptStatus.NeedsReview;
        db.Receipts.Add(r);
        await db.SaveChangesAsync(ct);
        return await GetAsync(r.Id, ct);
    }

    private Receipt NewManual(Guid companyId, Direction direction, CompanyContext ctx)
    {
        var today = clock.TodayInThailand;
        var r = new Receipt
        {
            CompanyId = companyId,
            Direction = direction,
            OriginalFileName = "บันทึกเอง",
            DocumentType = DocumentType.FullTaxInvoice,
            IssueDate = today,
            Period = Periods.Of(today),
            Status = ReceiptStatus.NeedsReview,
            ExtractorName = "manual",
            CreatedAt = clock.Now,
            ProcessedAt = clock.Now,
        };
        CompanySide.Apply(r, ctx.Company);
        return r;
    }

    public async Task<PagedResult<ReceiptSummaryDto>> ListAsync(ReceiptListQuery query, CancellationToken ct)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);

        var q = Filter(db.Receipts.AsNoTracking(), query.CompanyId, query.Direction, query.Period);
        if (query.Statuses is { Count: > 0 } statuses) q = q.Where(r => statuses.Contains(r.Status));
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim().ToLower();
            q = q.Where(r => (r.SellerName ?? "").ToLower().Contains(s)
                             || (r.BuyerName ?? "").ToLower().Contains(s)
                             || (r.DocumentNumber ?? "").ToLower().Contains(s)
                             || r.OriginalFileName.ToLower().Contains(s)
                             || (r.SellerTaxId ?? "").Contains(s)
                             || (r.BuyerTaxId ?? "").Contains(s)
                             || (r.Account != null && r.Account.Name.ToLower().Contains(s)));
        }

        var total = await q.CountAsync(ct);
        var rows = await q.Include(r => r.Account).Include(r => r.Payments)
            .OrderByDescending(r => r.IssueDate ?? DateOnly.MaxValue).ThenByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct);

        var adjustments = await AdjustmentsForAsync(rows.Select(r => r.Id).ToList(), ct);
        var items = rows
            .Select(r => r.ToSummary(PaymentMath.Compute(r, adjustments.GetValueOrDefault(r.Id))))
            .ToList();
        return new PagedResult<ReceiptSummaryDto>(items, total, page, pageSize);
    }

    public async Task<IReadOnlyList<StatusCountDto>> CountByStatusAsync(
        Guid companyId, Direction? direction, string? period, CancellationToken ct) =>
        await Filter(db.Receipts.AsNoTracking(), companyId, direction, period)
            .GroupBy(r => r.Status)
            .Select(g => new StatusCountDto(g.Key, g.Count()))
            .ToListAsync(ct);

    public async Task<ReceiptDetailDto> GetAsync(Guid id, CancellationToken ct) =>
        await BuildDetailAsync(await LoadAsync(id, tracking: false, ct), ct);

    public async Task<(Stream Content, string ContentType, string FileName)> OpenFileAsync(Guid id, CancellationToken ct)
    {
        var r = await db.Receipts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new NotFoundException($"Receipt {id} not found");
        if (r.StoredPath is null) throw new NotFoundException("เอกสารนี้บันทึกเอง ไม่มีไฟล์");
        return (storage.OpenRead(r.StoredPath), r.ContentType ?? "application/octet-stream", r.OriginalFileName);
    }

    /// <summary>Save reviewer corrections and re-run validation (does not approve).</summary>
    public async Task<ReceiptDetailDto> UpdateAsync(Guid id, ReceiptUpdateDto dto, CancellationToken ct)
    {
        var r = await LoadAsync(id, tracking: true, ct);
        if (r.Status == ReceiptStatus.Approved)
            throw new ConflictException("เอกสารนี้อนุมัติแล้ว แก้ไขไม่ได้ (ยกเลิกอนุมัติก่อน)");
        if (r.Status is ReceiptStatus.Pending or ReceiptStatus.Processing)
            throw new ConflictException("เอกสารกำลังประมวลผล รอสักครู่");

        var ctx = await db.LoadCompanyContextAsync(r.CompanyId, ct);
        ApplyUpdate(dto, r, ctx);
        await ResolveReferenceAsync(r, ct);
        r.Issues = ReceiptValidator.Validate(r, clock.TodayInThailand, ctx.Validation);
        r.Status = r.Issues.Count == 0 ? ReceiptStatus.Validated : ReceiptStatus.NeedsReview;

        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Validate an unsaved edit so the UI can show live feedback.</summary>
    public async Task<ValidatePreviewResult> PreviewAsync(Guid id, ReceiptUpdateDto dto, CancellationToken ct)
    {
        var current = await db.Receipts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct)
                      ?? throw new NotFoundException($"Receipt {id} not found");
        var ctx = await db.LoadCompanyContextAsync(current.CompanyId, ct);
        var r = new Receipt
        {
            Id = current.Id,
            CompanyId = current.CompanyId,
            Status = current.Status,
            RawExtraction = current.RawExtraction,
            OwnTaxIdOnDocument = current.OwnTaxIdOnDocument,
        };
        ApplyUpdate(dto, r, ctx);
        return new ValidatePreviewResult(ReceiptValidator.Validate(r, clock.TodayInThailand, ctx.Validation));
    }

    public async Task<ReceiptDetailDto> ApproveAsync(Guid id, ApproveRequest req, CancellationToken ct)
    {
        var r = await LoadAsync(id, tracking: true, ct);
        if (r.Status == ReceiptStatus.Approved) return await GetAsync(id, ct);
        if (r.Status is ReceiptStatus.Pending or ReceiptStatus.Processing or ReceiptStatus.Failed)
            throw new ConflictException("อนุมัติได้เฉพาะเอกสารที่อ่านเสร็จแล้ว");

        var ctx = await db.LoadCompanyContextAsync(r.CompanyId, ct);
        if (ctx.IsClosed(r.Period))
            throw new ConflictException($"งวด {r.Period} ปิดแล้ว ย้ายเอกสารไปงวดที่ยังเปิดอยู่ก่อนอนุมัติ");

        r.Issues = ReceiptValidator.Validate(r, clock.TodayInThailand, ctx.Validation);
        if (ReceiptValidator.HasErrors(r.Issues) && !req.Force)
            throw new ConflictException("ยังมีข้อผิดพลาด (Error) อยู่ แก้ไขก่อน หรือส่ง force=true เพื่อยืนยันอนุมัติ");

        r.Status = ReceiptStatus.Approved;
        r.ApprovedAt = clock.Now;
        r.ApprovedBy = string.IsNullOrWhiteSpace(req.ApprovedBy) ? null : req.ApprovedBy.Trim();

        AddPaidOnIssuePayment(r, ctx);
        await LearnVendorAsync(r, ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<ReceiptDetailDto> UnapproveAsync(Guid id, CancellationToken ct)
    {
        var r = await LoadAsync(id, tracking: true, ct);
        if (r.Status != ReceiptStatus.Approved) return await GetAsync(id, ct);

        var ctx = await db.LoadCompanyContextAsync(r.CompanyId, ct);
        if (ctx.IsClosed(r.Period))
            throw new ConflictException($"งวด {r.Period} ปิดแล้ว เปิดงวดก่อนจึงจะยกเลิกอนุมัติได้");
        var auto = r.Payments.Where(p => p.IsAuto).ToList();
        if (auto.Any(p => ctx.IsClosed(Periods.Of(p.Date))))
            throw new ConflictException("การชำระเงินอัตโนมัติของเอกสารนี้อยู่ในงวดที่ปิดแล้ว");

        r.Status = r.Issues.Count == 0 ? ReceiptStatus.Validated : ReceiptStatus.NeedsReview;
        r.ApprovedAt = null;
        r.ApprovedBy = null;
        foreach (var p in auto)
        {
            r.Payments.Remove(p);
            db.Payments.Remove(p);
        }

        // Drop the few-shot example learned from this receipt; it may have been wrong.
        var examples = await db.VendorExamples.Where(e => e.ReceiptId == id).ToListAsync(ct);
        db.VendorExamples.RemoveRange(examples);
        if (r.VendorId is { } vid && await db.Vendors.FindAsync([vid], ct) is { } vendor)
            vendor.ApprovedReceiptCount = Math.Max(0, vendor.ApprovedReceiptCount - 1);

        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task ReprocessAsync(Guid id, CancellationToken ct)
    {
        var r = await LoadAsync(id, tracking: true, ct);
        if (r.Status == ReceiptStatus.Approved)
            throw new ConflictException("เอกสารนี้อนุมัติแล้ว ยกเลิกอนุมัติก่อนจึงจะอ่านใหม่ได้");
        if (r.IsManual) throw new ConflictException("เอกสารนี้บันทึกเอง ไม่มีไฟล์ให้ AI อ่าน");
        r.Status = ReceiptStatus.Pending;
        r.ProcessingError = null;
        await db.SaveChangesAsync(ct);
        await queue.EnqueueAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var r = await LoadAsync(id, tracking: true, ct);
        if (r.Status == ReceiptStatus.Approved)
            throw new ConflictException("ลบเอกสารที่อนุมัติแล้วไม่ได้");
        var ctx = await db.LoadCompanyContextAsync(r.CompanyId, ct);
        if (r.Payments.Any(p => ctx.IsClosed(Periods.Of(p.Date))))
            throw new ConflictException("เอกสารนี้มีการชำระเงินในงวดที่ปิดแล้ว ลบไม่ได้");

        // Notes that pointed at this document lose their link but stay.
        await db.Receipts.Where(n => n.ReferenceReceiptId == id)
            .ExecuteUpdateAsync(u => u.SetProperty(n => n.ReferenceReceiptId, (Guid?)null), ct);
        db.Receipts.Remove(r);
        await db.SaveChangesAsync(ct);
        // The stored file is kept on purpose (audit trail); clean up the uploads folder separately if needed.
    }

    // ---------------- payments ----------------

    public async Task<ReceiptDetailDto> AddPaymentAsync(Guid id, PaymentInput input, CancellationToken ct)
    {
        var r = await LoadAsync(id, tracking: true, ct);
        if (r.IsAdjustmentNote)
            throw new BadRequestException("ใบลดหนี้/ใบเพิ่มหนี้ไม่ต้องบันทึกการชำระ — ยอดจะไปปรับที่ใบกำกับที่อ้างอิง");
        if (input.Amount < 0 || input.WhtAmount < 0) throw new BadRequestException("จำนวนเงินติดลบไม่ได้");
        if (input.Amount + input.WhtAmount <= 0) throw new BadRequestException("จำนวนเงินต้องมากกว่า 0");

        var ctx = await db.LoadCompanyContextAsync(r.CompanyId, ct);
        var period = Periods.Of(input.Date);
        if (ctx.IsClosed(period)) throw new ConflictException($"งวด {period} ปิดแล้ว บันทึกการชำระในงวดนี้ไม่ได้");

        var account = input.AccountId is { } aid
            ? ctx.Chart.ById(aid) ?? throw new BadRequestException("ไม่พบบัญชีที่เลือก")
            : ctx.Chart.ByRole(AccountRole.Bank);
        if (account.Type != AccountType.Asset) throw new BadRequestException("บัญชีรับ/จ่ายเงินต้องเป็นบัญชีสินทรัพย์ (เงินสด/ธนาคาร)");

        r.Payments.Add(new Payment
        {
            ReceiptId = r.Id,
            Date = input.Date,
            Amount = Math.Round(input.Amount, 2, MidpointRounding.AwayFromZero),
            WhtAmount = Math.Round(input.WhtAmount, 2, MidpointRounding.AwayFromZero),
            AccountId = account.Id,
            Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim(),
            CreatedAt = clock.Now,
        });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<ReceiptDetailDto> DeletePaymentAsync(Guid id, Guid paymentId, CancellationToken ct)
    {
        var r = await LoadAsync(id, tracking: true, ct);
        var p = r.Payments.FirstOrDefault(x => x.Id == paymentId) ?? throw new NotFoundException("ไม่พบรายการชำระเงิน");
        var ctx = await db.LoadCompanyContextAsync(r.CompanyId, ct);
        if (ctx.IsClosed(Periods.Of(p.Date)))
            throw new ConflictException($"งวด {Periods.Of(p.Date)} ปิดแล้ว ลบรายการชำระไม่ได้");
        r.Payments.Remove(p);
        db.Payments.Remove(p);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    // ------------------------------------------------------------------

    private static IQueryable<Receipt> Filter(IQueryable<Receipt> q, Guid companyId, Direction? direction, string? period)
    {
        q = q.Where(r => r.CompanyId == companyId);
        if (direction is { } d) q = q.Where(r => r.Direction == d);
        if (Periods.Normalize(period) is { } p) q = q.Where(r => r.Period == p);
        return q;
    }

    private async Task<Receipt> LoadAsync(Guid id, bool tracking, CancellationToken ct)
    {
        var q = db.Receipts
            .Include(r => r.LineItems)
            .Include(r => r.Vendor)
            .Include(r => r.Account)
            .Include(r => r.Company)
            .Include(r => r.Payments).ThenInclude(p => p.Account)
            .AsQueryable();
        if (!tracking) q = q.AsNoTracking();
        return await q.FirstOrDefaultAsync(r => r.Id == id, ct)
               ?? throw new NotFoundException($"Receipt {id} not found");
    }

    /// <summary>Net credit/debit-note adjustment per document id.</summary>
    private async Task<Dictionary<Guid, decimal>> AdjustmentsForAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        var notes = await db.Receipts.AsNoTracking()
            .Where(n => n.ReferenceReceiptId != null && ids.Contains(n.ReferenceReceiptId.Value)
                        && n.Status == ReceiptStatus.Approved)
            .ToListAsync(ct);
        return notes.GroupBy(n => n.ReferenceReceiptId!.Value)
            .ToDictionary(g => g.Key, g => PaymentMath.Adjustments(g));
    }

    private async Task<ReceiptDetailDto> BuildDetailAsync(Receipt r, CancellationToken ct)
    {
        var ctx = await db.LoadCompanyContextAsync(r.CompanyId, ct);

        var reference = r.ReferenceReceiptId is { } refId
            ? await db.Receipts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == refId, ct)
            : null;
        var notes = await db.Receipts.AsNoTracking()
            .Where(n => n.ReferenceReceiptId == r.Id)
            .OrderBy(n => n.IssueDate)
            .ToListAsync(ct);
        var pay = PaymentMath.Compute(r, PaymentMath.Adjustments(notes));
        var examplePinned = await db.VendorExamples.AnyAsync(e => e.ReceiptId == r.Id && e.Pinned, ct);

        // Shown before approval too, as a preview of what approving will post.
        var journal = new List<JournalEntryDto>();
        if (JournalBuilder.ForDocument(r, ctx.Company.VatRegistered, ctx.Chart) is { } doc) journal.Add(doc.ToDto());
        journal.AddRange(r.Payments.OrderBy(p => p.Date).Select(p => JournalBuilder.ForPayment(r, p, ctx.Chart).ToDto()));

        return new ReceiptDetailDto(
            r.Id, r.CompanyId, r.Company?.Name, r.Direction, r.Period, ctx.IsClosed(r.Period),
            r.OriginalFileName, r.IsManual ? null : $"/api/receipts/{r.Id}/file", r.ContentType, r.Status,
            r.ExtractorName, r.ModelName,
            r.ProcessingError, r.Issues, r.DocumentType, r.DocumentNumber, r.IssueDate, r.DueDate,
            r.ReferenceDocumentNumber, reference?.ToReference(), notes.Select(n => n.ToReference()).ToList(),
            r.SellerName, r.SellerTaxId, r.SellerBranch, r.SellerAddress,
            r.BuyerName, r.BuyerTaxId, r.BuyerBranch, r.BuyerAddress, r.OwnTaxIdOnDocument,
            r.Subtotal, r.Discount, r.VatAmount, r.Total, r.VatIncluded, r.Currency, r.PaymentMethod,
            r.AccountId, r.Account?.Name, r.Notes, r.VendorId, r.Vendor?.Name,
            r.LineItems.OrderBy(l => l.LineNo)
                .Select(l => new LineItemDto(l.LineNo, l.Description, l.Quantity, l.UnitPrice, l.Discount, l.Amount))
                .ToList(),
            r.Payments.OrderBy(p => p.Date).Select(p => p.ToDto()).ToList(),
            pay.Paid, pay.Adjustments, pay.Outstanding, pay.Status, journal,
            r.RawExtraction, r.OcrText, r.CreatedAt, r.ProcessedAt, r.ApprovedAt, r.ApprovedBy, examplePinned,
            r.WhtRate, r.WhtAmount, pay.WhtRemaining);
    }

    private static void ApplyUpdate(ReceiptUpdateDto d, Receipt r, CompanyContext ctx)
    {
        r.Direction = d.Direction;
        r.DocumentType = d.DocumentType;
        r.DocumentNumber = Trim(d.DocumentNumber);
        r.IssueDate = d.IssueDate;
        r.DueDate = d.DueDate;
        r.Period = Periods.Normalize(d.Period) ?? (d.IssueDate is { } date ? Periods.Of(date) : null);
        r.ReferenceDocumentNumber = r.IsAdjustmentNote ? Trim(d.ReferenceDocumentNumber) : null;
        r.SellerName = Trim(d.SellerName);
        r.SellerTaxId = ThaiTaxId.Normalize(d.SellerTaxId);
        r.SellerBranch = ThaiBranch.Normalize(d.SellerBranch);
        r.SellerAddress = Trim(d.SellerAddress);
        r.BuyerName = Trim(d.BuyerName);
        r.BuyerTaxId = ThaiTaxId.Normalize(d.BuyerTaxId);
        r.BuyerBranch = ThaiBranch.Normalize(d.BuyerBranch);
        r.BuyerAddress = Trim(d.BuyerAddress);
        CompanySide.Apply(r, ctx.Company);
        r.Subtotal = d.Subtotal;
        r.Discount = d.Discount;
        r.VatAmount = d.VatAmount;
        r.Total = d.Total;
        r.VatIncluded = d.VatIncluded;
        r.Currency = string.IsNullOrWhiteSpace(d.Currency) ? "THB" : d.Currency.Trim().ToUpperInvariant();
        r.PaymentMethod = Trim(d.PaymentMethod);
        // Notes adjust an invoice; its withholding tax stays on the invoice.
        r.WhtAmount = !r.IsAdjustmentNote && d.WhtAmount is > 0 ? Math.Round(d.WhtAmount.Value, 2, MidpointRounding.AwayFromZero) : null;
        r.WhtRate = r.WhtAmount is not null && d.WhtRate is > 0 ? Math.Round(d.WhtRate.Value, 2) : null;
        r.AccountId = ctx.Chart.ById(d.AccountId) is { Active: true } a ? a.Id : null;
        r.Notes = Trim(d.Notes);

        r.LineItems.Clear();
        var n = 1;
        foreach (var li in d.LineItems)
        {
            if (string.IsNullOrWhiteSpace(li.Description) && li.Amount is null) continue;
            r.LineItems.Add(new ReceiptLineItem
            {
                ReceiptId = r.Id,
                LineNo = n++,
                Description = li.Description?.Trim() ?? "",
                Quantity = li.Quantity,
                UnitPrice = li.UnitPrice,
                Discount = li.Discount is null or 0 ? null : li.Discount,
                Amount = li.Amount,
            });
        }
    }

    /// <summary>Link a credit/debit note to the invoice it adjusts, when that invoice is in the system.</summary>
    internal static async Task ResolveReferenceAsync(IAppDbContext db, Receipt r, CancellationToken ct)
    {
        r.ReferenceReceiptId = null;
        if (!r.IsAdjustmentNote || string.IsNullOrWhiteSpace(r.ReferenceDocumentNumber)) return;

        var number = r.ReferenceDocumentNumber.Trim().ToLower();
        var candidates = await db.Receipts.AsNoTracking()
            .Where(x => x.CompanyId == r.CompanyId && x.Direction == r.Direction && x.Id != r.Id
                        && x.DocumentType != DocumentType.CreditNote && x.DocumentType != DocumentType.DebitNote
                        && x.DocumentNumber != null && x.DocumentNumber.ToLower() == number)
            .OrderByDescending(x => x.IssueDate)
            .ToListAsync(ct);
        var counterparty = r.CounterpartyTaxId;
        r.ReferenceReceiptId = (candidates.FirstOrDefault(x => counterparty is not null && x.CounterpartyTaxId == counterparty)
                                ?? candidates.FirstOrDefault(x => counterparty is null || x.CounterpartyTaxId is null))?.Id;
    }

    private Task ResolveReferenceAsync(Receipt r, CancellationToken ct) => ResolveReferenceAsync(db, r, ct);

    /// <summary>A receipt proves payment at the issue date, so approving one records that payment automatically.</summary>
    private void AddPaidOnIssuePayment(Receipt r, CompanyContext ctx)
    {
        if (!VatRules.IsPaidOnIssue(r.DocumentType) || r.Payments.Count > 0) return;
        if (r.Total is not > 0 || r.IssueDate is not { } issued) return;

        // If the issue month is already closed the document was moved to a later period; pay it there.
        var date = !ctx.IsClosed(Periods.Of(issued)) ? issued
            : Periods.TryParse(r.Period, out var start) ? start : issued;
        if (ctx.IsClosed(Periods.Of(date))) return;

        r.Payments.Add(new Payment
        {
            ReceiptId = r.Id,
            Date = date,
            // Paid on the spot, less any withholding tax kept back.
            Amount = r.Total.Value - (r.WhtAmount ?? 0m),
            WhtAmount = r.WhtAmount ?? 0m,
            AccountId = ctx.Chart.ByRole(PaymentMath.AccountRoleFor(r.PaymentMethod)).Id,
            Note = "ชำระแล้วตามใบเสร็จ",
            IsAuto = true,
            CreatedAt = clock.Now,
        });
    }

    /// <summary>Upsert the document issuer and keep this document as a few-shot example for the next one.</summary>
    private async Task LearnVendorAsync(Receipt r, CancellationToken ct)
    {
        // The issuer is the seller — the counterparty on purchases, our own company on sales.
        if (!ThaiTaxId.IsValid(r.SellerTaxId)) return;

        var vendor = await db.Vendors.Include(v => v.Examples)
            .FirstOrDefaultAsync(v => v.TaxId == r.SellerTaxId, ct);
        if (vendor is null)
        {
            vendor = new Vendor { TaxId = r.SellerTaxId!, Name = r.SellerName ?? r.SellerTaxId!, CreatedAt = clock.Now };
            db.Vendors.Add(vendor);
        }

        if (!string.IsNullOrWhiteSpace(r.SellerName)) vendor.Name = r.SellerName;
        vendor.Branch = r.SellerBranch ?? vendor.Branch;
        vendor.ApprovedReceiptCount++;
        vendor.UpdatedAt = clock.Now;
        r.VendorId = vendor.Id;

        vendor.Examples.Add(new VendorExample
        {
            ReceiptId = r.Id,
            ApprovedJson = ReceiptMapper.ToExtracted(r).ToJson(),
            CreatedAt = clock.Now,
        });

        TrimExamples(db, vendor, options.Value.MaxVendorExamples);
    }

    /// <summary>Keep every pinned example plus the newest <paramref name="max"/> unpinned ones.</summary>
    internal static void TrimExamples(IAppDbContext db, Vendor vendor, int max)
    {
        var stale = vendor.Examples.Where(e => !e.Pinned)
            .OrderByDescending(e => e.CreatedAt)
            .Skip(Math.Max(1, max))
            .ToList();
        foreach (var e in stale)
        {
            vendor.Examples.Remove(e);
            if (e.Id != Guid.Empty) db.VendorExamples.Remove(e);
        }
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
