using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReceiptReader.Application.Abstractions;
using ReceiptReader.Application.Accounting;
using ReceiptReader.Application.Companies;
using ReceiptReader.Application.Extraction;
using ReceiptReader.Application.Validation;
using ReceiptReader.Domain.Entities;
using ReceiptReader.Domain.Enums;

namespace ReceiptReader.Application.Receipts;

/// <summary>
/// Runs one receipt through: preprocess → extract → normalize → our side from the company profile
/// → (vendor-hint retry) → validate → save.
/// Called by the background worker, one receipt at a time (a single local GPU serves one request well).
/// </summary>
public sealed class ReceiptProcessingService(
    IAppDbContext db,
    IFileStorage storage,
    IImagePreprocessor preprocessor,
    IReceiptExtractor extractor,
    IClock clock,
    IOptions<ProcessingOptions> options,
    ILogger<ReceiptProcessingService> logger)
{
    private readonly ProcessingOptions _opt = options.Value;

    public async Task ProcessAsync(Guid receiptId, CancellationToken ct)
    {
        var receipt = await db.Receipts.Include(r => r.LineItems)
            .FirstOrDefaultAsync(r => r.Id == receiptId, ct);
        if (receipt is null || receipt.Status == ReceiptStatus.Approved || receipt.StoredPath is null) return;

        receipt.Status = ReceiptStatus.Processing;
        receipt.ProcessingAttempts++;
        await db.SaveChangesAsync(ct);

        try
        {
            IReadOnlyList<byte[]> pages;
            await using (var file = storage.OpenRead(receipt.StoredPath))
                pages = await preprocessor.PrepareAsync(file, ct);

            var today = clock.TodayInThailand;
            var company = await db.LoadCompanyContextAsync(receipt.CompanyId, ct);
            var context = new ExtractionContext(receipt.Direction == Direction.Purchase, company.Company.Name,
                string.IsNullOrWhiteSpace(company.Company.TaxId) ? null : company.Company.TaxId);

            // Pass 1: no hints.
            var best = await ExtractAndScoreAsync(receipt, pages, [], context, company, today, ct);
            logger.LogInformation("Receipt {Id} pass 1: {Count} issue(s)", receiptId, best.Issues.Count);

            // Pass 2: if we know the issuer and pass 1 is imperfect, retry with their approved examples.
            // The issuer is the seller: the counterparty on purchases, our own company on sales.
            var vendor = await FindVendorAsync(best.Preview.SellerTaxId, ct);
            if (vendor is not null && _opt.RetryWithVendorExamples && best.Issues.Count > 0)
            {
                var examples = vendor.Examples
                    .OrderByDescending(e => e.Pinned)
                    .ThenByDescending(e => e.CreatedAt)
                    .Take(_opt.ExamplesInPrompt)
                    .Select(e => e.ApprovedJson)
                    .ToList();

                if (examples.Count > 0)
                {
                    var second = await ExtractAndScoreAsync(receipt, pages, examples, context, company, today, ct);
                    logger.LogInformation("Receipt {Id} pass 2 (vendor {Vendor}): {Count} issue(s)",
                        receiptId, vendor.Name, second.Issues.Count);
                    if (ReceiptValidator.Score(second.Issues) < ReceiptValidator.Score(best.Issues))
                        best = second;
                }
            }

            // Commit the winning attempt to the tracked entity.
            ReceiptMapper.Apply(best.Result.Data, receipt);
            CompanySide.CaptureAndApply(receipt, company.Company);
            receipt.Period = receipt.IssueDate is { } issued ? Periods.Of(issued) : null;
            vendor ??= await FindVendorAsync(receipt.SellerTaxId, ct);
            receipt.VendorId = vendor?.Id;
            receipt.AccountId ??= await LastAccountForAsync(receipt, ct);
            await ReceiptService.ResolveReferenceAsync(db, receipt, ct);

            receipt.Issues = ReceiptValidator.Validate(receipt, today, company.Validation);
            receipt.Status = receipt.Issues.Count == 0 ? ReceiptStatus.Validated : ReceiptStatus.NeedsReview;
            receipt.ExtractorName = extractor.Name;
            receipt.ModelName = best.Result.ModelName;
            receipt.RawExtraction = best.Result.RawJson;
            receipt.OcrText = best.Result.OcrText;
            receipt.ProcessingError = null;
            receipt.ProcessedAt = clock.Now;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Receipt {Id} failed", receiptId);
            receipt.Status = ReceiptStatus.Failed;
            receipt.ProcessingError = ex.Message;
            receipt.ProcessedAt = clock.Now;
        }

        try
        {
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (DbUpdateException ex)
        {
            // The tracked entity can't be saved, so write the failure past the change tracker;
            // otherwise the receipt would sit in "Processing" forever.
            logger.LogError(ex, "Receipt {Id}: saving the extraction failed", receiptId);
            var message = "บันทึกผลการอ่านไม่สำเร็จ: " + (ex.InnerException ?? ex).Message;
            var now = clock.Now;
            await db.Receipts.Where(r => r.Id == receiptId).ExecuteUpdateAsync(u => u
                .SetProperty(r => r.Status, ReceiptStatus.Failed)
                .SetProperty(r => r.ProcessingError, message)
                .SetProperty(r => r.ProcessedAt, now), CancellationToken.None);
        }
    }

    private sealed record Attempt(ExtractionResult Result, Receipt Preview, List<ValidationIssue> Issues);

    private async Task<Attempt> ExtractAndScoreAsync(
        Receipt receipt, IReadOnlyList<byte[]> pages, IReadOnlyList<string> examples, ExtractionContext context,
        CompanyContext company, DateOnly today, CancellationToken ct)
    {
        var result = await extractor.ExtractAsync(new ExtractionRequest(pages, examples, context), ct);
        // Score on a detached copy so a losing attempt never touches the tracked entity.
        var preview = new Receipt { CompanyId = receipt.CompanyId, Direction = receipt.Direction, RawExtraction = result.RawJson };
        ReceiptMapper.Apply(result.Data, preview);
        CompanySide.CaptureAndApply(preview, company.Company);
        preview.Period = preview.IssueDate is { } issued ? Periods.Of(issued) : null;
        return new Attempt(result, preview, ReceiptValidator.Validate(preview, today, company.Validation));
    }

    /// <summary>The account the last approved document from the same counterparty was booked to.</summary>
    private async Task<Guid?> LastAccountForAsync(Receipt r, CancellationToken ct)
    {
        var taxId = r.CounterpartyTaxId;
        if (!ThaiTaxId.IsValid(taxId)) return null;
        var q = db.Receipts.AsNoTracking()
            .Where(x => x.CompanyId == r.CompanyId && x.Direction == r.Direction && x.Id != r.Id
                        && x.Status == ReceiptStatus.Approved && x.AccountId != null);
        q = r.Direction == Direction.Purchase ? q.Where(x => x.SellerTaxId == taxId) : q.Where(x => x.BuyerTaxId == taxId);
        return await q.OrderByDescending(x => x.ApprovedAt).Select(x => x.AccountId).FirstOrDefaultAsync(ct);
    }

    private async Task<Vendor?> FindVendorAsync(string? taxId, CancellationToken ct)
    {
        if (!ThaiTaxId.IsValid(taxId)) return null;
        return await db.Vendors.Include(v => v.Examples)
            .FirstOrDefaultAsync(v => v.TaxId == taxId, ct);
    }
}
