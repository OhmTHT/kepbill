namespace ReceiptReader.Application.Receipts;

public sealed class ProcessingOptions
{
    public const string Section = "Processing";

    /// <summary>How many approved receipts per vendor to keep as few-shot examples.</summary>
    public int MaxVendorExamples { get; set; } = 3;

    /// <summary>How many of those examples to put into the prompt on a retry (more = slower, bigger context).</summary>
    public int ExamplesInPrompt { get; set; } = 2;

    /// <summary>Retry with vendor examples when the first pass has any validation issue.</summary>
    public bool RetryWithVendorExamples { get; set; } = true;

    public long MaxUploadBytes { get; set; } = 20 * 1024 * 1024;
}
