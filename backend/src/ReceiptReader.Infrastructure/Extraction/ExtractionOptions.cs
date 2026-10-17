namespace ReceiptReader.Infrastructure.Extraction;

public enum ExtractionMode
{
    /// <summary>Image → vision LLM → JSON in one call.</summary>
    Vision,
    /// <summary>Image → OCR model (text) → text LLM → JSON. Often better on small/faded Thai print.</summary>
    OcrThenLlm,
}

public sealed class ExtractionOptions
{
    public const string Section = "Extraction";

    public ExtractionMode Mode { get; set; } = ExtractionMode.Vision;

    public string OllamaBaseUrl { get; set; } = "http://localhost:11434";

    /// <summary>Vision model used in <see cref="ExtractionMode.Vision"/>.</summary>
    public string VisionModel { get; set; } = "qwen2.5vl:7b";

    /// <summary>Image→text model used in <see cref="ExtractionMode.OcrThenLlm"/> (e.g. a Typhoon OCR build, or any vision model).</summary>
    public string OcrModel { get; set; } = "qwen2.5vl:7b";

    /// <summary>Text model that turns OCR text into JSON in <see cref="ExtractionMode.OcrThenLlm"/>.</summary>
    public string TextModel { get; set; } = "qwen2.5:7b";

    /// <summary>
    /// Model for the product code suggestions (text in, JSON out). Null uses <see cref="VisionModel"/>: it reads Thai
    /// well and is usually loaded already, so no second model has to be swapped into VRAM.
    /// </summary>
    public string? AssistModel { get; set; }

    /// <summary>Context window passed to Ollama. Receipts + few-shot examples need more than the default.</summary>
    public int NumCtx { get; set; } = 8192;

    public double Temperature { get; set; } = 0;

    /// <summary>How long Ollama keeps the model in VRAM after a request.</summary>
    public string KeepAlive { get; set; } = "15m";

    public int TimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// For "thinking" models (e.g. qwen3.x): false skips the reasoning pass — much faster and leaves the context
    /// for the answer. Null sends nothing (model default); leave it null for models without a thinking mode.
    /// </summary>
    public bool? Think { get; set; }

    public int MaxImageEdge { get; set; } = 1600;

    /// <summary>0 = off. 0.15 = +15% contrast, helps faded thermal paper.</summary>
    public float ContrastBoost { get; set; } = 0.1f;

    /// <summary>PDF pages beyond this are ignored (each page is one more image for the model).</summary>
    public int MaxPdfPages { get; set; } = 3;

    /// <summary>Render resolution for PDF pages before the <see cref="MaxImageEdge"/> cap.</summary>
    public int PdfDpi { get; set; } = 200;
}
