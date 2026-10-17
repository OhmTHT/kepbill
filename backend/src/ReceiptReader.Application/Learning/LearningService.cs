using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ReceiptReader.Application.Abstractions;
using ReceiptReader.Application.Extraction;
using ReceiptReader.Application.Receipts;
using ReceiptReader.Application.Validation;
using ReceiptReader.Domain.Entities;
using ReceiptReader.Domain.Enums;

namespace ReceiptReader.Application.Learning;

public sealed record FieldAccuracyDto(string Field, string Label, int Correct, int Total, double Rate);

public sealed record ModelAccuracyDto(string Model, int Documents, int Perfect, double FieldRate);

public sealed record PeriodAccuracyDto(string Period, int Documents, double FieldRate);

public sealed record CorrectedDocumentDto(
    Guid Id, string OriginalFileName, string? CounterpartyName, string? Model, IReadOnlyList<string> WrongFields,
    DateTimeOffset? ApprovedAt);

public sealed record AccuracyDto(
    int Documents,
    int Perfect,
    double FieldRate,
    IReadOnlyList<FieldAccuracyDto> Fields,
    IReadOnlyList<ModelAccuracyDto> Models,
    IReadOnlyList<PeriodAccuracyDto> Trend,
    IReadOnlyList<CorrectedDocumentDto> MostCorrected);

public sealed record ExampleDto(
    Guid Id, Guid ReceiptId, bool Pinned, DateTimeOffset CreatedAt, string? DocumentNumber, DateOnly? IssueDate,
    decimal? Total);

public sealed record IssuerDto(
    Guid Id, string TaxId, string Name, string? Branch, int ApprovedReceiptCount, DateTimeOffset UpdatedAt,
    IReadOnlyList<ExampleDto> Examples);

/// <summary>
/// The "train AI" page: how often the model is right (model output vs. what a person approved), the few-shot
/// examples it learns from, and a dataset export for a future fine-tune.
/// </summary>
public sealed class LearningService(IAppDbContext db, IClock clock, IOptions<ProcessingOptions> options)
{
    private static readonly (string Field, string Label)[] Fields =
    [
        ("documentType", "ประเภทเอกสาร"),
        ("documentNumber", "เลขที่เอกสาร"),
        ("issueDate", "วันที่"),
        ("counterpartyName", "ชื่อคู่ค้า"),
        ("counterpartyTaxId", "เลขผู้เสียภาษีคู่ค้า"),
        ("counterpartyBranch", "สาขาคู่ค้า"),
        ("subtotal", "ยอดก่อนภาษี"),
        ("discount", "ส่วนลด"),
        ("vatAmount", "VAT"),
        ("total", "ยอดรวม"),
        ("lineItems", "รายการสินค้า"),
    ];

    // ---------------- accuracy ----------------

    public async Task<AccuracyDto> AccuracyAsync(Guid? companyId, CancellationToken ct)
    {
        var q = db.Receipts.AsNoTracking().Include(r => r.LineItems)
            .Where(r => r.Status == ReceiptStatus.Approved && r.RawExtraction != null);
        if (companyId is { } cid) q = q.Where(r => r.CompanyId == cid);
        var docs = await q.ToListAsync(ct);

        var compared = docs
            .Select(d => (Doc: d, Wrong: Compare(d)))
            .Where(x => x.Wrong is not null)
            .Select(x => (x.Doc, Wrong: x.Wrong!))
            .ToList();

        double Rate(IEnumerable<(Receipt Doc, List<string> Wrong)> set)
        {
            var list = set.ToList();
            var total = list.Count * Fields.Length;
            return total == 0 ? 0 : 1.0 - (double)list.Sum(x => x.Wrong.Count) / total;
        }

        var fields = Fields.Select(f =>
        {
            var correct = compared.Count(x => !x.Wrong.Contains(f.Field));
            return new FieldAccuracyDto(f.Field, f.Label, correct, compared.Count,
                compared.Count == 0 ? 0 : (double)correct / compared.Count);
        }).ToList();

        var models = compared.GroupBy(x => x.Doc.ModelName ?? "(ไม่ทราบ)")
            .Select(g => new ModelAccuracyDto(g.Key, g.Count(), g.Count(x => x.Wrong.Count == 0), Rate(g)))
            .OrderByDescending(m => m.Documents)
            .ToList();

        var trend = compared.Where(x => x.Doc.ApprovedAt is not null)
            .GroupBy(x => Accounting.Periods.Of(DateOnly.FromDateTime(x.Doc.ApprovedAt!.Value.UtcDateTime)))
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new PeriodAccuracyDto(g.Key, g.Count(), Rate(g)))
            .ToList();

        var labels = Fields.ToDictionary(f => f.Field, f => f.Label);
        var worst = compared.Where(x => x.Wrong.Count > 0)
            .OrderByDescending(x => x.Wrong.Count).ThenByDescending(x => x.Doc.ApprovedAt)
            .Take(20)
            .Select(x => new CorrectedDocumentDto(x.Doc.Id, x.Doc.OriginalFileName, x.Doc.CounterpartyName,
                x.Doc.ModelName, x.Wrong.Select(w => labels[w]).ToList(), x.Doc.ApprovedAt))
            .ToList();

