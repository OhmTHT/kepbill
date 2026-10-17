namespace ReceiptReader.Application.Extraction;

/// <param name="Pages">Preprocessed JPEG per page (a photo is one page; a PDF may be several).</param>
/// <param name="VendorExamples">Approved JSON from earlier receipts of the same seller (may be empty).</param>
/// <param name="Context">Which company the document belongs to and on which side (null = unknown).</param>
public sealed record ExtractionRequest(
    IReadOnlyList<byte[]> Pages,
    IReadOnlyList<string> VendorExamples,
    ExtractionContext? Context = null);

/// <param name="IsPurchase">True when our company is the buyer; false when we are the seller.</param>
public sealed record ExtractionContext(bool IsPurchase, string CompanyName, string? CompanyTaxId);

public sealed record ExtractionResult(ExtractedReceipt Data, string RawJson, string ModelName, string? OcrText);

/// <summary>
/// Strategy for turning a receipt image into <see cref="ExtractedReceipt"/>.
/// Implementations: vision-LLM (image → JSON) and OCR → text-LLM. Pick with Extraction:Mode.
/// </summary>
public interface IReceiptExtractor
{
    string Name { get; }
    Task<ExtractionResult> ExtractAsync(ExtractionRequest request, CancellationToken ct);
}
