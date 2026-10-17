using System.Text.Json;
using System.Text.Json.Serialization;

namespace ReceiptReader.Application.Extraction;

/// <summary>
/// The exact JSON contract the model must return. Field names are snake_case because that is what
/// models follow most reliably, and the same shape is used for vendor few-shot examples.
/// Keep this in sync with <see cref="ReceiptPrompt.JsonSchema"/>.
/// </summary>
public sealed class ExtractedReceipt
{
    [JsonPropertyName("document_type")] public string? DocumentType { get; set; }
    [JsonPropertyName("document_number")] public string? DocumentNumber { get; set; }
    /// <summary>Date exactly as printed, e.g. "13/10/69" or "13 ต.ค. 2569".</summary>
    [JsonPropertyName("issue_date_raw")] public string? IssueDateRaw { get; set; }
    /// <summary>Model's attempt at YYYY-MM-DD. May still be in พ.ศ. — normalized later.</summary>
    [JsonPropertyName("issue_date")] public string? IssueDate { get; set; }
    /// <summary>Payment due date (ครบกำหนดชำระ) as YYYY-MM-DD, normalized like issue_date.</summary>
    [JsonPropertyName("due_date")] public string? DueDate { get; set; }
    /// <summary>Credit/debit notes: number of the original tax invoice being adjusted.</summary>
    [JsonPropertyName("reference_document_number")] public string? ReferenceDocumentNumber { get; set; }

    [JsonPropertyName("seller_name")] public string? SellerName { get; set; }
    [JsonPropertyName("seller_tax_id")] public string? SellerTaxId { get; set; }
    [JsonPropertyName("seller_branch")] public string? SellerBranch { get; set; }
    [JsonPropertyName("seller_address")] public string? SellerAddress { get; set; }

    [JsonPropertyName("buyer_name")] public string? BuyerName { get; set; }
    [JsonPropertyName("buyer_tax_id")] public string? BuyerTaxId { get; set; }
    [JsonPropertyName("buyer_branch")] public string? BuyerBranch { get; set; }
    [JsonPropertyName("buyer_address")] public string? BuyerAddress { get; set; }

    [JsonPropertyName("items")] public List<ExtractedLineItem> Items { get; set; } = [];

    [JsonPropertyName("subtotal")] public decimal? Subtotal { get; set; }
    [JsonPropertyName("discount")] public decimal? Discount { get; set; }
    [JsonPropertyName("vat_amount")] public decimal? VatAmount { get; set; }
    [JsonPropertyName("vat_included")] public bool? VatIncluded { get; set; }
    [JsonPropertyName("total")] public decimal? Total { get; set; }
    [JsonPropertyName("currency")] public string? Currency { get; set; }
    [JsonPropertyName("payment_method")] public string? PaymentMethod { get; set; }

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keep Thai readable
    };

    public static ExtractedReceipt Parse(string json)
    {
        var cleaned = StripCodeFence(json);
        return JsonSerializer.Deserialize<ExtractedReceipt>(cleaned, JsonOptions)
               ?? throw new JsonException("Model returned an empty JSON document.");
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Some models wrap JSON in ```json fences even when asked not to.</summary>
    internal static string StripCodeFence(string text)
    {
        var t = text.Trim();
        if (!t.StartsWith("```")) return t;
        var firstNewLine = t.IndexOf('\n');
        var lastFence = t.LastIndexOf("```", StringComparison.Ordinal);
        return firstNewLine >= 0 && lastFence > firstNewLine
            ? t[(firstNewLine + 1)..lastFence].Trim()
            : t.Trim('`');
    }
}

public sealed class ExtractedLineItem
{
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("quantity")] public decimal? Quantity { get; set; }
    [JsonPropertyName("unit_price")] public decimal? UnitPrice { get; set; }
    [JsonPropertyName("discount")] public decimal? Discount { get; set; }
    [JsonPropertyName("amount")] public decimal? Amount { get; set; }
}