        return new AccuracyDto(compared.Count, compared.Count(x => x.Wrong.Count == 0), Rate(compared),
            fields, models, trend, worst);
    }

    /// <summary>
    /// Fields where the model's answer differs from the approved document. Only the counterparty is compared:
    /// our side comes from the company profile, not from the model.
    /// </summary>
    internal static List<string>? Compare(Receipt approved)
    {
        ExtractedReceipt raw;
        try { raw = ExtractedReceipt.Parse(approved.RawExtraction!); }
        catch (JsonException) { return null; }

        var ai = new Receipt { Direction = approved.Direction };
        ReceiptMapper.Apply(raw, ai);

        var wrong = new List<string>();
        void Check(string field, bool same) { if (!same) wrong.Add(field); }

        var purchase = approved.Direction == Direction.Purchase;
        Check("documentType", ai.DocumentType == approved.DocumentType);
        Check("documentNumber", SameText(ai.DocumentNumber, approved.DocumentNumber));
        Check("issueDate", ai.IssueDate == approved.IssueDate);
        Check("counterpartyName", SameText(ai.CounterpartyName, approved.CounterpartyName));
        Check("counterpartyTaxId", ai.CounterpartyTaxId == approved.CounterpartyTaxId);
        Check("counterpartyBranch",
            (purchase ? ai.SellerBranch : ai.BuyerBranch) == (purchase ? approved.SellerBranch : approved.BuyerBranch));
        Check("subtotal", ai.Subtotal == approved.Subtotal);
        Check("discount", (ai.Discount ?? 0) == (approved.Discount ?? 0));
        Check("vatAmount", (ai.VatAmount ?? 0) == (approved.VatAmount ?? 0));
        Check("total", ai.Total == approved.Total);
        Check("lineItems", ai.LineItems.Count == approved.LineItems.Count
                           && ai.LineItems.Zip(approved.LineItems.OrderBy(l => l.LineNo))
                               .All(p => p.First.Amount == p.Second.Amount));
        return wrong;
    }

    private static bool SameText(string? a, string? b) =>
        string.Equals(Squash(a), Squash(b), StringComparison.OrdinalIgnoreCase);

    private static string Squash(string? s) => string.IsNullOrWhiteSpace(s) ? "" : string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    // ---------------- few-shot examples ----------------

    public async Task<IReadOnlyList<IssuerDto>> IssuersAsync(CancellationToken ct)
    {
        var vendors = await db.Vendors.AsNoTracking().Include(v => v.Examples)
            .OrderByDescending(v => v.UpdatedAt)
            .ToListAsync(ct);
        var receiptIds = vendors.SelectMany(v => v.Examples.Select(e => e.ReceiptId)).ToList();
        var receipts = await db.Receipts.AsNoTracking()
            .Where(r => receiptIds.Contains(r.Id))
            .Select(r => new { r.Id, r.DocumentNumber, r.IssueDate, r.Total })
            .ToDictionaryAsync(r => r.Id, ct);

        return vendors.Select(v => new IssuerDto(v.Id, v.TaxId, v.Name, v.Branch, v.ApprovedReceiptCount, v.UpdatedAt,
                v.Examples.OrderByDescending(e => e.Pinned).ThenByDescending(e => e.CreatedAt)
                    .Select(e =>
                    {
                        var r = receipts.GetValueOrDefault(e.ReceiptId);
                        return new ExampleDto(e.Id, e.ReceiptId, e.Pinned, e.CreatedAt, r?.DocumentNumber, r?.IssueDate, r?.Total);
                    })
                    .ToList()))
            .ToList();
    }

    public async Task<string> ExampleJsonAsync(Guid exampleId, CancellationToken ct) =>
        await db.VendorExamples.AsNoTracking().Where(e => e.Id == exampleId).Select(e => e.ApprovedJson)
            .FirstOrDefaultAsync(ct) ?? throw new NotFoundException("ไม่พบตัวอย่าง");

    public async Task SetPinnedAsync(Guid exampleId, bool pinned, CancellationToken ct)
    {
        var e = await db.VendorExamples.FirstOrDefaultAsync(x => x.Id == exampleId, ct) ?? throw new NotFoundException("ไม่พบตัวอย่าง");
        e.Pinned = pinned;
        if (!pinned)
        {
            var vendor = await db.Vendors.Include(v => v.Examples).FirstAsync(v => v.Id == e.VendorId, ct);
            ReceiptService.TrimExamples(db, vendor, options.Value.MaxVendorExamples);
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteExampleAsync(Guid exampleId, CancellationToken ct)
    {
        await db.VendorExamples.Where(e => e.Id == exampleId).ExecuteDeleteAsync(ct);
    }

    /// <summary>Stop pinning a document's example; it stays as an ordinary one until newer approvals rotate it out.</summary>
    public async Task UnpinReceiptAsync(Guid receiptId, CancellationToken ct)
    {
        var e = await db.VendorExamples.FirstOrDefaultAsync(x => x.ReceiptId == receiptId && x.Pinned, ct);
        if (e is not null) await SetPinnedAsync(e.Id, false, ct);
    }

    /// <summary>Use an approved document as a pinned example — e.g. a clean scan of a tricky layout.</summary>
    public async Task UseAsExampleAsync(Guid receiptId, CancellationToken ct)
    {
        var r = await db.Receipts.AsNoTracking().Include(x => x.LineItems).FirstOrDefaultAsync(x => x.Id == receiptId, ct)
                ?? throw new NotFoundException("ไม่พบเอกสาร");
        if (r.Status != ReceiptStatus.Approved) throw new ConflictException("ใช้เป็นตัวอย่างได้เฉพาะเอกสารที่อนุมัติแล้ว");
        if (!ThaiTaxId.IsValid(r.SellerTaxId)) throw new ConflictException("เอกสารต้องมีเลขผู้เสียภาษีผู้ขายที่ถูกต้อง");

        var vendor = await db.Vendors.Include(v => v.Examples).FirstOrDefaultAsync(v => v.TaxId == r.SellerTaxId, ct);
        if (vendor is null)
        {
            vendor = new Vendor { TaxId = r.SellerTaxId!, Name = r.SellerName ?? r.SellerTaxId!, CreatedAt = clock.Now, UpdatedAt = clock.Now };
            db.Vendors.Add(vendor);
        }

        if (vendor.Examples.FirstOrDefault(e => e.ReceiptId == r.Id) is { } existing)
            existing.Pinned = true;
        else
            vendor.Examples.Add(new VendorExample
            {
                ReceiptId = r.Id,
                ApprovedJson = ReceiptMapper.ToExtracted(r).ToJson(),
                Pinned = true,
                CreatedAt = clock.Now,
            });
        await db.SaveChangesAsync(ct);
    }

    // ---------------- dataset ----------------

    /// <summary>
    /// A zip with the original files and train.jsonl in chat format (system / user+image / assistant = approved JSON),
    /// ready for a vision fine-tune (e.g. Unsloth / LLaMA-Factory). <paramref name="output"/> must be seekable or
    /// allow synchronous writes (a temp file, not the HTTP response).
    /// </summary>
    public async Task WriteDatasetAsync(Guid? companyId, Stream output, IFileStorage storage, CancellationToken ct)
    {
        var q = db.Receipts.AsNoTracking().Include(r => r.LineItems).Include(r => r.Company)
            .Where(r => r.Status == ReceiptStatus.Approved && r.StoredPath != null); // manual entries have no image
        if (companyId is { } cid) q = q.Where(r => r.CompanyId == cid);
        var docs = await q.OrderBy(r => r.ApprovedAt).ToListAsync(ct);

        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        var jsonl = new StringBuilder();
        foreach (var r in docs)
        {
            var image = $"images/{r.Id}{Path.GetExtension(r.StoredPath)}";
            try
            {
                await using var src = storage.OpenRead(r.StoredPath!);
                var entry = zip.CreateEntry(image, CompressionLevel.NoCompression);
                await using var dst = entry.Open();
                await src.CopyToAsync(dst, ct);
            }
            catch (FileNotFoundException)
            {
                continue; // file removed from storage; skip the sample
            }

            var context = new ExtractionContext(r.Direction == Direction.Purchase, r.Company?.Name ?? "",
                string.IsNullOrWhiteSpace(r.Company?.TaxId) ? null : r.Company.TaxId);
            var row = new
            {
                id = r.Id,
                image,
                direction = r.Direction.ToString(),
                messages = new object[]
                {
                    new { role = "system", content = ReceiptPrompt.SystemPrompt + ReceiptPrompt.ContextHint(context) },
                    new { role = "user", content = ReceiptPrompt.UserInstructionForImage },
                    new { role = "assistant", content = ReceiptMapper.ToExtracted(r).ToJson() },
                },
            };
            jsonl.AppendLine(JsonSerializer.Serialize(row, ExtractedReceipt.JsonOptions));
        }

        var train = zip.CreateEntry("train.jsonl");
        await using (var w = new StreamWriter(train.Open(), new UTF8Encoding(false)))
            await w.WriteAsync(jsonl.ToString());

        var readme = zip.CreateEntry("README.txt");
        await using (var w = new StreamWriter(readme.Open(), new UTF8Encoding(false)))
            await w.WriteAsync($"""
                Receipt Reader dataset — {docs.Count} approved documents, exported {clock.Now:yyyy-MM-dd HH:mm}.
                train.jsonl: one sample per line; "image" is the file in images/, "messages" is the chat to learn
                (the assistant turn is the human-approved JSON). Feed it to a vision fine-tuning tool such as
                Unsloth or LLaMA-Factory, export the result to GGUF and `ollama create` a new model, then set
                Extraction:VisionModel to it.
                """);
    }
}
