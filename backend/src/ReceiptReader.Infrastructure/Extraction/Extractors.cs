using Microsoft.Extensions.Options;
using ReceiptReader.Application.Extraction;

namespace ReceiptReader.Infrastructure.Extraction;

/// <summary>Mode "Vision": one call, image in, JSON out.</summary>
public sealed class VisionLlmExtractor(OllamaClient ollama, IOptions<ExtractionOptions> options) : IReceiptExtractor
{
    public string Name => "vision-llm";

    public async Task<ExtractionResult> ExtractAsync(ExtractionRequest request, CancellationToken ct)
    {
        var model = options.Value.VisionModel;
        var messages = new List<OllamaClient.Message>
        {
            new("system", ReceiptPrompt.SystemPrompt + ReceiptPrompt.ContextHint(request.Context) +
                ReceiptPrompt.VendorHint(request.VendorExamples)),
            new("user", ReceiptPrompt.UserInstructionForImage, request.Pages.Select(Convert.ToBase64String).ToList()),
        };

        var json = await ollama.ChatAsync(model, messages, ReceiptPrompt.JsonSchema(), ct);
        return new ExtractionResult(ExtractedReceipt.Parse(json), json, model, OcrText: null);
    }
}

/// <summary>
/// Mode "OcrThenLlm": a dedicated OCR model transcribes the image, then a text model structures it.
/// Two smaller specialised steps are often more accurate on Thai receipts than one generalist call,
/// and the OCR text is stored so reviewers can see what the machine actually "saw".
/// </summary>
public sealed class OcrThenLlmExtractor(OllamaClient ollama, IOptions<ExtractionOptions> options) : IReceiptExtractor
{
    public string Name => "ocr+llm";

    public async Task<ExtractionResult> ExtractAsync(ExtractionRequest request, CancellationToken ct)
    {
        var opt = options.Value;

        var ocrText = await ollama.ChatAsync(opt.OcrModel,
            [new("user", ReceiptPrompt.OcrPrompt, request.Pages.Select(Convert.ToBase64String).ToList())],
            format: null, ct);

        var messages = new List<OllamaClient.Message>
        {
            new("system", ReceiptPrompt.SystemPrompt + ReceiptPrompt.ContextHint(request.Context) +
                ReceiptPrompt.VendorHint(request.VendorExamples)),
            new("user", ReceiptPrompt.UserInstructionForOcrText(ocrText)),
        };
        var json = await ollama.ChatAsync(opt.TextModel, messages, ReceiptPrompt.JsonSchema(), ct);

        return new ExtractionResult(ExtractedReceipt.Parse(json), json, $"{opt.OcrModel} → {opt.TextModel}", ocrText);
    }
}
